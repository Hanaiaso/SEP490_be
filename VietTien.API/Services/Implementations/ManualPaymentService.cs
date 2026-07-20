using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Payment;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    /// <summary>
    /// Xử lý ngoại lệ thanh toán SePay — MGR-05
    /// Tuân thủ quy tắc: Payment=PAID không được rollback dù allocation thất bại.
    /// </summary>
    public class ManualPaymentService : IManualPaymentService
    {
        private readonly ApplicationDbContext _context;

        public ManualPaymentService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ──────────────────────────────────────────────────────────
        // MANUAL CONFIRM
        // ──────────────────────────────────────────────────────────
        public async Task<ManualConfirmPaymentResponse> ManualConfirmAsync(
            Guid orderId,
            ManualConfirmPaymentRequest request,
            Guid confirmedByUserId,
            CancellationToken ct = default)
        {
            // ── Bước 1: Load order với đầy đủ liên kết ──
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Transactions)
                .Include(o => o.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order is null)
                throw new KeyNotFoundException("PAYMENT_NOT_FOUND: Không tìm thấy đơn hàng.");

            // ── Bước 2: Chỉ cho phép SePay ──
            if (order.PaymentMethod != PaymentMethod.SePay)
                throw new InvalidOperationException("INVALID_PAYMENT_METHOD: Chỉ giao dịch SePay mới được xác nhận bằng chức năng này.");

            // ── Bước 3: Không cho phép khi đơn đã hủy ──
            if (order.OrderStatus == OrderStatus.Cancelled ||
                order.OrderStatus == OrderStatus.CancelledReallocated)
                throw new InvalidOperationException("ORDER_ALREADY_CANCELLED: Đơn hàng đã bị hủy, không thể xác nhận thanh toán.");

            if (order.PaymentStatus == PaymentStatus.Failed)
                throw new InvalidOperationException("PAYMENT_ALREADY_FAILED: Thanh toán đã ở trạng thái thất bại.");

            // ── Bước 4: Idempotency — đơn đã PAID với cùng mã giao dịch ──
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                var existingTx = order.Transactions
                    .FirstOrDefault(t => t.TransactionId == request.ExternalTransactionId
                                        && t.Source == PaymentTransactionSource.ManualConfirmation);

                if (existingTx is not null)
                {
                    // Trả lại kết quả cũ, không tạo thêm
                    return BuildResponse(order, request.ExternalTransactionId,
                        order.OrderStatus == OrderStatus.Confirmed ? "ALLOCATED" : "FAILED",
                        null, true);
                }

                // Đã PAID nhưng gửi mã giao dịch khác
                throw new InvalidOperationException("PAYMENT_ALREADY_CONFIRMED: Thanh toán đã được xác nhận bằng một giao dịch khác.");
            }

            // ── Bước 5: Bắt buộc có bằng chứng ──
            if (string.IsNullOrWhiteSpace(request.EvidenceUrl))
                throw new ArgumentException("MANUAL_CONFIRM_EVIDENCE_REQUIRED: Bằng chứng thanh toán là bắt buộc khi xác nhận thủ công.");

            // ── Bước 6: Kiểm tra số tiền ──
            if (request.ActualAmount != order.FinalPayment)
                throw new InvalidOperationException(
                    $"PAYMENT_AMOUNT_MISMATCH: Số tiền thực nhận ({request.ActualAmount:N0}) " +
                    $"không khớp với số tiền cần thanh toán ({order.FinalPayment:N0}).");

            // ── Bước 7: Kiểm tra mã giao dịch trùng với đơn KHÁC ──
            var duplicateTx = await _context.PaymentTransactions
                .FirstOrDefaultAsync(t => t.TransactionId == request.ExternalTransactionId, ct);

            if (duplicateTx is not null)
                throw new InvalidOperationException("PAYMENT_TRANSACTION_DUPLICATED: Mã giao dịch ngân hàng đã được sử dụng cho thanh toán khác.");

            // ── Bước 8: Tạo PaymentTransaction + cập nhật Order thành PAID ──
            var transaction = new PaymentTransaction
            {
                OrderId = order.Id,
                TransactionId = request.ExternalTransactionId,
                Amount = request.ActualAmount,
                AccountNumber = string.Empty,
                ReferenceCode = request.TransferContent ?? string.Empty,
                IsSuccess = true,
                IsManualConfirmed = true,
                Source = PaymentTransactionSource.ManualConfirmation,
                EvidenceUrl = request.EvidenceUrl,
                ConfirmedByUserId = confirmedByUserId,
                ConfirmedAt = DateTime.UtcNow,
                Note = request.Note,
                Timestamp = DateTime.UtcNow
            };

            order.PaymentStatus = PaymentStatus.Paid;
            order.ManualConfirmedByUserId = confirmedByUserId;
            order.ManualConfirmedAt = DateTime.UtcNow;
            order.ManualConfirmEvidenceUrl = request.EvidenceUrl;

            await _context.PaymentTransactions.AddAsync(transaction, ct);

            // ── Bước 9: Thử phân bổ tồn kho ──
            var allocationResult = await TryAllocateInventoryAsync(order, ct);

            // ── Bước 10: Cập nhật Order status dựa trên kết quả allocation ──
            if (allocationResult.Success)
            {
                order.OrderStatus = OrderStatus.Confirmed;
                await CreatePickTaskAsync(order, ct);
            }
            else
            {
                order.OrderStatus = OrderStatus.PaidReviewRequired;
                await CreateOrUpdatePaymentExceptionAsync(order, allocationResult.ErrorCode, allocationResult.ErrorMessage, ct);
            }

            // ── Bước 11: SaveChanges — 1 lần duy nhất ──
            await _context.SaveChangesAsync(ct);

            return BuildResponse(
                order,
                request.ExternalTransactionId,
                allocationResult.Success ? "ALLOCATED" : "FAILED",
                allocationResult.Success ? null : allocationResult.ErrorCode,
                false);
        }

        // ──────────────────────────────────────────────────────────
        // RETRY ALLOCATION
        // ──────────────────────────────────────────────────────────
        public async Task<ManualConfirmPaymentResponse> RetryAllocationAsync(
            Guid orderId,
            Guid retryByUserId,
            string? note,
            CancellationToken ct = default)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Transactions)
                .Include(o => o.PaymentExceptions)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order is null)
                throw new KeyNotFoundException("PAYMENT_NOT_FOUND: Không tìm thấy đơn hàng.");

            // Chỉ retry khi đã PAID nhưng chưa confirm allocation
            if (order.PaymentStatus != PaymentStatus.Paid)
                throw new InvalidOperationException("ORDER_NOT_PAID: Chỉ retry khi đơn đã thanh toán (PAID).");

            if (order.OrderStatus != OrderStatus.PaidReviewRequired)
                throw new InvalidOperationException("ORDER_NOT_IN_REVIEW: Chỉ retry khi đơn đang ở trạng thái PAID_REVIEW_REQUIRED.");

            // Thử phân bổ lại
            var allocationResult = await TryAllocateInventoryAsync(order, ct);

            if (allocationResult.Success)
            {
                order.OrderStatus = OrderStatus.Confirmed;
                await CreatePickTaskAsync(order, ct);

                // Đánh dấu PaymentException là RESOLVED
                var openException = order.PaymentExceptions.FirstOrDefault(pe => pe.Status == "OPEN");
                if (openException is not null)
                {
                    openException.Status = "RESOLVED";
                    openException.ResolvedAt = DateTime.UtcNow;
                    openException.ResolvedByUserId = retryByUserId;
                }
            }
            else
            {
                // Tăng RetryCount
                var openException = order.PaymentExceptions.FirstOrDefault(pe => pe.Status == "OPEN");
                if (openException is not null)
                {
                    openException.RetryCount++;
                    openException.LastRetryAt = DateTime.UtcNow;
                    openException.LastRetryByUserId = retryByUserId;
                    openException.Description = note;
                }
                else
                {
                    await CreateOrUpdatePaymentExceptionAsync(order, allocationResult.ErrorCode, allocationResult.ErrorMessage, ct);
                }
            }

            await _context.SaveChangesAsync(ct);

            var externalTxId = order.Transactions
                .FirstOrDefault(t => t.Source == PaymentTransactionSource.ManualConfirmation)?.TransactionId
                ?? "N/A";

            return BuildResponse(
                order,
                externalTxId,
                allocationResult.Success ? "ALLOCATED" : "FAILED",
                allocationResult.Success ? null : allocationResult.ErrorCode,
                false);
        }

        // ──────────────────────────────────────────────────────────
        // GET EXCEPTIONS LIST
        // ──────────────────────────────────────────────────────────
        public async Task<List<SePayExceptionItemDto>> GetExceptionsAsync(CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-30); // Đơn SePay pending > 30 phút

            var exceptions = await _context.Orders
                .Include(o => o.CustomerProfile)
                    .ThenInclude(cp => cp.AssignedSalesStaff)
                .Include(o => o.PaymentExceptions)
                .Include(o => o.Transactions)
                .Where(o =>
                    o.PaymentMethod == PaymentMethod.SePay &&
                    (
                        // TH1: Pending quá lâu (chưa webhook)
                        (o.PaymentStatus == PaymentStatus.Pending && o.CreatedAt < cutoff) ||
                        // TH2: Đã PAID nhưng chưa phân bổ được tồn
                        (o.PaymentStatus == PaymentStatus.Paid && o.OrderStatus == OrderStatus.PaidReviewRequired)
                    )
                )
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync(ct);

            return exceptions.Select(o =>
            {
                var openEx = o.PaymentExceptions.FirstOrDefault(pe => pe.Status == "OPEN");
                var manualTx = o.Transactions.FirstOrDefault(t => t.Source == PaymentTransactionSource.ManualConfirmation);
                var customerName = o.CustomerProfile?.User?.FullName
                                   ?? o.CustomerProfile?.Representative
                                   ?? o.CustomerProfile?.CompanyName
                                   ?? "Khách hàng";
                var assignedName = o.CustomerProfile?.AssignedSalesStaff?.FullName;

                var reason = o.PaymentStatus == PaymentStatus.Pending
                    ? "Webhook SePay chưa nhận hoặc không tìm thấy đơn"
                    : (openEx?.ReasonCode ?? "INSUFFICIENT_AVAILABLE_STOCK");

                return new SePayExceptionItemDto
                {
                    OrderId = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = customerName,
                    AssignedSalesStaffName = assignedName,
                    FinalPayment = o.FinalPayment,
                    PaymentStatus = o.PaymentStatus.ToString(),
                    OrderStatus = o.OrderStatus.ToString(),
                    ExceptionReason = reason,
                    RetryCount = openEx?.RetryCount ?? 0,
                    CreatedAt = o.CreatedAt,
                    LastRetryAt = openEx?.LastRetryAt,
                    ExternalTransactionId = manualTx?.TransactionId,
                    EvidenceUrl = manualTx?.EvidenceUrl
                };
            }).ToList();
        }

        // ──────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ──────────────────────────────────────────────────────────

        private record AllocationResult(bool Success, string ErrorCode = "", string ErrorMessage = "");

        /// <summary>
        /// Thử phân bổ tồn kho cho toàn bộ OrderItem.
        /// Nếu bất kỳ SKU nào không đủ tồn → toàn bộ fail (không làm âm tồn).
        /// </summary>
        private async Task<AllocationResult> TryAllocateInventoryAsync(Order order, CancellationToken ct)
        {
            // Load tất cả inventory cho các product trong đơn
            var productIds = order.OrderItems.Select(oi => oi.ProductId).Distinct().ToList();
            var inventories = await _context.Inventories
                .Where(inv => inv.ProductId != null && productIds.Contains(inv.ProductId.Value))
                .ToListAsync(ct);

            // Kiểm tra trước — không sửa gì cả nếu có SKU thiếu tồn
            foreach (var item in order.OrderItems)
            {
                var inv = inventories.FirstOrDefault(i => i.ProductId == item.ProductId);
                if (inv is null)
                    return new AllocationResult(false, "INSUFFICIENT_AVAILABLE_STOCK",
                        $"Không tìm thấy tồn kho cho sản phẩm {item.Product?.Name ?? item.ProductId.ToString()}.");

                // AvailableQuantity là computed property, tính thủ công
                var available = Math.Max(0,
                    inv.OnHandQuantity - inv.ReservedQuantity - inv.AllocatedQuantity
                    - inv.DamagedQuantity - inv.QuarantineQuantity);

                if (available < item.Quantity)
                    return new AllocationResult(false, "INSUFFICIENT_AVAILABLE_STOCK",
                        $"Sản phẩm '{item.Product?.Name}' chỉ còn {available} đơn vị khả dụng, cần {item.Quantity}.");
            }

            // Đủ tồn — thực hiện allocation
            foreach (var item in order.OrderItems)
            {
                var inv = inventories.First(i => i.ProductId == item.ProductId);
                inv.AllocatedQuantity += item.Quantity;
            }

            return new AllocationResult(true);
        }

        /// <summary>Tạo PickTask cho đơn hàng sau khi allocation thành công</summary>
        private async Task CreatePickTaskAsync(Order order, CancellationToken ct)
        {
            // Lấy warehouse đầu tiên có tồn (đơn giản hoá — trong production sẽ chọn theo FIFO/warehouse cụ thể)
            var warehouseId = await _context.Inventories
                .Where(inv => inv.ProductId != null && order.OrderItems.Select(oi => oi.ProductId).Contains(inv.ProductId.Value))
                .Select(inv => inv.WarehouseLocation.WarehouseId)
                .FirstOrDefaultAsync(ct);

            if (warehouseId == Guid.Empty) return;

            var existingPickTask = await _context.PickTasks
                .AnyAsync(pt => pt.OrderId == order.Id && pt.Status == PickTaskStatus.Pending, ct);

            if (!existingPickTask)
            {
                var pickTask = new PickTask
                {
                    OrderId = order.Id,
                    WarehouseId = warehouseId,
                    Status = PickTaskStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Note = "[MGR-05] Tạo tự động sau khi xác nhận thanh toán thủ công",
                    Items = order.OrderItems.Select(oi => new PickTaskItem
                    {
                        ProductId = oi.ProductId,
                        QuantityToPick = oi.Quantity,
                        PickedQuantity = 0
                    }).ToList()
                };

                await _context.PickTasks.AddAsync(pickTask, ct);
            }
        }

        /// <summary>Tạo hoặc cập nhật PaymentException khi allocation thất bại</summary>
        private async Task CreateOrUpdatePaymentExceptionAsync(
            Order order, string reasonCode, string description, CancellationToken ct)
        {
            var existing = await _context.PaymentExceptions
                .FirstOrDefaultAsync(pe => pe.OrderId == order.Id && pe.Status == "OPEN", ct);

            if (existing is not null)
            {
                existing.ReasonCode = reasonCode;
                existing.Description = description;
            }
            else
            {
                await _context.PaymentExceptions.AddAsync(new PaymentException
                {
                    OrderId = order.Id,
                    ReasonCode = reasonCode,
                    Description = description,
                    Status = "OPEN",
                    RetryCount = 0,
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }
        }

        private static ManualConfirmPaymentResponse BuildResponse(
            Order order,
            string externalTransactionId,
            string allocationStatus,
            string? exceptionCode,
            bool isCachedIdempotent)
        {
            string message;
            if (allocationStatus == "ALLOCATED")
                message = isCachedIdempotent
                    ? "Thanh toán đã được xác nhận trước đó."
                    : "Đã xác nhận thanh toán và phân bổ tồn kho thành công.";
            else
                message = "Đã xác nhận nhận tiền nhưng chưa thể phân bổ đủ tồn kho. Đơn hàng cần xem xét lại.";

            return new ManualConfirmPaymentResponse
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                ExternalTransactionId = externalTransactionId,
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderStatus = order.OrderStatus.ToString(),
                AllocationStatus = allocationStatus,
                ExceptionCode = exceptionCode,
                Message = message,
                ConfirmedAt = order.ManualConfirmedAt ?? DateTime.UtcNow
            };
        }
    }
}
