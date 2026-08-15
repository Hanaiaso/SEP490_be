using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Cart;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;
using VietTien.API.Exceptions;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ICartService _cartService;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ISystemConfigService _systemConfigService;
        private readonly IDiscountTierService _discountTierService;
        private readonly IInventoryReservationService _inventoryReservationService;

        public OrderService(
            IUnitOfWork unitOfWork,
            ApplicationDbContext context,
            IConfiguration configuration,
            ICartService cartService,
            IEmailService emailService,
            INotificationService notificationService,
            ICloudinaryService cloudinaryService,
            ISystemConfigService systemConfigService,
            IDiscountTierService discountTierService,
            IInventoryReservationService inventoryReservationService)
        {
            _unitOfWork = unitOfWork;
            _context = context;
            _configuration = configuration;
            _cartService = cartService;
            _emailService = emailService;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
            _systemConfigService = systemConfigService;
            _discountTierService = discountTierService;
            _inventoryReservationService = inventoryReservationService;
        }

        private async Task<CustomerProfile> GetCustomerProfileAsync(Guid userId)
        {
            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null)
                throw new KeyNotFoundException("Customer Profile not found.");
            return profile;
        }

        // UC-13/BR-004: đơn đầu tiên của khách + SĐT chưa xác thực OTP -> bắt buộc verify trước khi đặt hàng.
        private async Task<bool> RequiresFirstOrderPhoneOtpAsync(CustomerProfile profile)
        {
            if (profile.User.IsPhoneVerified) return false;
            var hasExistingOrder = await _context.Orders.AnyAsync(o => o.CustomerProfileId == profile.Id);
            return !hasExistingOrder;
        }

        private static readonly HashSet<string> StaffRolesWithOrderAccess = new(StringComparer.OrdinalIgnoreCase)
        {
            "SalesStaff", "SalesManager", "AccountingStaff", "CEO", "Admin"
        };

        /// <summary>
        /// Chặn IDOR: chỉ khách hàng sở hữu đơn hàng hoặc nhân viên nghiệp vụ mới được truy cập
        /// thông tin thanh toán/hóa đơn của đơn hàng.
        /// </summary>
        private async Task EnsureOrderAccessAsync(Order order, Guid callerUserId, string callerRole)
        {
            if (StaffRolesWithOrderAccess.Contains(callerRole ?? string.Empty))
                return;

            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(callerUserId);
            if (profile == null || order.CustomerProfileId != profile.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền truy cập đơn hàng này.");
        }

        // Trả kèm negotiatedUnitPrices (null nếu không áp giá đàm phán) để caller snapshot ĐÚNG đơn giá
        // đàm phán vào từng OrderItem — trước đây chỉ trả về tổng chiết khấu nên OrderItem.PriceSnapshot
        // luôn lấy giá niêm yết trong giỏ, khiến chi tiết đơn hàng hiển thị sai giá dù tổng tiền vẫn đúng.
        private async Task<(decimal discountAmount, decimal discountPercentage, Dictionary<Guid, decimal>? negotiatedUnitPrices)> CalculateDiscountAsync(
            decimal totalAmount, Guid customerProfileId, IEnumerable<(Guid ProductId, int Quantity, decimal UnitPrice)> cartLines)
        {
            // Ngưỡng chuyển sang luồng báo giá B2B (CR-01), cấu hình qua QUOTATION_MIN_VALUE, mặc định 100 triệu.
            var quotationMinValueRaw = await _systemConfigService.GetEffectiveValueAsync("QUOTATION_MIN_VALUE");
            var quotationMinValue = decimal.TryParse(quotationMinValueRaw, out var parsedMinValue) ? parsedMinValue : 100_000_000m;

            if (totalAmount >= quotationMinValue)
            {
                // BR-007: ưu tiên áp giá đàm phán nếu khách có báo giá đã duyệt (CustomerAccepted) còn hạn.
                var acceptedVersion = await _context.QuotationVersions
                    .Include(v => v.Items)
                    .Where(v => v.Quotation.CustomerProfileId == customerProfileId
                        && v.Quotation.Status == QuotationStatus.CustomerAccepted
                        && v.Id == v.Quotation.AcceptedVersionId
                        && (v.Quotation.ValidUntil == null || v.Quotation.ValidUntil >= DateTime.UtcNow))
                    .OrderByDescending(v => v.Quotation.RequestDate)
                    .FirstOrDefaultAsync();

                if (acceptedVersion != null)
                {
                    var cartLineList = cartLines.ToList();
                    var negotiatedByProduct = acceptedVersion.Items.ToDictionary(i => i.ProductId, i => i);

                    // DEF-L3-003: giá đàm phán là ĐƠN GIÁ theo SKU, không gắn với số lượng đã duyệt — chỉ
                    // cần mọi dòng trong giỏ thuộc tập SKU đã đàm phán là áp dụng, bất kể số lượng đã đổi.
                    var allLinesNegotiated = cartLineList.Count > 0
                        && cartLineList.All(line => negotiatedByProduct.ContainsKey(line.ProductId));

                    if (allLinesNegotiated)
                    {
                        var negotiatedUnitPrices = negotiatedByProduct.ToDictionary(kv => kv.Key, kv => kv.Value.ProposedUnitPrice);
                        var negotiatedTotal = cartLineList.Sum(line => negotiatedUnitPrices[line.ProductId] * line.Quantity);
                        var negotiatedDiscount = Math.Max(0, totalAmount - negotiatedTotal);
                        var negotiatedPercentage = totalAmount > 0 ? negotiatedDiscount / totalAmount : 0m;
                        return (Math.Round(negotiatedDiscount, 0, MidpointRounding.AwayFromZero), negotiatedPercentage, negotiatedUnitPrices);
                    }
                }
            }

            var discountPercentage = await _discountTierService.GetApplicableDiscountPercentAsync(totalAmount);
            var discountAmount = Math.Round(totalAmount * discountPercentage, 0, MidpointRounding.AwayFromZero);

            return (discountAmount, discountPercentage, null);
        }

        // BR-026: đơn ≥ ngưỡng báo giá B2B mà chưa có báo giá được duyệt thì không được đặt thẳng theo
        // giá niêm yết — phải qua Sales duyệt giá trước (chặn ở BE để FE không thể bypass).
        private async Task EnsureQuotationRequirementMetAsync(decimal totalAmount, Guid customerProfileId)
        {
            var quotationMinValueRaw = await _systemConfigService.GetEffectiveValueAsync("QUOTATION_MIN_VALUE");
            var quotationMinValue = decimal.TryParse(quotationMinValueRaw, out var parsedMinValue) ? parsedMinValue : 100_000_000m;

            if (totalAmount < quotationMinValue) return;

            var hasApprovedQuotation = await _context.Quotations.AnyAsync(q =>
                q.CustomerProfileId == customerProfileId
                && q.Status == QuotationStatus.CustomerAccepted
                && (q.ValidUntil == null || q.ValidUntil >= DateTime.UtcNow));

            if (!hasApprovedQuotation)
                throw new Exception($"Đơn hàng từ {quotationMinValue:N0}đ trở lên bắt buộc phải có báo giá được duyệt. Vui lòng gửi yêu cầu báo giá trước khi đặt hàng.");
        }

        // Khách có thể chỉ chọn một phần giỏ hàng để thanh toán thay vì bắt buộc cả giỏ:
        // cartItemIds = null -> lấy toàn bộ giỏ (hành vi cũ, giữ tương thích các luồng khác);
        // cartItemIds != null -> chỉ lấy đúng các dòng được chọn, báo lỗi nếu chọn rỗng hoặc
        // có dòng không còn tồn tại trong giỏ (vd bị xoá/thay đổi ở tab khác).
        private static List<CartItemDto> ResolveSelectedCartItems(List<CartItemDto> items, List<Guid>? cartItemIds)
        {
            if (cartItemIds == null) return items;

            var idSet = cartItemIds.ToHashSet();
            var existingIds = items.Select(i => i.Id).ToHashSet();
            if (idSet.Any(id => !existingIds.Contains(id)))
                throw new Exception("Một số sản phẩm đã chọn không còn trong giỏ hàng. Vui lòng tải lại giỏ hàng.");

            var selected = items.Where(i => idSet.Contains(i.Id)).ToList();
            if (selected.Count == 0)
                throw new Exception("Vui lòng chọn ít nhất một sản phẩm để thanh toán.");

            return selected;
        }

        public async Task<OrderPreviewDto> GetCheckoutSummaryAsync(Guid userId, List<Guid>? cartItemIds = null)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _cartService.GetCartAsync(userId);

            if (cart == null || !cart.Items.Any())
                throw new Exception("Giỏ hàng trống.");

            var selectedItems = ResolveSelectedCartItems(cart.Items, cartItemIds);

            var baseTotal = selectedItems.Sum(i => i.TotalPrice);
            await EnsureQuotationRequirementMetAsync(baseTotal, profile.Id);
            var (discountAmount, discountPercentage, _) = await CalculateDiscountAsync(
                baseTotal, profile.Id, selectedItems.Select(i => (i.ProductId, i.Quantity, i.UnitPrice)));

            var totalAfterDiscount = baseTotal - discountAmount;
            var requiresVat = !string.IsNullOrEmpty(profile.TaxCode);
            decimal vatPercentage = requiresVat ? 0.10m : 0m;
            decimal vatAmount = Math.Round(totalAfterDiscount * vatPercentage, 0, MidpointRounding.AwayFromZero);
            decimal finalPayment = totalAfterDiscount + vatAmount;

            return new OrderPreviewDto
            {
                TotalAmount = baseTotal,
                DiscountAmount = discountAmount,
                DiscountPercentage = discountPercentage * 100,
                VatPercentage = vatPercentage * 100,
                VatAmount = vatAmount,
                FinalPayment = finalPayment,
                RequiresPhoneOtp = await RequiresFirstOrderPhoneOtpAsync(profile),
                IsPriceExpired = cart.IsPriceExpired,
                Items = selectedItems
            };
        }

        public async Task<OrderResponseDto> PlaceOrderAsync(Guid userId, PlaceOrderRequestDto request)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cartEntity = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            // GH-08/BR-025: chặn đặt hàng khi giỏ đã giữ giá quá 24h — khách phải bấm làm mới giá
            // (RefreshCartPricesAsync) trước, GetCartAsync không tự làm mới UpdatedAt khi đọc.
            if (cartEntity != null && (DateTime.UtcNow - cartEntity.UpdatedAt).TotalHours > 24)
                throw new Exception("Giá trong giỏ hàng đã hết hạn giữ (quá 24h). Vui lòng xem lại giỏ hàng để cập nhật giá mới trước khi đặt hàng.");

            var cart = await _cartService.GetCartAsync(userId);

            if (cart == null || !cart.Items.Any() || cartEntity == null)
                throw new Exception("Giỏ hàng trống.");

            var selectedItems = ResolveSelectedCartItems(cart.Items, request.CartItemIds);

            // UC-13/BR-004 + BR-026: chặn TRƯỚC khi giữ tồn, tránh giữ tồn vô ích cho đơn sẽ bị từ chối.
            if (await RequiresFirstOrderPhoneOtpAsync(profile))
                throw new PhoneVerificationRequiredException("Vui lòng xác thực số điện thoại qua OTP trước khi đặt đơn hàng đầu tiên.");
            await EnsureQuotationRequirementMetAsync(selectedItems.Sum(i => i.TotalPrice), profile.Id);

            Order order;

            // Giữ tồn + tạo Order + ghi CreditTransaction trong 1 transaction duy nhất để rollback được
            // toàn bộ (kể cả tồn đã giữ) nếu bước sau thất bại — ReserveAsync không tự commit riêng nữa.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Giữ mềm tồn kho để tránh oversell khi nhiều khách đặt đồng thời; ném Exception nếu thiếu hàng.
                await _inventoryReservationService.ReserveAsync(
                    selectedItems.Select(i => (i.ProductId, i.Quantity)));

                var baseTotal = selectedItems.Sum(i => i.TotalPrice);
                var (discountAmount, discountPercentage, negotiatedUnitPrices) = await CalculateDiscountAsync(
                    baseTotal, profile.Id, selectedItems.Select(i => (i.ProductId, i.Quantity, i.UnitPrice)));
                var totalAfterDiscount = baseTotal - discountAmount;
                // Cùng nguồn sự thật với GetCheckoutSummaryAsync: VAT áp theo hồ sơ có MST, KHÔNG theo
                // cờ request.RequiresRedInvoice (đó là cờ yêu cầu xuất hóa đơn đỏ, khác việc tính VAT).
                var requiresVat = !string.IsNullOrEmpty(profile.TaxCode);
                decimal vatAmount = requiresVat ? Math.Round(totalAfterDiscount * 0.10m, 0, MidpointRounding.AwayFromZero) : 0m;
                decimal finalPayment = totalAfterDiscount + vatAmount;

                decimal creditApplied = 0m;
                if (profile.AvailableCredit > 0)
                {
                    creditApplied = Math.Min(finalPayment, profile.AvailableCredit);
                    profile.AvailableCredit -= creditApplied;
                    finalPayment -= creditApplied;
                    _unitOfWork.Users.Update(profile.User);
                }

                var orderCode = $"VT{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";

                // Chốt (snapshot) địa chỉ giao hàng khách chọn ngay tại thời điểm đặt đơn: đơn hàng
                // là hồ sơ bất biến, không được phép đổi theo mỗi khi khách cập nhật sổ địa chỉ sau này.
                var shippingAddress = request.AddressId.HasValue
                    ? await _context.Addresses.FirstOrDefaultAsync(a => a.Id == request.AddressId.Value && a.CustomerProfileId == profile.Id)
                    : null;
                shippingAddress ??= await _context.Addresses
                    .Where(a => a.CustomerProfileId == profile.Id)
                    .OrderByDescending(a => a.IsDefault)
                    .FirstOrDefaultAsync();
                var shippingAddressText = shippingAddress != null
                    ? $"{shippingAddress.SpecificAddress}, {shippingAddress.Ward}, {shippingAddress.District}, {shippingAddress.City}"
                    : profile.CompanyAddress;

                var orderStatus = request.PaymentMethod == PaymentMethod.COD ? OrderStatus.PendingConfirmation : OrderStatus.Draft;
                var paymentStatus = PaymentStatus.Pending;
                var fulfillmentStatus = request.PaymentMethod == PaymentMethod.COD ? FulfillmentStatus.Reserved : FulfillmentStatus.Unallocated;
                DateTime? confirmedAt = null;

                // Trả đủ toàn bộ bằng Credit -> coi như đã thanh toán/xác nhận ngay, bỏ qua Draft/PendingConfirmation.
                if (finalPayment == 0)
                {
                    paymentStatus = PaymentStatus.Paid;
                    orderStatus = OrderStatus.Confirmed;
                    confirmedAt = DateTime.UtcNow;
                }

                order = new Order
                {
                    CustomerProfileId = profile.Id,
                    OrderCode = orderCode,
                    TotalAmount = baseTotal,
                    DiscountAmount = discountAmount,
                    VatAmount = vatAmount,
                    CreditApplied = creditApplied,
                    FinalPayment = finalPayment,
                    PaymentMethod = request.PaymentMethod,
                    PaymentStatus = paymentStatus,
                    OrderStatus = orderStatus,
                    ConfirmedAt = confirmedAt,
                    FulfillmentStatus = fulfillmentStatus,
                    CreatedAt = DateTime.UtcNow,
                    RequiresRedInvoice = request.RequiresRedInvoice,
                    SalesStaffId = profile.AssignedSalesStaffId, // Snapshot Sale phụ trách tại thời điểm tạo đơn (LUỒNG 7)
                    ShippingAddress = shippingAddressText
                };

                foreach (var item in selectedItems)
                {
                    // Snapshot đúng đơn giá đàm phán (nếu áp dụng) thay vì luôn lấy giá niêm yết trong
                    // giỏ — nếu không, chi tiết đơn hàng sẽ hiển thị sai giá dù tổng tiền đơn vẫn đúng
                    // (do DiscountAmount đã được tính đúng ở cấp tổng đơn).
                    var unitPrice = negotiatedUnitPrices != null && negotiatedUnitPrices.TryGetValue(item.ProductId, out var negotiatedPrice)
                        ? negotiatedPrice
                        : item.UnitPrice;

                    order.OrderItems.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        PriceSnapshot = unitPrice,
                        CostSnapshot = 0
                    });
                }

                await _unitOfWork.Orders.CreateOrderAsync(order);

                // Chỉ xoá khỏi giỏ đúng những dòng vừa được đặt — các dòng khách chưa chọn thanh toán
                // phải còn nguyên trong giỏ để khách thanh toán riêng ở lượt sau.
                if (request.CartItemIds == null)
                {
                    await _unitOfWork.Carts.ClearCartAsync(cartEntity.Id);
                }
                else
                {
                    var selectedIdSet = selectedItems.Select(i => i.Id).ToHashSet();
                    foreach (var entityItem in cartEntity.Items.Where(ci => selectedIdSet.Contains(ci.Id)).ToList())
                    {
                        _unitOfWork.Carts.RemoveCartItem(entityItem);
                    }
                }
                await _unitOfWork.SaveChangesAsync();

                if (creditApplied > 0)
                {
                    _context.CreditTransactions.Add(new CreditTransaction
                    {
                        CustomerProfileId = profile.Id,
                        Amount = -creditApplied,
                        Description = $"Thanh toán cho đơn hàng {order.OrderCode}",
                        OrderId = order.Id,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                // Rollback nguyên tử: undo cả tồn đã giữ (ReserveAsync tham gia cùng transaction) lẫn Order/CreditTransaction.
                await transaction.RollbackAsync();
                throw;
            }

            try
            {
                if (profile.AssignedSalesStaffId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_02_NewOrder,
                        profile.AssignedSalesStaffId.Value,
                        $"Đơn hàng mới {order.OrderCode}",
                        $"Khách hàng {profile.User.FullName} vừa đặt đơn hàng trị giá {order.FinalPayment:N0}đ.",
                        order.Id,
                        "Order"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OrderService] Error sending new order notification: {ex.Message}");
            }

            return new OrderResponseDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                FinalPayment = order.FinalPayment,
                PaymentMethod = order.PaymentMethod,
                SePayQrUrl = null // Lấy QR qua API khác
            };
        }

        public async Task<SePayQrResponseDto> GenerateSePayQrAsync(Guid orderId, Guid callerUserId, string callerRole)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            await EnsureOrderAccessAsync(order, callerUserId, callerRole);

            if (order.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("Đơn hàng đã thanh toán, không thể sinh mã QR mới.");

            var bankAccount = await _systemConfigService.GetEffectiveValueAsync("SEPAY_BANK_ACCOUNT") ?? _configuration["SePaySettings:BankAccount"];
            var bankId = await _systemConfigService.GetEffectiveValueAsync("SEPAY_BANK_ID") ?? _configuration["SePaySettings:BankId"];
            
            var qrUrl = $"https://qr.sepay.vn/img?acc={bankAccount}&bank={bankId}&amount={(int)order.FinalPayment}&des={order.OrderCode}";

            return new SePayQrResponseDto
            {
                QrImageUrl = qrUrl,
                TransferContent = order.OrderCode
            };
        }

        public async Task ProcessSePayWebhookAsync(SePayWebhookDto payload, string providedToken)
        {
            // GH-01/SEC-03: không bypass theo môi trường — token sai/thiếu luôn bị từ chối.
            var apiToken = await _systemConfigService.GetEffectiveValueAsync("SEPAY_API_TOKEN") ?? _configuration["SePaySettings:ApiToken"];
            if (providedToken != apiToken)
                throw new UnauthorizedAccessException("Token không hợp lệ.");

            var transferContentText = !string.IsNullOrEmpty(payload.content) ? payload.content : payload.transferContent;
            var orderCode = ExtractOrderCode(transferContentText);
            if (string.IsNullOrEmpty(orderCode)) return;

            var order = await _unitOfWork.Orders.GetOrderByCodeAsync(orderCode);
            if (order == null || order.PaymentStatus == PaymentStatus.Paid) return;

            // SRS BV-01: đối soát phải khớp CHÍNH XÁC với FinalPayment. Trả thiếu HOẶC trả thừa đều
            // không được tự động set Paid — phải để lại dấu vết (PaymentException) cho nhân viên đối
            // soát, không được return im lặng (tiền đã thật sự vào tài khoản công ty).
            if (payload.transferAmount != order.FinalPayment)
            {
                var mismatchRefCode = !string.IsNullOrEmpty(payload.referenceCode) ? payload.referenceCode : payload.referenceNumber;
                string anomalyMessage;
                if (payload.transferAmount < order.FinalPayment)
                {
                    var shortfall = order.FinalPayment - payload.transferAmount;
                    anomalyMessage = $"Webhook SePay báo nhận {payload.transferAmount:N0}đ (mã đối soát: {mismatchRefCode ?? "N/A"}) cho đơn {order.OrderCode} nhưng còn THIẾU {shortfall:N0}đ so với FinalPayment {order.FinalPayment:N0}đ. Đơn giữ nguyên Pending, cần đối soát thủ công.";
                    await CreateOrUpdatePaymentExceptionAsync(order, "UNDERPAYMENT", anomalyMessage);
                }
                else
                {
                    var overage = payload.transferAmount - order.FinalPayment;
                    anomalyMessage = $"Webhook SePay báo nhận {payload.transferAmount:N0}đ (mã đối soát: {mismatchRefCode ?? "N/A"}) cho đơn {order.OrderCode} nhưng THỪA {overage:N0}đ so với FinalPayment {order.FinalPayment:N0}đ. Không tự động ghi Paid, cần đối soát/hoàn tiền thủ công.";
                    await CreateOrUpdatePaymentExceptionAsync(order, "OVERPAYMENT", anomalyMessage);
                }
                await _unitOfWork.SaveChangesAsync();
                await NotifyPaymentAnomalyAsync(order, "Thanh toán không khớp đơn hàng", anomalyMessage);
                return;
            }

            // Chống trùng khi SePay gửi lại (redelivery) cùng một giao dịch ngân hàng gần như đồng thời:
            // nếu mã đối soát này đã được ghi nhận cho đơn hàng thì bỏ qua, không tạo PaymentTransaction thứ 2.
            var refCode = !string.IsNullOrEmpty(payload.referenceCode) ? payload.referenceCode : payload.referenceNumber;
            if (!string.IsNullOrEmpty(refCode))
            {
                var alreadyRecorded = await _context.PaymentTransactions
                    .AnyAsync(t => t.OrderId == order.Id && t.TransactionId == refCode);
                if (alreadyRecorded) return;
            }

            if (order.OrderStatus == OrderStatus.Cancelled || order.OrderStatus == OrderStatus.CancelledReallocated)
            {
                // Đơn đã hủy trước khi tiền về (vd. hết hạn giữ chỗ) -> KHÔNG set Paid/tạo PaymentTransaction
                // (tránh vừa Cancelled vừa Paid), chỉ mở ngoại lệ để đối soát/hoàn tiền thủ công.
                var cancelledAnomalyMessage = $"Webhook SePay báo nhận {payload.transferAmount:N0}đ (mã đối soát: {refCode ?? "N/A"}) cho đơn {order.OrderCode} nhưng đơn đã ở trạng thái {order.OrderStatus} trước đó.";
                await CreateOrUpdatePaymentExceptionAsync(order, "PAID_AFTER_CANCELLATION", cancelledAnomalyMessage);
                await _unitOfWork.SaveChangesAsync();
                await NotifyPaymentAnomalyAsync(order, "Nhận tiền cho đơn đã hủy", cancelledAnomalyMessage);
                return;
            }

            order.PaymentStatus = PaymentStatus.Paid;
            // Thiếu dòng này thì AmountPaid vẫn = 0 dù đã Paid: lúc giao hàng, tính công nợ dựa trên
            // AmountPaid sẽ coi đơn chưa trả đồng nào, tạo nợ oan = cả FinalPayment.
            order.AmountPaid = payload.transferAmount;

            var transaction = new PaymentTransaction
            {
                OrderId = order.Id,
                TransactionId = !string.IsNullOrEmpty(refCode) ? refCode : Guid.NewGuid().ToString(),
                Amount = payload.transferAmount,
                AccountNumber = payload.accountNumber,
                ReferenceCode = transferContentText,
                IsSuccess = true,
                Timestamp = DateTime.UtcNow
            };
            await _context.PaymentTransactions.AddAsync(transaction);

            if (order.OrderCode.StartsWith("VT-DT-", StringComparison.OrdinalIgnoreCase))
            {
                order.OrderStatus = OrderStatus.Completed;
            }
            else
            {
                var allocationResult = await TryAllocateInventoryAsync(order);
                if (allocationResult.Success)
                {
                    order.OrderStatus = OrderStatus.Confirmed;
                    order.ConfirmedAt = DateTime.UtcNow;
                    await CreatePickTaskAsync(order);
                }
                else
                {
                    order.OrderStatus = OrderStatus.PaidReviewRequired;
                    await CreateOrUpdatePaymentExceptionAsync(order, allocationResult.ErrorCode, allocationResult.ErrorMessage);
                }
            }

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();

            try
            {
                var fullOrder = await _context.Orders
                    .Include(o => o.CustomerProfile)
                    .ThenInclude(c => c.User)
                    .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                    .FirstOrDefaultAsync(o => o.Id == order.Id);

                if (fullOrder != null)
                {
                    var customerEmail = fullOrder.CustomerProfile?.User?.Email ?? fullOrder.CustomerProfile?.InvoiceEmail;
                    var customerName = fullOrder.CustomerProfile?.Representative ?? fullOrder.CustomerProfile?.CompanyName ?? "Khách hàng";
                    if (!string.IsNullOrEmpty(customerEmail))
                    {
                        await _emailService.SendOrderInvoiceEmailAsync(customerEmail, customerName, fullOrder, isSalesNotify: false);
                    }

                    var salesEmail = _configuration["EmailSettings:SenderEmail"] ?? "sales@viettien.vn";
                    await _emailService.SendOrderInvoiceEmailAsync(salesEmail, "Bộ phận Bán hàng VietTien", fullOrder, isSalesNotify: true);

                    if (fullOrder.CustomerProfile?.AssignedSalesStaffId != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_05_SePaySuccess,
                            fullOrder.CustomerProfile.AssignedSalesStaffId.Value,
                            $"Thanh toán thành công {fullOrder.OrderCode}",
                            $"Khách hàng đã thanh toán {payload.transferAmount:N0}đ qua SePay cho đơn hàng {fullOrder.OrderCode}.",
                            fullOrder.Id,
                            "Order"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SePay Webhook] Error sending invoice emails: {ex.Message}");
            }
        }

        private record InventoryAllocationResult(bool Success, string ErrorCode = "", string ErrorMessage = "");

        /// <summary>
        /// Thử phân bổ tồn kho cho đơn đã Paid qua SePay — trả phần giữ mềm (Reserved) rồi Allocate,
        /// dùng chung cơ chế atomic guarded UPDATE của IInventoryReservationService (giống ManualPaymentService)
        /// để tránh đếm trùng và tránh oversell khi nhiều đơn cùng tranh chấp tồn kho ít.
        /// </summary>
        private async Task<InventoryAllocationResult> TryAllocateInventoryAsync(Order order)
        {
            var items = order.OrderItems.Select(oi => (oi.ProductId, oi.Quantity)).ToList();
            try
            {
                await _inventoryReservationService.ReleaseReservedAsync(items);
                await _inventoryReservationService.AllocateAsync(items);
                return new InventoryAllocationResult(true);
            }
            catch (Exception ex)
            {
                return new InventoryAllocationResult(false, "INSUFFICIENT_AVAILABLE_STOCK", ex.Message);
            }
        }

        /// <summary>Tạo PickTask cho đơn hàng sau khi allocation thành công (webhook SePay tự động).</summary>
        private async Task CreatePickTaskAsync(Order order)
        {
            var warehouseId = await _context.Inventories
                .Where(inv => inv.ProductId != null && order.OrderItems.Select(oi => oi.ProductId).Contains(inv.ProductId.Value))
                .Select(inv => inv.WarehouseLocation.WarehouseId)
                .FirstOrDefaultAsync();

            if (warehouseId == Guid.Empty) return;

            var existingPickTask = await _context.PickTasks
                .AnyAsync(pt => pt.OrderId == order.Id && pt.Status == PickTaskStatus.Pending);

            if (!existingPickTask)
            {
                var pickTask = new PickTask
                {
                    OrderId = order.Id,
                    WarehouseId = warehouseId,
                    Status = PickTaskStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Note = "Tạo tự động sau khi xác nhận thanh toán SePay (webhook)",
                    Items = order.OrderItems.Select(oi => new PickTaskItem
                    {
                        ProductId = oi.ProductId,
                        QuantityToPick = oi.Quantity,
                        PickedQuantity = 0
                    }).ToList()
                };

                await _context.PickTasks.AddAsync(pickTask);
            }
        }

        /// <summary>Tạo hoặc cập nhật PaymentException khi allocation thất bại hoặc khi tiền về sau khi đơn đã hủy.</summary>
        private async Task CreateOrUpdatePaymentExceptionAsync(Order order, string reasonCode, string description)
        {
            var existing = await _context.PaymentExceptions
                .FirstOrDefaultAsync(pe => pe.OrderId == order.Id && pe.Status == "OPEN");

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
                });
            }
        }

        // PaymentException đã được lưu thành công ở trên -> lỗi gửi notification không được làm
        // fail luồng xử lý webhook, chỉ log để theo dõi.
        private async Task NotifyPaymentAnomalyAsync(Order order, string title, string message)
        {
            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_32_PaymentAnomaly,
                    SystemRole.AccountingStaff,
                    title,
                    message,
                    order.Id,
                    "Order");

                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_32_PaymentAnomaly,
                    SystemRole.SalesManager,
                    title,
                    message,
                    order.Id,
                    "Order");
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[OrderService] Error sending payment anomaly notification: {notifyEx.Message}");
            }
        }

        public async Task<PaymentStatusResponseDto> GetPaymentStatusAsync(Guid orderId, Guid callerUserId, string callerRole)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            await EnsureOrderAccessAsync(order, callerUserId, callerRole);

            return new PaymentStatusResponseDto
            {
                Status = order.PaymentStatus.ToString()
            };
        }

        public async Task<DirectOrderResponseDto> PlaceDirectOrderAsync(PlaceDirectOrderRequestDto request, Guid staffId)
        {
            if (request.Items == null || !request.Items.Any())
            {
                throw new Exception("Danh sách sản phẩm trống.");
            }

            var phone = request.PhoneNumber?.Trim();
            if (string.IsNullOrEmpty(phone))
            {
                phone = "0000000000";
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^0\d{9}$"))
            {
                throw new InvalidOperationException("Số điện thoại không đúng định dạng.");
            }

            CustomerProfile? customerProfile = null;
            var existingUser = await _context.Users
                .Include(u => u.CustomerProfile)
                .FirstOrDefaultAsync(u => u.PhoneNumber == phone);

            if (existingUser != null)
            {
                customerProfile = existingUser.CustomerProfile;
                if (customerProfile == null)
                {
                    customerProfile = new CustomerProfile
                    {
                        UserId = existingUser.Id,
                        Representative = request.CustomerName,
                        CompanyName = request.CustomerName,
                        CompanyAddress = request.Address
                    };
                    await _unitOfWork.Users.AddCustomerProfileAsync(customerProfile);
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            else
            {
                var guidStr = Guid.NewGuid().ToString("N").Substring(0, 8);
                var newUser = new User
                {
                    FullName = request.CustomerName,
                    PhoneNumber = phone,
                    Email = $"guest_{phone}_{guidStr}@store.viettien.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    Role = SystemRole.Customer,
                    IsEmailVerified = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Users.AddAsync(newUser);
                await _unitOfWork.SaveChangesAsync();

                customerProfile = new CustomerProfile
                {
                    UserId = newUser.Id,
                    Representative = request.CustomerName,
                    CompanyName = request.CustomerName,
                    CompanyAddress = request.Address
                };
                await _unitOfWork.Users.AddCustomerProfileAsync(customerProfile);
                await _unitOfWork.SaveChangesAsync();
            }

            var orderCode = $"VT-DT-{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";

            string? pdfUrl = null;
            if (!string.IsNullOrEmpty(request.InvoicePdfBase64))
            {
                try
                {
                    var base64Data = request.InvoicePdfBase64;
                    if (base64Data.Contains(","))
                    {
                        base64Data = base64Data.Split(',')[1];
                    }

                    var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var invoicesPath = Path.Combine(wwwrootPath, "invoices");
                    if (!Directory.Exists(invoicesPath))
                    {
                        Directory.CreateDirectory(invoicesPath);
                    }

                    var filePath = Path.Combine(invoicesPath, $"{orderCode}.pdf");
                    var fileBytes = Convert.FromBase64String(base64Data);
                    await File.WriteAllBytesAsync(filePath, fileBytes);
                    
                    pdfUrl = $"/invoices/{orderCode}.pdf";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving PDF invoice: {ex.Message}");
                }
            }

            var order = new Order
            {
                CustomerProfileId = customerProfile.Id,
                OrderCode = orderCode,
                TotalAmount = request.TotalAmount,
                DiscountAmount = request.DiscountAmount,
                VatAmount = request.VatAmount,
                FinalPayment = request.FinalPayment,
                PaymentMethod = request.PaymentMethod,
                // Cash tại quầy chưa coi là đã thanh toán ngay — chỉ Paid sau khi nhân viên bấm
                // "Xác nhận đã nhận tiền mặt" (ConfirmDirectOrderPaymentAsync).
                PaymentStatus = PaymentStatus.Pending,
                OrderStatus = request.PaymentMethod == PaymentMethod.COD ? OrderStatus.PendingConfirmation : OrderStatus.Draft,
                FulfillmentStatus = request.PaymentMethod == PaymentMethod.COD ? FulfillmentStatus.Reserved : FulfillmentStatus.Unallocated,
                IsExternalOrder = true,
                CreatedAt = DateTime.UtcNow,
                RequiresRedInvoice = false,
                InvoicePdfUrl = pdfUrl,
                // BUGFIX: trước đây gán theo customerProfile.AssignedSalesStaffId (Sale phụ trách khách
                // trên hồ sơ) — với khách vãng lai/mới (đa số đơn bán tại quầy) trường này luôn null, nên
                // đơn không gắn với ai cả. Mọi truy vấn "Quản lý đơn hàng"/dashboard của Sales Staff đều
                // lọc theo Order.SalesStaffId == chính nhân viên đang đăng nhập -> đơn quầy biến mất khỏi
                // cả 2 màn hình. Đơn bán trực tiếp tại quầy phải gắn với nhân viên đang đứng quầy lập đơn.
                SalesStaffId = staffId,
                ShippingAddress = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim()
            };

            // Đối chiếu số học cơ bản để chặn sai lệch/gõ nhầm giữa các trường tiền client gửi lên
            // (không ép khớp catalog vì nhân viên bán trực tiếp được phép thương lượng giá tại quầy).
            var itemsTotal = request.Items.Sum(i => i.Price * i.Quantity);
            if (itemsTotal != request.TotalAmount)
                throw new Exception("Tổng tiền hàng (TotalAmount) không khớp với tổng đơn giá x số lượng của các mặt hàng.");
            if (request.TotalAmount - request.DiscountAmount + request.VatAmount != request.FinalPayment)
                throw new Exception("Số tiền thanh toán cuối cùng (FinalPayment) không khớp với TotalAmount - DiscountAmount + VatAmount.");

            foreach (var item in request.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null)
                {
                    throw new Exception($"Không tìm thấy sản phẩm với ID: {item.ProductId}");
                }

                order.OrderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceSnapshot = item.Price,
                    CostSnapshot = 0
                });
            }

            // Gộp trừ OnHand (DeductOnHandAsync) + tạo Order vào 1 transaction duy nhất: trước đây
            // DeductOnHandAsync tự commit riêng, nếu SaveChangesAsync tạo Order ngay sau đó thất bại
            // (vd trùng OrderCode, lỗi DB nhất thời) thì tồn kho vật lý đã bị trừ thật (mất tồn) nhưng
            // không có Order tương ứng, không có cách khôi phục. Nay rollback 1 lần undo cả 2.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Trừ thẳng OnHand bằng atomic guarded UPDATE (bán trực tiếp tại quầy — hàng rời kho ngay).
                // Ném Exception nếu có SKU nào không đủ khả dụng, kể cả khi 2 giao dịch tranh chấp đồng thời.
                try
                {
                    await _inventoryReservationService.DeductOnHandAsync(
                        request.Items.Select(i => (i.ProductId, i.Quantity)));
                }
                catch (Exception)
                {
                    throw new Exception("Stock depleted by another transaction. Please refresh inventory data.");
                }

                await _unitOfWork.Orders.CreateOrderAsync(order);
                await _unitOfWork.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return new DirectOrderResponseDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                FinalPayment = order.FinalPayment,
                InvoicePdfUrl = order.InvoicePdfUrl
            };
        }

        private string? ExtractOrderCode(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(VT-?DT-?\d+|VT\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpper() : null;
        }

        public async Task<SalesDashboardStatsDto> GetSalesDashboardStatsAsync(Guid? scopedSalesStaffId = null)
        {
            // "Hôm nay" tính theo giờ Việt Nam (UTC+7), không phải ngày UTC — tránh tính nhầm đơn phát
            // sinh vào sáng sớm giờ VN (vẫn là ngày UTC hôm trước) sang hôm qua.
            var today = DateTime.UtcNow.AddHours(7).Date;

            // 1. KPI aggregates
            // scopedSalesStaffId = null -> SalesManager/Admin xem toàn hệ thống (giữ nguyên hành vi cũ).
            // scopedSalesStaffId có giá trị -> SalesStaff chỉ xem đơn của chính mình (business.md bước 6).
            var ordersQuery = _context.Orders.AsQueryable();
            if (scopedSalesStaffId.HasValue)
                ordersQuery = ordersQuery.Where(o => o.SalesStaffId == scopedSalesStaffId.Value);
            var allOrders = await ordersQuery.ToListAsync();
            var newOrdersCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Draft);
            var processingOrdersCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed);
            var shippingOrdersCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Processing);
            var deliveredTodayCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt.AddHours(7).Date == today);
            var revenueToday = allOrders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt.AddHours(7).Date == today).Sum(o => o.FinalPayment);

            // "Công nợ cần thu" lấy từ sổ công nợ THẬT (CustomerDebts), không phải đơn PaymentStatus=Pending
            // (Pending chỉ là "chờ thanh toán", không phải khoản khách nợ thật).
            var pendingDebtQuery = _context.CustomerDebts.Where(d => d.Status == DebtStatus.InDebt);
            if (scopedSalesStaffId.HasValue)
                pendingDebtQuery = pendingDebtQuery.Where(d => d.Order.SalesStaffId == scopedSalesStaffId.Value);
            var pendingDebt = await pendingDebtQuery.SumAsync(d => (decimal?)d.DebtAmount) ?? 0m;

            var kpiDto = new DashboardKpiDto
            {
                NewOrdersCount = newOrdersCount,
                ProcessingOrdersCount = processingOrdersCount,
                ShippingOrdersCount = shippingOrdersCount,
                DeliveredTodayCount = deliveredTodayCount,
                RevenueToday = revenueToday,
                PendingDebt = pendingDebt
            };

            // 2. Recent orders (top 10)
            var recentOrdersList = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Where(o => scopedSalesStaffId == null || o.SalesStaffId == scopedSalesStaffId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .Select(o => new DashboardOrderDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    CreatedAt = o.CreatedAt,
                    FinalPayment = o.FinalPayment,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    OrderStatus = o.OrderStatus,
                    InvoicePdfUrl = o.InvoicePdfUrl
                })
                .ToListAsync();

            // 3. Urgent orders (top 5 oldest pending orders)
            var urgentOrdersList = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Where(o => (o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed)
                            && (scopedSalesStaffId == null || o.SalesStaffId == scopedSalesStaffId))
                .OrderBy(o => o.CreatedAt)
                .Take(5)
                .ToListAsync();

            var urgentDtos = new List<DashboardUrgentOrderDto>();
            foreach (var o in urgentOrdersList)
            {
                var timeElapsed = DateTime.UtcNow - o.CreatedAt;
                string deadlineText;
                string level;

                if (timeElapsed.TotalMinutes < 60)
                {
                    deadlineText = $"{(int)timeElapsed.TotalMinutes} phút trước";
                    level = "normal";
                }
                else if (timeElapsed.TotalHours < 2)
                {
                    deadlineText = $"{(int)timeElapsed.TotalHours} giờ trước";
                    level = "high";
                }
                else
                {
                    deadlineText = $"{(int)timeElapsed.TotalHours} giờ trước";
                    level = "critical";
                }

                urgentDtos.Add(new DashboardUrgentOrderDto
                {
                    Id = o.OrderCode,
                    Customer = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    Amount = o.FinalPayment,
                    Deadline = deadlineText,
                    Level = level
                });
            }

            // 4. Warehouse Queue (top 5 packing orders)
            var warehouseList = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Include(o => o.OrderItems)
                .Where(o => (o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed)
                            && (scopedSalesStaffId == null || o.SalesStaffId == scopedSalesStaffId))
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new DashboardWarehouseQueueDto
                {
                    Id = o.OrderCode,
                    Customer = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    ItemsCount = o.OrderItems.Sum(oi => oi.Quantity),
                    Status = o.OrderStatus == OrderStatus.Draft ? "Chờ xác nhận" : "Đang đóng gói"
                })
                .ToListAsync();

            // 5. Quote requests (top 5 pending/negotiating quotations)
            var quotationsList = await _context.Quotations
                .Include(q => q.CustomerProfile)
                .Where(q => (q.Status == QuotationStatus.Draft || q.Status == QuotationStatus.Negotiating)
                            && (scopedSalesStaffId == null || q.SalesStaffId == scopedSalesStaffId))
                .OrderByDescending(q => q.RequestDate)
                .Take(5)
                .Select(q => new DashboardQuoteRequestDto
                {
                    Id = q.Id,
                    CustomerName = q.CustomerProfile.Representative ?? q.CustomerProfile.CompanyName ?? "Khách hàng",
                    Value = q.OriginalTotal,
                    RequestDate = q.RequestDate
                })
                .ToListAsync();

            // 6. Top Products (top 5 by revenue)
            var topProductsList = await _context.OrderItems
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.PaymentStatus == PaymentStatus.Paid
                            && (scopedSalesStaffId == null || oi.Order.SalesStaffId == scopedSalesStaffId))
                .GroupBy(oi => new { oi.ProductId, oi.Product.Name })
                .Select(g => new DashboardTopProductDto
                {
                    Name = g.Key.Name,
                    Revenue = g.Sum(oi => oi.Quantity * oi.PriceSnapshot) / 1000000m // convert to triệu đồng
                })
                .OrderByDescending(p => p.Revenue)
                .Take(5)
                .ToListAsync();

            // 7. Last 7 Days Revenue
            var revenueDays = new List<DashboardRevenueDayDto>();
            var sevenDaysAgo = today.AddDays(-6).AddHours(-7); // quy đổi ngày VN về mốc UTC instant để lọc DB
            var paidOrdersIn7Days = await _context.Orders
                .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= sevenDaysAgo
                            && (scopedSalesStaffId == null || o.SalesStaffId == scopedSalesStaffId))
                .ToListAsync();

            // Mục tiêu/ngày = mục tiêu THÁNG (Sales Manager đặt, bảng SalesRevenueTargets) chia đều cho
            // số ngày trong tháng hiện tại — không còn hardcode 5tr/ngày cho mọi Sales Staff.
            var monthlyTargetQuery = _context.SalesRevenueTargets
                .Where(t => t.Year == today.Year && t.Month == today.Month);
            if (scopedSalesStaffId.HasValue)
                monthlyTargetQuery = monthlyTargetQuery.Where(t => t.SalesStaffId == scopedSalesStaffId.Value);
            var monthlyTargetTotal = await monthlyTargetQuery.SumAsync(t => (decimal?)t.TargetAmount) ?? 0m;
            var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            var dailyTarget = Math.Round(monthlyTargetTotal / daysInMonth / 1_000_000m, 2); // triệu đồng/ngày

            var culture = new System.Globalization.CultureInfo("vi-VN");
            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var dayName = culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);
                if (d.Date == today) dayName = "Hôm nay";

                var dailyRevenue = paidOrdersIn7Days
                    .Where(o => o.CreatedAt.AddHours(7).Date == d.Date)
                    .Sum(o => o.FinalPayment) / 1000000m; // convert to triệu đồng

                revenueDays.Add(new DashboardRevenueDayDto
                {
                    Day = dayName,
                    Revenue = Math.Round(dailyRevenue, 2),
                    Target = dailyTarget
                });
            }

            return new SalesDashboardStatsDto
            {
                Kpi = kpiDto,
                RecentOrders = recentOrdersList,
                UrgentOrders = urgentDtos,
                WarehouseQueue = warehouseList,
                QuoteRequests = quotationsList,
                TopProducts = topProductsList,
                Last7DaysRevenue = revenueDays
            };
        }

        private static readonly HashSet<string> ValidDashboardDrillDownMetrics = new(StringComparer.OrdinalIgnoreCase)
        {
            "newOrders", "processing", "shipping", "deliveredToday", "revenueToday", "pendingDebt", "completedOrders", "codSlaBreach"
        };

        // Dùng LẠI đúng điều kiện lọc của GetSalesDashboardStatsAsync để số hiển thị và danh sách
        // khi bấm vào luôn khớp nhau tuyệt đối.
        public async Task<List<DashboardOrderDto>> GetSalesDashboardDrillDownAsync(string metric, Guid? scopedSalesStaffId = null)
        {
            if (string.IsNullOrEmpty(metric) || !ValidDashboardDrillDownMetrics.Contains(metric))
                throw new ArgumentException("Chỉ số không hợp lệ.");

            // "Hôm nay" tính theo giờ Việt Nam (UTC+7), không phải ngày UTC.
            var today = DateTime.UtcNow.AddHours(7).Date;

            if (string.Equals(metric, "codSlaBreach", StringComparison.OrdinalIgnoreCase))
            {
                var todayStart = DateTime.UtcNow.AddHours(7).Date.AddHours(-7);
                var breachedOrderIds = await _context.Notifications
                    .AsNoTracking()
                    .Where(n => n.Type == NotificationType.SYS_04_CodUnconfirmed30m && n.CreatedAt >= todayStart && n.ReferenceId != null)
                    .Select(n => n.ReferenceId!.Value)
                    .Distinct()
                    .ToListAsync();

                var breachQuery = _context.Orders.Include(o => o.CustomerProfile).Where(o => breachedOrderIds.Contains(o.Id));
                if (scopedSalesStaffId.HasValue)
                    breachQuery = breachQuery.Where(o => o.SalesStaffId == scopedSalesStaffId.Value);

                return await breachQuery
                    .OrderByDescending(o => o.CreatedAt)
                    .Select(o => new DashboardOrderDto
                    {
                        Id = o.Id,
                        OrderCode = o.OrderCode,
                        CustomerName = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                        CreatedAt = o.CreatedAt,
                        FinalPayment = o.FinalPayment,
                        PaymentMethod = o.PaymentMethod,
                        PaymentStatus = o.PaymentStatus,
                        OrderStatus = o.OrderStatus,
                        InvoicePdfUrl = o.InvoicePdfUrl
                    })
                    .ToListAsync();
            }

            if (string.Equals(metric, "pendingDebt", StringComparison.OrdinalIgnoreCase))
            {
                var debtQuery = _context.CustomerDebts
                    .Include(d => d.Order)
                    .Include(d => d.CustomerProfile)
                    .Where(d => d.Status == DebtStatus.InDebt);
                if (scopedSalesStaffId.HasValue)
                    debtQuery = debtQuery.Where(d => d.Order.SalesStaffId == scopedSalesStaffId.Value);

                // OverdueDays lưu trong DB chỉ set 1 lần lúc tạo (luôn = 0), không có job cập nhật —
                // sắp xếp theo CreatedAt (nợ cũ nhất trước) thay vì tin field đó, cùng lý do với
                // SalesManagerDashboardService (EF.Functions.DateDiffDay chỉ dịch được sang SQL Server).
                return await debtQuery
                    .OrderBy(d => d.CreatedAt)
                    .Select(d => new DashboardOrderDto
                    {
                        Id = d.Order.Id,
                        OrderCode = d.Order.OrderCode,
                        CustomerName = d.CustomerProfile.Representative ?? d.CustomerProfile.CompanyName ?? "Khách lẻ",
                        CreatedAt = d.CreatedAt,
                        FinalPayment = d.DebtAmount,
                        PaymentMethod = d.Order.PaymentMethod,
                        PaymentStatus = d.Order.PaymentStatus,
                        OrderStatus = d.Order.OrderStatus,
                        InvoicePdfUrl = d.Order.InvoicePdfUrl
                    })
                    .ToListAsync();
            }

            var ordersQuery = _context.Orders.Include(o => o.CustomerProfile).AsQueryable();
            if (scopedSalesStaffId.HasValue)
                ordersQuery = ordersQuery.Where(o => o.SalesStaffId == scopedSalesStaffId.Value);

            ordersQuery = metric.ToLowerInvariant() switch
            {
                "neworders" => ordersQuery.Where(o => o.OrderStatus == OrderStatus.Draft),
                "processing" => ordersQuery.Where(o => o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed),
                "shipping" => ordersQuery.Where(o => o.OrderStatus == OrderStatus.Processing),
                "deliveredtoday" => ordersQuery.Where(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt.AddHours(7).Date == today),
                "revenuetoday" => ordersQuery.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt.AddHours(7).Date == today),
                "completedorders" => ordersQuery.Where(o => o.OrderStatus == OrderStatus.Completed),
                _ => ordersQuery
            };

            return await ordersQuery
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .Select(o => new DashboardOrderDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    CreatedAt = o.CreatedAt,
                    FinalPayment = o.FinalPayment,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    OrderStatus = o.OrderStatus,
                    InvoicePdfUrl = o.InvoicePdfUrl
                })
                .ToListAsync();
        }

        public async Task ConfirmDirectOrderPaymentAsync(Guid orderId)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus == OrderStatus.Cancelled || order.OrderStatus == OrderStatus.CancelledReallocated)
                throw new InvalidOperationException("Đơn hàng đã bị hủy, không thể xác nhận thanh toán.");

            if (order.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("Đơn hàng đã được xác nhận thanh toán trước đó.");

            order.PaymentStatus = PaymentStatus.Paid;
            order.OrderStatus = OrderStatus.Completed; // Delivered upon counter cash payment confirmation

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task UploadInvoicePdfAsync(Guid orderId, string pdfBase64, Guid callerUserId, string callerRole)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            await EnsureOrderAccessAsync(order, callerUserId, callerRole);

            if (!string.IsNullOrEmpty(pdfBase64))
            {
                try
                {
                    // Lưu trên Cloudinary thay vì local: container Azure không đảm bảo file ghi vào đĩa
                    // còn tồn tại sau restart/redeploy, khiến URL /invoices/xxx.pdf trả 404.
                    var pdfUrl = await _cloudinaryService.UploadBase64ImageAsync(
                        pdfBase64,
                        "invoices",
                        order.OrderCode
                    );

                    order.InvoicePdfUrl = pdfUrl;
                    await _unitOfWork.Orders.UpdateOrderAsync(order);
                    await _unitOfWork.SaveChangesAsync();

                    if (order.PaymentMethod == PaymentMethod.COD)
                    {
                        var fullOrder = await _context.Orders
                            .Include(o => o.CustomerProfile)
                            .ThenInclude(c => c.User)
                            .Include(o => o.OrderItems)
                            .ThenInclude(oi => oi.Product)
                            .FirstOrDefaultAsync(o => o.Id == order.Id);

                        if (fullOrder != null)
                        {
                            var customerEmail = fullOrder.CustomerProfile?.User?.Email ?? fullOrder.CustomerProfile?.InvoiceEmail;
                            var customerName = fullOrder.CustomerProfile?.Representative ?? fullOrder.CustomerProfile?.CompanyName ?? "Khách hàng";
                            if (!string.IsNullOrEmpty(customerEmail))
                            {
                                await _emailService.SendOrderInvoiceEmailAsync(customerEmail, customerName, fullOrder, isSalesNotify: false);
                            }

                            var salesEmail = _configuration["EmailSettings:SenderEmail"] ?? "sales@viettien.vn";
                            await _emailService.SendOrderInvoiceEmailAsync(salesEmail, "Bộ phận Bán hàng VietTien", fullOrder, isSalesNotify: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uploading/saving PDF invoice: {ex.Message}");
                    throw;
                }
            }
        }


        public async Task<PagedResultDto<OrderHistoryItemDto>> GetOrderHistoryAsync(
            Guid userId, OrderHistoryQueryDto query)
        {
            var profile = await GetCustomerProfileAsync(userId);

            var pageSize = Math.Max(1, Math.Min(query.PageSize, 50));
            var page     = Math.Max(1, query.Page);

            var (items, totalCount) = await _unitOfWork.Orders.GetOrderHistoryAsync(profile.Id, query);

            const int vatWindowHours = 168;

            var dtos = items.Select(o =>
            {
                var vatDeadline  = o.CreatedAt.AddHours(vatWindowHours);
                var canRequestVat = o.OrderStatus == OrderStatus.Completed
                                    && DateTime.UtcNow < vatDeadline
                                    && o.RedInvoiceStatus == RedInvoiceStatus.None;

                return new OrderHistoryItemDto
                {
                    Id               = o.Id,
                    OrderCode        = o.OrderCode,
                    CreatedAt        = o.CreatedAt,
                    TotalAmount      = o.TotalAmount,
                    DiscountAmount   = o.DiscountAmount,
                    VatAmount        = o.VatAmount,
                    FinalPayment     = o.FinalPayment,
                    PaymentMethod    = o.PaymentMethod.ToString(),
                    PaymentStatus    = o.PaymentStatus.ToString(),
                    OrderStatus      = o.OrderStatus.ToString(),
                    RedInvoiceStatus = o.RedInvoiceStatus.ToString(),
                    ItemCount        = o.OrderItems.Sum(oi => oi.Quantity),
                    HasInvoicePdf    = !string.IsNullOrEmpty(o.InvoicePdfUrl),
                    CanRequestVat    = canRequestVat,
                };
            }).ToList();

            return new PagedResultDto<OrderHistoryItemDto>
            {
                Items      = dtos,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };
        }


        public async Task<SpendingStatsDto> GetSpendingStatsAsync(Guid userId, string period)
        {
            var profile = await GetCustomerProfileAsync(userId);

            // Map kỳ thống kê -> số ngày lùi lại từ hiện tại
            var days = (period?.Trim().ToLower()) switch
            {
                "week"    => 7,
                "quarter" => 90,
                "year"    => 365,
                _         => 30,   // month (mặc định)
            };
            var fromUtc = DateTime.UtcNow.AddDays(-days);

            var orders = await _unitOfWork.Orders.GetPaidOrdersForStatsAsync(profile.Id, fromUtc);

            // Chi tiêu theo tháng (sắp xếp tăng dần theo thời gian)
            var spendingByMonth = orders
                .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new SpendingPointDto
                {
                    Label = $"T{g.Key.Month}",
                    Value = g.Sum(o => o.FinalPayment)
                })
                .ToList();

            // Top 5 sản phẩm theo tổng số lượng đặt
            var topProducts = orders
                .SelectMany(o => o.OrderItems)
                .Where(oi => oi.Product != null)
                .GroupBy(oi => oi.Product.Name)
                .Select(g => new TopProductDto { Name = g.Key, Value = g.Sum(oi => oi.Quantity) })
                .OrderByDescending(p => p.Value)
                .Take(5)
                .ToList();

            return new SpendingStatsDto
            {
                TotalOrders     = orders.Count,
                TotalSpent      = orders.Sum(o => o.FinalPayment),
                VatInvoiceCount = orders.Count(o => o.RedInvoiceStatus != RedInvoiceStatus.None),
                TopProductName  = topProducts.FirstOrDefault()?.Name ?? "Chưa có dữ liệu",
                SpendingByMonth = spendingByMonth,
                TopProducts     = topProducts,
            };
        }


        public async Task<OrderHistoryDetailDto> GetOrderDetailForCustomerAsync(Guid userId, Guid orderId)
        {
            
            var profile = await GetCustomerProfileAsync(userId);

            var order = await _unitOfWork.Orders.GetOrderDetailForCustomerAsync(orderId, profile.Id);
            if (order == null)
                throw new KeyNotFoundException("Không tìm thấy đơn hàng hoặc bạn không có quyền xem đơn này.");

            const int vatWindowHours = 168;
            var vatDeadline  = order.CreatedAt.AddHours(vatWindowHours);
            var canRequestVat = order.OrderStatus == OrderStatus.Completed
                                && DateTime.UtcNow < vatDeadline
                                && order.RedInvoiceStatus == RedInvoiceStatus.None;

           
            var itemDtos = order.OrderItems.Select(oi => new OrderItemDetailDto
            {
                ProductId    = oi.ProductId,
                ProductName  = oi.Product?.Name ?? "(Sản phẩm đã bị xóa)",
                ProductSku   = oi.Product?.Sku  ?? string.Empty,
                ProductImage = oi.Product?.ImageUrl,
                Quantity     = oi.Quantity,
                PriceSnapshot = oi.PriceSnapshot,
                LineTotal    = oi.PriceSnapshot * oi.Quantity,
            }).ToList();

            // Ưu tiên địa chỉ đã snapshot tại thời điểm đặt hàng; null (đơn cũ) mới fallback về địa chỉ mặc định hiện tại.
            string? addressString = order.ShippingAddress;
            if (string.IsNullOrEmpty(addressString))
            {
                var defaultAddress = order.CustomerProfile?.Addresses?.FirstOrDefault(a => a.IsDefault)
                                     ?? order.CustomerProfile?.Addresses?.FirstOrDefault();
                if (defaultAddress != null)
                {
                    addressString = $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}";
                }
                else if (!string.IsNullOrEmpty(order.CustomerProfile?.CompanyAddress))
                {
                    addressString = order.CustomerProfile.CompanyAddress;
                }
            }

            var customerName = order.CustomerProfile?.User?.FullName
                               ?? order.CustomerProfile?.CompanyName 
                               ?? "Khách hàng";
                               
            var customerPhone = order.CustomerProfile?.User?.PhoneNumber 
                                ?? order.CustomerProfile?.CompanyPhone 
                                ?? "---";

            return new OrderHistoryDetailDto
            {
                Id               = order.Id,
                OrderCode        = order.OrderCode,
                CreatedAt        = order.CreatedAt,
                TotalAmount      = order.TotalAmount,
                DiscountAmount   = order.DiscountAmount,
                VatAmount        = order.VatAmount,
                FinalPayment     = order.FinalPayment,
                PaymentMethod    = order.PaymentMethod.ToString(),
                PaymentStatus    = order.PaymentStatus.ToString(),
                OrderStatus      = order.OrderStatus.ToString(),
                RedInvoiceStatus = order.RedInvoiceStatus.ToString(),
                
                CustomerName     = customerName,
                CustomerPhone    = customerPhone,
                ShippingAddress  = addressString ?? "---",
                
                DeliveryShift    = order.DeliveryShift,
                DeliveryVehicleId = order.DeliveryVehicleId,
                DeliveryStatus   = order.DeliveryStatus.ToString(),
                ScheduledDeliveryDate = order.ScheduledDeliveryDate,
                DeliveredAt      = order.DeliveredAt,
                CustomerSignatureUrl = order.CustomerSignatureUrl,
                DeliveryPhotoUrl  = order.DeliveryPhotoUrl,
                FailedDeliveryCount = order.FailedDeliveryCount,
                AmountPaid       = order.AmountPaid,

                InvoicePdfUrl    = order.InvoicePdfUrl,
                CanRequestVat    = canRequestVat,
                VatDeadline      = canRequestVat ? vatDeadline : null,
                Items            = itemDtos,
                ReturnExchangeRequests = order.ReturnExchangeRequests.Select(req => new ReturnExchangeRequestSnapshotDto
                {
                    Id = req.Id,
                    Status = req.Status.ToString(),
                    Reason = req.Reason,
                    ManagerNote = req.ManagerNote,
                    CreatedAt = req.CreatedAt,
                    RequestedBy = req.RequestedBy?.FullName ?? "",
                    EvidenceUrls = req.EvidenceUrls,
                    ReplacementOrderId = req.ReplacementOrderId,
                    ReplacementOrderCode = req.ReplacementOrder?.OrderCode,
                    ReturnItems = req.ReturnItems.Select(ri => new SalesOrderItemDto
                    {
                        ProductId = ri.ProductId,
                        ProductName = ri.Product?.Name ?? "",
                        ProductSku = ri.Product?.Sku ?? "",
                        Quantity = ri.Quantity,
                        PriceSnapshot = ri.PriceSnapshot,
                        LineTotal = ri.PriceSnapshot * ri.Quantity
                    }).ToList(),
                    ExchangeItems = req.ExchangeItems.Select(ei => new SalesOrderItemDto
                    {
                        ProductId = ei.ProductId,
                        ProductName = ei.Product?.Name ?? "",
                        ProductSku = ei.Product?.Sku ?? "",
                        Quantity = ei.Quantity,
                        PriceSnapshot = ei.PriceSnapshot,
                        LineTotal = ei.PriceSnapshot * ei.Quantity
                    }).ToList()
                }).ToList()
            };
        }

        public async Task<OrderHistoryDetailDto> TrackOrderPublicAsync(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                throw new ArgumentException("Vui lòng nhập mã đơn hàng hoặc số điện thoại để tra cứu.");

            var term = search.Trim().ToLower();

            var order = await _context.Orders
                .AsNoTracking()
                .AsSplitQuery() // tránh nhân bản dòng (tích Descartes) khi JOIN nhiều collection 1-nhiều cùng lúc
                .Include(o => o.CustomerProfile).ThenInclude(cp => cp.User)
                .Include(o => o.CustomerProfile.Addresses)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
                .Include(o => o.ReturnExchangeRequests).ThenInclude(req => req.ReturnItems).ThenInclude(ri => ri.Product)
                .Include(o => o.ReturnExchangeRequests).ThenInclude(req => req.ExchangeItems).ThenInclude(ei => ei.Product)
                .Include(o => o.ReturnExchangeRequests).ThenInclude(req => req.ReplacementOrder)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync(o => o.OrderCode.ToLower() == term || 
                                          (o.CustomerProfile != null && o.CustomerProfile.User != null && o.CustomerProfile.User.PhoneNumber != null && o.CustomerProfile.User.PhoneNumber.ToLower() == term) ||
                                          (o.CustomerProfile != null && o.CustomerProfile.CompanyPhone != null && o.CustomerProfile.CompanyPhone.ToLower() == term));

            if (order == null)
                throw new KeyNotFoundException($"Không tìm thấy đơn hàng tương ứng với '{search}'. Vui lòng kiểm tra lại mã đơn hàng hoặc số điện thoại.");

            const int vatWindowHours = 168;
            var vatDeadline = order.CreatedAt.AddHours(vatWindowHours);
            var canRequestVat = order.OrderStatus == OrderStatus.Completed
                                && DateTime.UtcNow < vatDeadline
                                && order.RedInvoiceStatus == RedInvoiceStatus.None;

            var itemDtos = order.OrderItems.Select(oi => new OrderItemDetailDto
            {
                ProductId = oi.ProductId,
                ProductName = oi.Product?.Name ?? "(Sản phẩm đã bị xóa)",
                ProductSku = oi.Product?.Sku ?? string.Empty,
                ProductImage = oi.Product?.ImageUrl,
                Quantity = oi.Quantity,
                PriceSnapshot = oi.PriceSnapshot,
                LineTotal = oi.PriceSnapshot * oi.Quantity,
            }).ToList();

            // Ưu tiên địa chỉ đã snapshot tại thời điểm đặt hàng; null (đơn cũ) mới fallback về địa chỉ mặc định hiện tại.
            string addressString = order.ShippingAddress ?? "---";
            if (string.IsNullOrEmpty(order.ShippingAddress))
            {
                var defaultAddress = order.CustomerProfile?.Addresses?.FirstOrDefault(a => a.IsDefault)
                                     ?? order.CustomerProfile?.Addresses?.FirstOrDefault();
                if (defaultAddress != null)
                    addressString = $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}";
                else if (!string.IsNullOrEmpty(order.CustomerProfile?.CompanyAddress))
                    addressString = order.CustomerProfile.CompanyAddress;
            }

            var customerName = order.CustomerProfile?.User?.FullName ?? order.CustomerProfile?.CompanyName ?? "Khách hàng";

            return new OrderHistoryDetailDto
            {
                Id = order.Id,
                OrderCode = order.OrderCode,
                CreatedAt = order.CreatedAt,
                TotalAmount = order.TotalAmount,
                DiscountAmount = order.DiscountAmount,
                VatAmount = order.VatAmount,
                FinalPayment = order.FinalPayment,
                PaymentMethod = order.PaymentMethod.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderStatus = order.OrderStatus.ToString(),
                RedInvoiceStatus = order.RedInvoiceStatus.ToString(),
                CustomerName = customerName,
                // Tra cứu công khai (chỉ cần biết mã đơn) — không lộ SĐT/địa chỉ đầy đủ (NFR-SEC06).
                CustomerPhone = null,
                ShippingAddress = null,
                DeliveryShift = order.DeliveryShift,
                DeliveryVehicleId = order.DeliveryVehicleId,
                DeliveryStatus = order.DeliveryStatus.ToString(),
                ScheduledDeliveryDate = order.ScheduledDeliveryDate,
                DeliveredAt = order.DeliveredAt,
                CustomerSignatureUrl = order.CustomerSignatureUrl,
                DeliveryPhotoUrl = order.DeliveryPhotoUrl,
                FailedDeliveryCount = order.FailedDeliveryCount,
                AmountPaid = order.AmountPaid,
                InvoicePdfUrl = order.InvoicePdfUrl,
                CanRequestVat = canRequestVat,
                VatDeadline = canRequestVat ? vatDeadline : null,
                Items = itemDtos,
                ReturnExchangeRequests = order.ReturnExchangeRequests.Select(req => new ReturnExchangeRequestSnapshotDto
                {
                    Id = req.Id,
                    Status = req.Status.ToString(),
                    Reason = req.Reason,
                    ManagerNote = req.ManagerNote,
                    CreatedAt = req.CreatedAt,
                    RequestedBy = req.RequestedBy?.FullName ?? "",
                    EvidenceUrls = req.EvidenceUrls,
                    ReplacementOrderId = req.ReplacementOrderId,
                    ReplacementOrderCode = req.ReplacementOrder?.OrderCode,
                    ReturnItems = req.ReturnItems.Select(ri => new SalesOrderItemDto
                    {
                        ProductId = ri.ProductId,
                        ProductName = ri.Product?.Name ?? "",
                        ProductSku = ri.Product?.Sku ?? "",
                        Quantity = ri.Quantity,
                        PriceSnapshot = ri.PriceSnapshot,
                        LineTotal = ri.PriceSnapshot * ri.Quantity
                    }).ToList(),
                    ExchangeItems = req.ExchangeItems.Select(ei => new SalesOrderItemDto
                    {
                        ProductId = ei.ProductId,
                        ProductName = ei.Product?.Name ?? "",
                        ProductSku = ei.Product?.Sku ?? "",
                        Quantity = ei.Quantity,
                        PriceSnapshot = ei.PriceSnapshot,
                        LineTotal = ei.PriceSnapshot * ei.Quantity
                    }).ToList()
                }).ToList()
            };
        }

        public async Task RequestVatInvoiceAsync(Guid userId, Guid orderId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            // GetOrderByIdAsync (tracked) thay vì bản AsNoTracking+Include CustomerProfile để tránh
            // EF double-tracking với profile đã track ở trên (Defect VT-01).
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);

            if (order == null || order.CustomerProfileId != profile.Id)
                throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.Completed)
                throw new InvalidOperationException("Chỉ đơn hàng đã giao thành công mới được yêu cầu hóa đơn VAT.");

            const int vatWindowHours = 168;
            if (DateTime.UtcNow > order.CreatedAt.AddHours(vatWindowHours))
                throw new InvalidOperationException("Đã quá thời hạn 7 ngày để yêu cầu hóa đơn VAT.");

            if (order.RedInvoiceStatus != RedInvoiceStatus.None)
                throw new InvalidOperationException("Đơn hàng này đã có yêu cầu hóa đơn VAT trước đó.");

            order.RequiresRedInvoice = true;
            order.RedInvoiceStatus   = RedInvoiceStatus.Pending;

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<PagedResultDto<SalesOrderListDto>> GetSalesOrdersAsync(SalesOrderQueryDto query, Guid? salesStaffId = null)
        {
            query.Page = query.Page < 1 ? 1 : query.Page;
            query.PageSize = query.PageSize < 1 ? 10 : (query.PageSize > 100 ? 100 : query.PageSize);

            var queryable = _context.Orders
                .Include(o => o.CustomerProfile)
                .ThenInclude(cp => cp.User)
                .AsQueryable();

            if (salesStaffId.HasValue)
            {
                // Lọc theo snapshot Sale trên đơn (không theo chủ khách hiện tại):
                // đơn được "giữ lại" cho Sale cũ sau khi đổi Sale vẫn hiển thị đúng người xử lý
                queryable = queryable.Where(o => o.SalesStaffId == salesStaffId.Value);
            }

            if (!string.IsNullOrEmpty(query.SearchQuery))
            {
                var lowerSearch = query.SearchQuery.ToLower();
                queryable = queryable.Where(o => o.OrderCode.ToLower().Contains(lowerSearch) 
                                              || (o.CustomerProfile.Representative != null && o.CustomerProfile.Representative.ToLower().Contains(lowerSearch))
                                              || (o.CustomerProfile.CompanyName != null && o.CustomerProfile.CompanyName.ToLower().Contains(lowerSearch)));
            }

            if (!string.IsNullOrEmpty(query.Status) && query.Status != "all")
            {
                if (Enum.TryParse<OrderStatus>(query.Status, true, out var orderStatus))
                {
                    queryable = queryable.Where(o => o.OrderStatus == orderStatus);
                }
            }

            if (!string.IsNullOrEmpty(query.PaymentMethod) && query.PaymentMethod != "all")
            {
                if (Enum.TryParse<PaymentMethod>(query.PaymentMethod, true, out var paymentMethod))
                {
                    queryable = queryable.Where(o => o.PaymentMethod == paymentMethod);
                }
            }

            var totalCount = await queryable.CountAsync();
            var items = await queryable
                .OrderByDescending(o => o.CreatedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(o => new SalesOrderListDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách hàng",
                    CreatedAt = o.CreatedAt,
                    FinalPayment = o.FinalPayment,
                    PaymentMethod = o.PaymentMethod.ToString(),
                    PaymentStatus = o.PaymentStatus.ToString(),
                    OrderStatus = o.OrderStatus.ToString(),
                    FulfillmentStatus = o.FulfillmentStatus.ToString(),
                    // Ưu tiên địa chỉ đã snapshot tại thời điểm đặt hàng, fallback về mặc định hiện tại.
                    ShippingAddress = !string.IsNullOrEmpty(o.ShippingAddress)
                        ? o.ShippingAddress
                        : (o.CustomerProfile.Addresses.Where(a => a.IsDefault)
                            .Select(a => a.SpecificAddress + ", " + a.Ward + ", " + a.District + ", " + a.City)
                            .FirstOrDefault() ?? o.CustomerProfile.CompanyAddress ?? "---"),
                    TotalQuantity = o.OrderItems.Sum(i => i.Quantity),
                    PickingStartedAt = o.PickingStartedAt,
                    PickingCompletedAt = o.PickingCompletedAt,
                    InvoicePdfUrl = o.InvoicePdfUrl,
                    HasReturnRequest = o.ReturnExchangeRequests.Any(r => r.Status != ReturnExchangeStatus.Cancelled),
                    ReturnRequestStatus = o.ReturnExchangeRequests.Where(r => r.Status != ReturnExchangeStatus.Cancelled)
                        .OrderByDescending(r => r.CreatedAt)
                        .Select(r => r.Status.ToString())
                        .FirstOrDefault()
                })
                .ToListAsync();

            return new PagedResultDto<SalesOrderListDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
            };
        }

        public async Task<SalesOrderDetailDto> GetSalesOrderDetailAsync(Guid orderId, Guid? salesStaffId = null)
        {
            var order = await _context.Orders
                .Include(o => o.CustomerProfile)
                .ThenInclude(cp => cp.User)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
                .Include(o => o.CustomerProfile.Addresses)
                .Include(o => o.ReturnExchangeRequests)
                    .ThenInclude(req => req.ReturnItems)
                        .ThenInclude(ri => ri.Product)
                .Include(o => o.ReturnExchangeRequests)
                    .ThenInclude(req => req.ExchangeItems)
                        .ThenInclude(ei => ei.Product)
                .Include(o => o.ReturnExchangeRequests)
                    .ThenInclude(req => req.RequestedBy)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (salesStaffId.HasValue && order.SalesStaffId != salesStaffId.Value)
            {
                // Chặn IDOR: cùng cơ chế scope theo snapshot SalesStaffId đã dùng ở GetSalesOrdersAsync.
                throw new UnauthorizedAccessException("Bạn không có quyền truy cập đơn hàng này.");
            }

            // Ưu tiên địa chỉ đã snapshot tại thời điểm đặt hàng; null (đơn cũ) mới fallback về địa chỉ mặc định hiện tại.
            string addressString = order.ShippingAddress ?? "---";
            if (string.IsNullOrEmpty(order.ShippingAddress))
            {
                var defaultAddress = order.CustomerProfile.Addresses?.FirstOrDefault(a => a.IsDefault)
                                     ?? order.CustomerProfile.Addresses?.FirstOrDefault();
                if (defaultAddress != null)
                    addressString = $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}";
                else if (!string.IsNullOrEmpty(order.CustomerProfile.CompanyAddress))
                    addressString = order.CustomerProfile.CompanyAddress;
            }

            return new SalesOrderDetailDto
            {
                Id = order.Id,
                OrderCode = order.OrderCode,
                CustomerName = order.CustomerProfile.Representative ?? order.CustomerProfile.CompanyName ?? "Khách hàng",
                CustomerPhone = order.CustomerProfile.User?.PhoneNumber ?? order.CustomerProfile.CompanyPhone ?? "---",
                CustomerEmail = order.CustomerProfile.User?.Email,
                CompanyName = order.CustomerProfile.CompanyName,
                ShippingAddress = addressString,
                CreatedAt = order.CreatedAt,
                TotalAmount = order.TotalAmount,
                DiscountAmount = order.DiscountAmount,
                VatAmount = order.VatAmount,
                FinalPayment = order.FinalPayment,
                PaymentMethod = order.PaymentMethod.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderStatus = order.OrderStatus.ToString(),
                FulfillmentStatus = order.FulfillmentStatus.ToString(),
                DeliveryStatus = order.DeliveryStatus.ToString(),
                DeliveryVehicleId = order.DeliveryVehicleId,
                DeliveryShift = order.DeliveryShift,
                ScheduledDeliveryDate = order.ScheduledDeliveryDate,
                DeliveredAt = order.DeliveredAt,
                CustomerSignatureUrl = order.CustomerSignatureUrl,
                DeliveryPhotoUrl = order.DeliveryPhotoUrl,
                FailedDeliveryCount = order.FailedDeliveryCount,
                AmountPaid = order.AmountPaid,
                PickingStartedAt = order.PickingStartedAt,
                PickingCompletedAt = order.PickingCompletedAt,
                InvoicePdfUrl = order.InvoicePdfUrl,
                HasReturnRequest = order.ReturnExchangeRequests.Any(r => r.Status != ReturnExchangeStatus.Cancelled),
                ReturnRequestStatus = order.ReturnExchangeRequests.Where(r => r.Status != ReturnExchangeStatus.Cancelled)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.Status.ToString())
                    .FirstOrDefault(),
                ReturnExchangeRequests = order.ReturnExchangeRequests.Select(req => new ReturnExchangeRequestSnapshotDto
                {
                    Id = req.Id,
                    Status = req.Status.ToString(),
                    Reason = req.Reason,
                    ManagerNote = req.ManagerNote,
                    CreatedAt = req.CreatedAt,
                    RequestedBy = req.RequestedBy?.FullName ?? "",
                    EvidenceUrls = req.EvidenceUrls,
                    ReturnItems = req.ReturnItems.Select(ri => new SalesOrderItemDto
                    {
                        ProductId = ri.ProductId,
                        ProductName = ri.Product?.Name ?? "",
                        ProductSku = ri.Product?.Sku ?? "",
                        Quantity = ri.Quantity,
                        PriceSnapshot = ri.PriceSnapshot,
                        LineTotal = ri.PriceSnapshot * ri.Quantity
                    }).ToList(),
                    ExchangeItems = req.ExchangeItems.Select(ei => new SalesOrderItemDto
                    {
                        ProductId = ei.ProductId,
                        ProductName = ei.Product?.Name ?? "",
                        ProductSku = ei.Product?.Sku ?? "",
                        Quantity = ei.Quantity,
                        PriceSnapshot = ei.PriceSnapshot,
                        LineTotal = ei.PriceSnapshot * ei.Quantity
                    }).ToList()
                }).ToList(),
                Items = order.OrderItems.Select(oi => new SalesOrderItemDto
                {
                    ProductId = oi.ProductId,
                    ProductName = oi.Product?.Name ?? "(Sản phẩm đã bị xóa)",
                    ProductSku = oi.Product?.Sku ?? string.Empty,
                    ProductImageUrl = oi.Product?.ImageUrl,
                    Quantity = oi.Quantity,
                    PriceSnapshot = oi.PriceSnapshot,
                    LineTotal = oi.PriceSnapshot * oi.Quantity
                }).ToList()
            };
        }

        public async Task ConfirmOrderAsync(Guid orderId, Guid salesStaffId)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.Draft && order.OrderStatus != OrderStatus.PendingConfirmation)
                throw new InvalidOperationException("Chỉ đơn hàng ở trạng thái 'Mới' hoặc 'Chờ xác nhận' mới có thể được xác nhận.");

            if (order.PaymentMethod == PaymentMethod.SePay && order.PaymentStatus != PaymentStatus.Paid)
                throw new InvalidOperationException("Đơn hàng SePay phải được thanh toán trước khi xác nhận.");

            // Gộp release+allocate tồn kho + tạo pick task + cập nhật OrderStatus vào 1 transaction để
            // rollback được nếu AllocateAsync throw (vd hết hàng do giao dịch khác chen ngang).
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                order.OrderStatus = OrderStatus.Confirmed;
                order.ConfirmedAt = DateTime.UtcNow;
                if (order.FulfillmentStatus == FulfillmentStatus.Reserved || order.FulfillmentStatus == FulfillmentStatus.Unallocated)
                {
                    order.FulfillmentStatus = FulfillmentStatus.Allocated;

                    // Chuyển giữ mềm (Reserved, đặt lúc checkout) sang giữ chắc (Allocated) cho đơn này.
                    var orderItemQuantities = order.OrderItems.Select(oi => (oi.ProductId, oi.Quantity)).ToList();
                    await _inventoryReservationService.ReleaseReservedAsync(orderItemQuantities);
                    await _inventoryReservationService.AllocateAsync(orderItemQuantities);

                    var defaultWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Code == "WH-DEFAULT");
                    if (defaultWarehouse == null) throw new Exception("Không tìm thấy kho mặc định (WH-DEFAULT).");

                    var pickTasksToCreate = new List<PickTask>();
                    var warehouseTasks = new Dictionary<Guid, PickTask>();

                    PickTask GetOrCreateTask(Guid warehouseId)
                    {
                        if (!warehouseTasks.TryGetValue(warehouseId, out var task))
                        {
                            task = new PickTask
                            {
                                Id = Guid.NewGuid(),
                                OrderId = order.Id,
                                WarehouseId = warehouseId,
                                Status = PickTaskStatus.Pending,
                                CreatedAt = DateTime.UtcNow
                            };
                            warehouseTasks[warehouseId] = task;
                            pickTasksToCreate.Add(task);
                        }
                        return task;
                    }

                    foreach (var item in order.OrderItems)
                    {
                        var quantityRemaining = item.Quantity;

                        var defaultInv = await _context.Inventories
                            .Include(inv => inv.WarehouseLocation)
                            .ThenInclude(wl => wl.Warehouse)
                            .FirstOrDefaultAsync(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Id == defaultWarehouse.Id);

                        if (defaultInv != null && defaultInv.OnHandQuantity > 0)
                        {
                            var takeQty = Math.Min(quantityRemaining, defaultInv.OnHandQuantity);
                            var task = GetOrCreateTask(defaultWarehouse.Id);
                            task.Items.Add(new PickTaskItem
                            {
                                PickTaskId = task.Id,
                                ProductId = item.ProductId,
                                QuantityToPick = takeQty,
                                PickedQuantity = 0
                            });
                            quantityRemaining -= takeQty;
                        }

                        if (quantityRemaining > 0)
                        {
                            var otherInvs = await _context.Inventories
                                .Include(inv => inv.WarehouseLocation)
                                .ThenInclude(wl => wl.Warehouse)
                                .Where(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Id != defaultWarehouse.Id && inv.OnHandQuantity > 0)
                                .OrderByDescending(inv => inv.OnHandQuantity)
                                .ToListAsync();

                            foreach (var inv in otherInvs)
                            {
                                if (quantityRemaining <= 0) break;

                                var takeQty = Math.Min(quantityRemaining, inv.OnHandQuantity);
                                var task = GetOrCreateTask(inv.WarehouseLocation!.WarehouseId);
                                task.Items.Add(new PickTaskItem
                                {
                                    PickTaskId = task.Id,
                                    ProductId = item.ProductId,
                                    QuantityToPick = takeQty,
                                    PickedQuantity = 0
                                });
                                quantityRemaining -= takeQty;
                            }

                            if (quantityRemaining > 0)
                            {
                                // Không đủ tồn ở bất kỳ kho nào -> vẫn dồn phần thiếu vào WH-DEFAULT để cảnh báo thiếu hàng.
                                var task = GetOrCreateTask(defaultWarehouse.Id);
                                var existingItem = task.Items.FirstOrDefault(i => i.ProductId == item.ProductId);
                                if (existingItem != null) {
                                    existingItem.QuantityToPick += quantityRemaining;
                                } else {
                                    task.Items.Add(new PickTaskItem
                                    {
                                        PickTaskId = task.Id,
                                        ProductId = item.ProductId,
                                        QuantityToPick = quantityRemaining,
                                        PickedQuantity = 0
                                    });
                                }
                            }
                        }
                    }

                    if (pickTasksToCreate.Any())
                    {
                        _context.PickTasks.AddRange(pickTasksToCreate);
                    }
                }

                await _unitOfWork.Orders.UpdateOrderAsync(order);
                await _unitOfWork.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        public async Task RejectOrderAsync(Guid orderId, Guid salesStaffId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("Vui lòng nhập lý do từ chối đơn hàng.");

            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.PendingConfirmation)
                throw new InvalidOperationException("Chỉ đơn hàng đang 'Chờ xác nhận' mới có thể bị từ chối.");

            // Gộp release tồn + cập nhật OrderStatus vào 1 transaction để rollback nguyên tử nếu SaveChanges thất bại.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _inventoryReservationService.ReleaseReservedAsync(
                    order.OrderItems.Select(oi => (oi.ProductId, oi.Quantity)));

                order.OrderStatus = OrderStatus.Cancelled;
                order.FulfillmentStatus = FulfillmentStatus.Unallocated;
                order.CancelReason = reason;

                await _unitOfWork.Orders.UpdateOrderAsync(order);
                await _unitOfWork.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task RequestCancelOrderAsync(Guid orderId, Guid customerId, RequestCancelOrderDto request)
        {
            var customerProfile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(customerId);
            if (customerProfile == null) throw new UnauthorizedAccessException("Không tìm thấy hồ sơ khách hàng.");

            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null || order.CustomerProfileId != customerProfile.Id)
                throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            var validStatuses = new[] { OrderStatus.PendingPayment, OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderStatus.Processing };
            if (!validStatuses.Contains(order.OrderStatus))
                throw new InvalidOperationException($"Không thể yêu cầu hủy đơn hàng ở trạng thái {order.OrderStatus}.");

            if (order.DeliveryStatus == DeliveryStatus.InDelivery)
                throw new InvalidOperationException("Không thể yêu cầu hủy đơn hàng đang trên đường giao.");

            order.OrderStatus = OrderStatus.CancelRequested;
            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();

            // Đã commit CancelRequested ở trên -> lỗi gửi thông báo không được báo lỗi cho khách, chỉ log.
            if (customerProfile.AssignedSalesStaffId.HasValue)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_24_CustomerRequestedCancel,
                        customerProfile.AssignedSalesStaffId.Value,
                        "Yêu cầu hủy đơn",
                        $"Khách hàng yêu cầu hủy đơn {order.OrderCode}. Lý do: {request.Reason}",
                        order.Id,
                        "Order"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OrderService] Error sending cancel-request notification: {ex.Message}");
                }
            }
        }

        public async Task ProcessCancelRequestAsync(Guid orderId, Guid salesStaffId, ProcessCancelRequestDto request)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.CancelRequested)
                throw new InvalidOperationException(
                    request.IsApproved
                        ? "Không thể duyệt hủy khi đơn hàng không ở trạng thái yêu cầu hủy."
                        : "Không thể từ chối hủy khi đơn hàng không ở trạng thái yêu cầu hủy.");

            var profile = await _context.CustomerProfiles.FindAsync(order.CustomerProfileId);
            var customerUserId = profile?.UserId ?? Guid.Empty;

            // Thông báo cho khách chỉ gửi SAU khi transaction dưới đây commit thành công — biến local
            // này chỉ chuẩn bị nội dung, gửi thật ở cuối method.
            string notificationTitle;
            string notificationMessage;

            // Gộp release tồn (nếu duyệt hủy) + hoàn Credit + cập nhật OrderStatus vào 1 transaction để
            // rollback nguyên tử nếu SaveChanges thất bại.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (!request.IsApproved)
                {
                    // Khôi phục trạng thái
                    if (order.PaymentMethod == PaymentMethod.SePay && order.PaymentStatus != PaymentStatus.Paid)
                        order.OrderStatus = OrderStatus.PendingPayment;
                    else if (order.PaymentMethod == PaymentMethod.COD && order.FulfillmentStatus == FulfillmentStatus.Unallocated)
                        order.OrderStatus = OrderStatus.PendingConfirmation;
                    else if (order.FulfillmentStatus != FulfillmentStatus.Unallocated)
                        order.OrderStatus = OrderStatus.Processing;
                    else
                        order.OrderStatus = OrderStatus.Confirmed;

                    notificationTitle = "Yêu cầu hủy đơn bị từ chối";
                    notificationMessage = $"Yêu cầu hủy đơn {order.OrderCode} không được chấp thuận. Lý do: {request.Reason}";
                }
                else
                {
                    // Trả lại phần tồn kho đang giữ trước khi đổi trạng thái — Allocated (đã Confirmed)
                    // trả AllocatedQuantity, còn lại (chỉ mới giữ mềm lúc checkout) trả ReservedQuantity.
                    var orderItemQuantities = order.OrderItems.Select(oi => (oi.ProductId, oi.Quantity)).ToList();
                    if (order.FulfillmentStatus == FulfillmentStatus.Allocated)
                        await _inventoryReservationService.ReleaseAllocatedAsync(orderItemQuantities);
                    else
                        await _inventoryReservationService.ReleaseReservedAsync(orderItemQuantities);

                    order.OrderStatus = OrderStatus.Cancelled;
                    order.FulfillmentStatus = FulfillmentStatus.Unallocated;

                    // Hoàn lại tiền (nếu đã thanh toán) và Credit đã dùng
                    decimal amountToRefund = 0m;
                    if (order.PaymentStatus == PaymentStatus.Paid || order.PaymentStatus == PaymentStatus.PartiallyPaid)
                    {
                        amountToRefund += order.FinalPayment; // TODO: PartiallyPaid nên hoàn theo AmountPaid thực tế, tạm dùng FinalPayment
                    }
                    amountToRefund += order.CreditApplied;

                    if (amountToRefund > 0)
                    {
                        if (profile != null)
                        {
                            profile.AvailableCredit += amountToRefund;
                            // profile đã được track bởi EF Core, không cần gọi Update() thủ công

                            _context.CreditTransactions.Add(new CreditTransaction
                            {
                                CustomerProfileId = profile.Id,
                                Amount = amountToRefund,
                                Description = $"Hoàn tiền từ đơn hàng hủy {order.OrderCode}",
                                OrderId = order.Id,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }

                    notificationTitle = "Yêu cầu hủy đơn được chấp thuận";
                    notificationMessage = $"Yêu cầu hủy đơn {order.OrderCode} đã được xử lý thành công.";
                }

                await _unitOfWork.Orders.UpdateOrderAsync(order);
                await _unitOfWork.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            if (customerUserId != Guid.Empty)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_25_CancelRequestResult,
                        customerUserId,
                        notificationTitle,
                        notificationMessage,
                        order.Id,
                        "Order"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OrderService] Error sending cancel-request-result notification: {ex.Message}");
                }
            }
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 1: LẬP LỊCH XE & PHÂN CÔNG GIAO HÀNG
        // =====================================================================

        public async Task<ScheduleDeliveryResponseDto> ScheduleDeliveryAsync(Guid scheduledByUserId, ScheduleDeliveryRequestDto dto)
        {
            if (dto.OrderIds == null || !dto.OrderIds.Any())
                throw new Exception("Danh sách đơn hàng không được rỗng.");

            var vehicleActive = await _context.Vehicles.AnyAsync(v => v.VehicleNumber == dto.VehicleId && v.IsActive);
            if (!vehicleActive)
                throw new Exception("Mã xe không hợp lệ hoặc xe đã ngừng hoạt động.");

            var validShifts = new[] { "Sáng", "Trưa", "Chiều" };
            if (!validShifts.Contains(dto.Shift))
                throw new Exception("Ca giao hàng không hợp lệ. Chọn: Sáng / Trưa / Chiều.");

            // Kiểm tra ngày và ca giao hàng hết hạn (MGR-06 / BR-DL-02)
            var targetDate = dto.DeliveryDate ?? DateTime.UtcNow.Date;
            var localNow = DateTime.UtcNow.AddHours(7); // Giả định múi giờ GMT+7 Việt Nam
            var localToday = localNow.Date;

            if (targetDate.Date < localToday)
            {
                throw new InvalidOperationException("Không thể lên lịch giao hàng cho ngày trong quá khứ.");
            }

            if (targetDate.Date == localToday)
            {
                var currentHour = localNow.Hour;
                if (dto.Shift == "Sáng" && currentHour >= 10)
                {
                    throw new InvalidOperationException("Đã quá 10:00 AM, không thể thêm/sửa đơn hàng cho Ca sáng ngày hôm nay.");
                }
                if (dto.Shift == "Trưa" && currentHour >= 14)
                {
                    throw new InvalidOperationException("Đã quá 14:00 (2:00 PM), không thể thêm/sửa đơn hàng cho Ca trưa ngày hôm nay.");
                }
                if (dto.Shift == "Chiều" && currentHour >= 22)
                {
                    throw new InvalidOperationException("Đã quá 22:00 (10:00 PM), không thể thêm/sửa đơn hàng cho Ca chiều ngày hôm nay.");
                }
            }

            // UC-34: kiểm tra xung đột xe + ca + ngày (tính cả chuyến Scheduled, không chỉ InDelivery).
            // Khi trùng, không chặn cứng mà tạo hàng đợi cho Sales Manager chủ động xử lý.
            var conflictingOrderIds = await _context.Orders
                .Where(o => o.DeliveryVehicleId == dto.VehicleId
                         && o.DeliveryShift == dto.Shift
                         && o.ScheduledDeliveryDate.HasValue && o.ScheduledDeliveryDate.Value.Date == targetDate.Date
                         && (o.DeliveryStatus == DeliveryStatus.InDelivery || o.DeliveryStatus == DeliveryStatus.Scheduled)
                         && !dto.OrderIds.Contains(o.Id))
                .Select(o => o.Id)
                .ToListAsync();

            // Trùng lịch xe/ca/ngày phải so cả với StockTransfer (điều chuyển nội bộ) đã xếp xe, không
            // chỉ Orders — nếu không sẽ bỏ sót xung đột khi payload chỉ gồm StockTransfer.
            var conflictingTransferIds = await _context.StockTransfers
                .Where(st => st.DeliveryVehicleId == dto.VehicleId
                          && st.DeliveryShift == dto.Shift
                          && st.ScheduledDeliveryDate.HasValue && st.ScheduledDeliveryDate.Value.Date == targetDate.Date
                          && st.Status == StockTransferStatus.TransportArranged
                          && !dto.OrderIds.Contains(st.Id))
                .Select(st => st.Id)
                .ToListAsync();

            if (conflictingOrderIds.Any() || conflictingTransferIds.Any())
            {
                var conflict = new DeliveryScheduleConflict
                {
                    VehicleId = dto.VehicleId,
                    Shift = dto.Shift,
                    RequestedDate = targetDate.Date,
                    OrderIds = string.Join(",", dto.OrderIds),
                    RaisedByUserId = scheduledByUserId,
                    Status = DeliveryConflictStatus.Pending
                };
                _context.DeliveryScheduleConflicts.Add(conflict);
                await _context.SaveChangesAsync();

                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_11_DeliveryScheduleConflict,
                        SystemRole.SalesManager,
                        "Xung đột lịch xe/ca",
                        $"Xe {dto.VehicleId} ca {dto.Shift} ngày {targetDate:dd/MM/yyyy} đã có lịch trùng. Cần Sales Manager xử lý.",
                        conflict.Id,
                        "DeliveryScheduleConflict");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OrderService] Error sending delivery conflict notification: {ex.Message}");
                }

                throw new ScheduleConflictException(
                    $"Xe {dto.VehicleId} đang có lịch trùng ca {dto.Shift} ngày {targetDate:dd/MM/yyyy}. Đã gửi yêu cầu xử lý tới Sales Manager.",
                    conflict.Id);
            }

            var orders = await _context.Orders
                .Where(o => dto.OrderIds.Contains(o.Id))
                .ToListAsync();

            int scheduled = 0;
            foreach (var order in orders)
            {
                // Chỉ lập lịch cho đơn đã HandedOver hoặc Ready
                if (order.DeliveryStatus != DeliveryStatus.NotScheduled && order.DeliveryStatus != DeliveryStatus.Rescheduled)
                    continue;

                order.DeliveryVehicleId = dto.VehicleId;
                order.DeliveryShift = dto.Shift;
                order.ScheduledDeliveryDate = targetDate;
                order.DeliveryStatus = DeliveryStatus.Scheduled;
                scheduled++;
            }

            // ─── Xử lý StockTransfer (điều chuyển nội bộ) trong cùng payload ──
            var matchedOrderIds = orders.Select(o => o.Id).ToHashSet();
            var remainingIds = dto.OrderIds.Where(id => !matchedOrderIds.Contains(id)).ToList();

            if (remainingIds.Any())
            {
                var stockTransfers = await _context.StockTransfers
                    .Where(st => remainingIds.Contains(st.Id)
                              && st.Status == StockTransferStatus.TransportRequested)
                    .ToListAsync();

                foreach (var st in stockTransfers)
                {
                    st.DeliveryVehicleId = dto.VehicleId;
                    st.DeliveryShift = dto.Shift;
                    st.ScheduledDeliveryDate = targetDate;
                    st.Status = StockTransferStatus.TransportArranged;
                    scheduled++;
                }
            }

            await _context.SaveChangesAsync();

            return new ScheduleDeliveryResponseDto
            {
                VehicleId = dto.VehicleId,
                Shift = dto.Shift,
                DeliveryDate = targetDate,
                OrdersScheduled = scheduled,
                Message = $"Đã lập lịch {scheduled} đơn hàng cho xe {dto.VehicleId} ca {dto.Shift} ngày {targetDate:dd/MM/yyyy}."
            };
        }

        // =====================================================================
        // UC-34: SALES MANAGER XỬ LÝ XUNG ĐỘT LỊCH XE/CA
        // =====================================================================

        public async Task<List<DeliveryScheduleConflictDto>> GetPendingDeliveryConflictsAsync()
        {
            var conflicts = await _context.DeliveryScheduleConflicts
                .AsNoTracking()
                .Where(c => c.Status == DeliveryConflictStatus.Pending)
                .OrderBy(c => c.RaisedAt)
                .ToListAsync();

            var result = new List<DeliveryScheduleConflictDto>();
            foreach (var c in conflicts)
                result.Add(await BuildConflictDtoAsync(c));
            return result;
        }

        public async Task<DeliveryScheduleConflictDto> ResolveDeliveryConflictAsync(Guid conflictId, Guid managerId, ResolveDeliveryConflictRequestDto dto)
        {
            var conflict = await _context.DeliveryScheduleConflicts.FirstOrDefaultAsync(c => c.Id == conflictId);
            if (conflict == null) throw new KeyNotFoundException("Không tìm thấy xung đột lịch giao hàng.");
            if (conflict.Status != DeliveryConflictStatus.Pending)
                throw new InvalidOperationException("Xung đột này đã được xử lý trước đó.");

            var orderIds = conflict.OrderIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();

            if (dto.Action == "Reassign" || dto.Action == "Override")
            {
                int vehicleId;
                string shift;
                DateTime date;

                if (dto.Action == "Reassign")
                {
                    if (!dto.NewVehicleId.HasValue || string.IsNullOrWhiteSpace(dto.NewShift) || !dto.NewDate.HasValue)
                        throw new Exception("Vui lòng chọn xe/ca/ngày mới khi chuyển lịch (Reassign).");

                    vehicleId = dto.NewVehicleId.Value;
                    shift = dto.NewShift;
                    date = dto.NewDate.Value.Date;

                    var stillConflicting = await _context.Orders.AnyAsync(o =>
                        o.DeliveryVehicleId == vehicleId && o.DeliveryShift == shift &&
                        o.ScheduledDeliveryDate.HasValue && o.ScheduledDeliveryDate.Value.Date == date &&
                        (o.DeliveryStatus == DeliveryStatus.InDelivery || o.DeliveryStatus == DeliveryStatus.Scheduled) &&
                        !orderIds.Contains(o.Id));

                    // Cùng lý do với ScheduleDeliveryAsync: phải kiểm tra trùng cả với StockTransfer đã xếp xe.
                    if (!stillConflicting)
                    {
                        stillConflicting = await _context.StockTransfers.AnyAsync(st =>
                            st.DeliveryVehicleId == vehicleId && st.DeliveryShift == shift &&
                            st.ScheduledDeliveryDate.HasValue && st.ScheduledDeliveryDate.Value.Date == date &&
                            st.Status == StockTransferStatus.TransportArranged &&
                            !orderIds.Contains(st.Id));
                    }

                    if (stillConflicting)
                        throw new InvalidOperationException("Lịch mới được chọn cũng đang trùng. Vui lòng chọn xe/ca/ngày khác.");
                }
                else // Override: Manager chấp nhận trùng lịch, áp lại đúng lịch gốc đã yêu cầu.
                {
                    vehicleId = conflict.VehicleId;
                    shift = conflict.Shift;
                    date = conflict.RequestedDate;
                }

                var orders = await _context.Orders.Where(o => orderIds.Contains(o.Id)).ToListAsync();
                foreach (var order in orders)
                {
                    if (order.DeliveryStatus != DeliveryStatus.NotScheduled && order.DeliveryStatus != DeliveryStatus.Rescheduled)
                        continue;

                    order.DeliveryVehicleId = vehicleId;
                    order.DeliveryShift = shift;
                    order.ScheduledDeliveryDate = date;
                    order.DeliveryStatus = DeliveryStatus.Scheduled;
                }

                // Áp lại lịch cho cả StockTransfer trong lô xung đột (không chỉ Order), tránh bị kẹt
                // vĩnh viễn ở TransportRequested vì RequestTransportAsync chỉ nhận trạng thái Draft.
                var stockTransfersInConflict = await _context.StockTransfers
                    .Where(st => orderIds.Contains(st.Id) && st.Status == StockTransferStatus.TransportRequested)
                    .ToListAsync();
                foreach (var st in stockTransfersInConflict)
                {
                    st.DeliveryVehicleId = vehicleId;
                    st.DeliveryShift = shift;
                    st.ScheduledDeliveryDate = date;
                    st.Status = StockTransferStatus.TransportArranged;
                }
            }
            // Reject: không đổi gì trên Order/StockTransfer — Sales/Kho phải tự lập lịch lại từ đầu qua endpoint schedule.

            conflict.Status = dto.Action == "Reject" ? DeliveryConflictStatus.Rejected : DeliveryConflictStatus.Resolved;
            conflict.ResolvedByUserId = managerId;
            conflict.ResolvedAt = DateTime.UtcNow;
            conflict.ResolutionAction = dto.Action;
            conflict.ResolutionNote = dto.Note;

            await _context.SaveChangesAsync();

            return await BuildConflictDtoAsync(conflict);
        }

        private async Task<DeliveryScheduleConflictDto> BuildConflictDtoAsync(DeliveryScheduleConflict c)
        {
            var orderIds = c.OrderIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
            var orderCodes = await _context.Orders.Where(o => orderIds.Contains(o.Id)).Select(o => o.OrderCode).ToListAsync();

            var userIds = new List<Guid> { c.RaisedByUserId };
            if (c.ResolvedByUserId.HasValue) userIds.Add(c.ResolvedByUserId.Value);
            var users = await _context.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

            return new DeliveryScheduleConflictDto
            {
                Id = c.Id,
                VehicleId = c.VehicleId,
                Shift = c.Shift,
                RequestedDate = c.RequestedDate,
                OrderCodes = orderCodes,
                Status = c.Status.ToString(),
                RaisedByUserId = c.RaisedByUserId,
                RaisedByUserName = users.GetValueOrDefault(c.RaisedByUserId),
                RaisedAt = c.RaisedAt,
                ResolvedByUserId = c.ResolvedByUserId,
                ResolvedByUserName = c.ResolvedByUserId.HasValue ? users.GetValueOrDefault(c.ResolvedByUserId.Value) : null,
                ResolvedAt = c.ResolvedAt,
                ResolutionAction = c.ResolutionAction,
                ResolutionNote = c.ResolutionNote
            };
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 1: LẤY DANH SÁCH ĐƠN CẦN/ĐANG GIAO
        // =====================================================================

        public async Task<List<DeliveryOrderListDto>> GetDeliveryOrdersAsync(Guid salesStaffId)
        {
            var orders = await _context.Orders
                .AsNoTracking()
                .AsSplitQuery() // tránh nhân bản dòng khi JOIN nhiều collection 1-nhiều cùng lúc
                .Include(o => o.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(o => o.CustomerProfile.Addresses)
                .Include(o => o.OrderItems)
                .Where(o => o.CustomerProfile.AssignedSalesStaffId == salesStaffId
                         && (
                             (o.DeliveryStatus == DeliveryStatus.NotScheduled &&
                              (o.FulfillmentStatus == FulfillmentStatus.Ready
                            || o.FulfillmentStatus == FulfillmentStatus.Consolidating
                            || o.FulfillmentStatus == FulfillmentStatus.Consolidated
                            || o.FulfillmentStatus == FulfillmentStatus.HandedOver
                            || o.FulfillmentStatus == FulfillmentStatus.Fulfilled))
                          || o.DeliveryStatus == DeliveryStatus.Scheduled
                          || o.DeliveryStatus == DeliveryStatus.InDelivery
                          || o.DeliveryStatus == DeliveryStatus.Failed
                          || o.DeliveryStatus == DeliveryStatus.Rescheduled
                         ))
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var result = orders.Select(o =>
            {
                // Ưu tiên địa chỉ đã snapshot tại thời điểm đặt hàng, fallback về mặc định hiện tại.
                string address;
                if (!string.IsNullOrEmpty(o.ShippingAddress))
                {
                    address = o.ShippingAddress;
                }
                else
                {
                    var defaultAddress = o.CustomerProfile?.Addresses?.FirstOrDefault(a => a.IsDefault)
                                      ?? o.CustomerProfile?.Addresses?.FirstOrDefault();
                    address = defaultAddress != null
                        ? $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}"
                        : o.CustomerProfile?.CompanyAddress ?? "---";
                }

                return new DeliveryOrderListDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile?.Representative ?? o.CustomerProfile?.CompanyName ?? "Khách hàng",
                    CustomerPhone = o.CustomerProfile?.User?.PhoneNumber ?? o.CustomerProfile?.CompanyPhone ?? "---",
                    ShippingAddress = address,
                    FinalPayment = o.FinalPayment,
                    AmountPaid = o.AmountPaid,
                    PaymentMethod = o.PaymentMethod.ToString(),
                    OrderStatus = o.OrderStatus.ToString(),
                    DeliveryStatus = o.DeliveryStatus.ToString(),
                    VehicleId = o.DeliveryVehicleId,
                    Shift = o.DeliveryShift,
                    ScheduledDeliveryDate = o.ScheduledDeliveryDate,
                    FailedDeliveryCount = o.FailedDeliveryCount,
                    IsBlocked = o.IsBlockedForDelivery,
                    ItemCount = o.OrderItems?.Sum(oi => oi.Quantity) ?? 0
                };
            }).ToList();

            // ─── Gộp thêm StockTransfer đang chờ/đã xếp xe vào danh sách điều phối ──
            var stockTransfers = await _context.StockTransfers
                .AsNoTracking()
                .Include(st => st.SourceWarehouse)
                .Include(st => st.DestinationWarehouse)
                .Include(st => st.Items)
                .Where(st => st.Status == StockTransferStatus.TransportRequested
                          || st.Status == StockTransferStatus.TransportArranged)
                .OrderByDescending(st => st.CreatedAt)
                .ToListAsync();

            foreach (var st in stockTransfers)
            {
                var deliveryStatus = st.Status == StockTransferStatus.TransportArranged
                    ? "Scheduled" : "NotScheduled";

                result.Add(new DeliveryOrderListDto
                {
                    Id = st.Id,
                    OrderCode = st.Code,
                    CustomerName = $"Nội bộ: {st.SourceWarehouse?.Name} → {st.DestinationWarehouse?.Name}",
                    CustomerPhone = "---",
                    ShippingAddress = st.DestinationWarehouse?.Name ?? "---",
                    FinalPayment = 0,
                    AmountPaid = 0,
                    PaymentMethod = "Transfer",
                    OrderStatus = "Transfer",
                    DeliveryStatus = deliveryStatus,
                    VehicleId = st.DeliveryVehicleId,
                    Shift = st.DeliveryShift,
                    ScheduledDeliveryDate = st.ScheduledDeliveryDate,
                    FailedDeliveryCount = 0,
                    IsBlocked = false,
                    ItemCount = st.Items?.Sum(i => i.Quantity) ?? 0
                });
            }

            return result;
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 2: GHI NHẬN KẾT QUẢ GIAO HÀNG (POD + COD)
        // =====================================================================

        public async Task<DeliveryResultResponseDto> RecordDeliveryResultAsync(Guid orderId, Guid salesStaffId, RecordDeliveryResultDto dto)
        {
            var order = await _context.Orders
                .Include(o => o.CustomerProfile)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
            if (order.IsBlockedForDelivery)
                throw new InvalidOperationException("Đơn hàng đã bị khóa do thất bại quá 3 lần. Vui lòng liên hệ Sales Manager.");

            // Đơn đã thanh toán qua SePay -> không được thu tiền mặt (COD) lần nữa (tránh thu trùng).
            if (order.PaymentStatus == PaymentStatus.Paid && dto.AmountCollected > 0)
                throw new InvalidOperationException("Đơn hàng đã được thanh toán qua SePay, không được thu thêm tiền mặt (COD).");

            // Khách từ chối nhận hàng -> bắt buộc chọn lý do (không được để trống).
            if (dto.CustomerRejected && string.IsNullOrWhiteSpace(dto.RejectionReasonCode))
                throw new InvalidOperationException("Vui lòng chọn lý do khi khách từ chối nhận hàng.");

            // Ràng buộc: cho phép giao hàng SỚM hơn ngày hẹn (khách nhận trước vẫn hợp lệ), chỉ chặn
            // xác nhận SAU ngày hẹn — đổi ngược lại so với trước đây (trước đây chặn giao sớm, cho
            // qua giao trễ).
            var localNow = DateTime.UtcNow.AddHours(7);
            var localToday = localNow.Date;
            if (order.ScheduledDeliveryDate.HasValue && order.ScheduledDeliveryDate.Value.Date < localToday)
            {
                throw new InvalidOperationException($"Đơn hàng này có lịch hẹn giao vào ngày {order.ScheduledDeliveryDate.Value.ToString("dd/MM/yyyy")}. Không thể xác nhận giao hàng sau ngày hẹn.");
            }

            var outcome = dto.DeliveryOutcome?.ToLower() ?? "delivered";
            bool debtCreated = false;
            decimal? remainingDebt = null;
            bool signatureUploadFailed = false;
            bool photoUploadFailed = false;

            // ─ Lưu ảnh chữ ký lên Cloudinary
            if (!string.IsNullOrEmpty(dto.CustomerSignatureBase64))
            {
                try
                {
                    var sigUrl = await _cloudinaryService.UploadBase64ImageAsync(
                        dto.CustomerSignatureBase64,
                        "signatures",
                        $"{order.OrderCode}-sig"
                    );
                    order.CustomerSignatureUrl = sigUrl;
                }
                catch (Exception ex)
                {
                    signatureUploadFailed = true;
                    Console.WriteLine($"[Delivery] Cloudinary Sig upload error: {ex.Message}");
                }
            }

            // ─ Lưu ảnh hiện trường lên Cloudinary
            if (!string.IsNullOrEmpty(dto.DeliveryPhotoBase64))
            {
                try
                {
                    var photoUrl = await _cloudinaryService.UploadBase64ImageAsync(
                        dto.DeliveryPhotoBase64,
                        "delivery-photos",
                        $"{order.OrderCode}-photo"
                    );
                    order.DeliveryPhotoUrl = photoUrl;
                }
                catch (Exception ex)
                {
                    photoUploadFailed = true;
                    Console.WriteLine($"[Delivery] Cloudinary Photo upload error: {ex.Message}");
                }
            }

            if (outcome == "delivered" || outcome == "partially_delivered")
            {
                // CỘNG DỒN vào AmountPaid đã có (vd. trả trước qua SePay), không ghi đè — ghi đè sẽ
                // tính nhầm thành nợ toàn bộ FinalPayment dù đơn đã thanh toán đủ từ trước.
                order.AmountPaid += dto.AmountCollected;
                order.DeliveredAt = DateTime.UtcNow;
                
                if (outcome == "delivered")
                {
                    order.DeliveryStatus = DeliveryStatus.Delivered;
                }
                else
                {
                    order.DeliveryStatus = DeliveryStatus.PartiallyDelivered;
                }

                var amountDue = order.FinalPayment - order.AmountPaid;

                if (amountDue <= 0)
                {
                    // Khách trả đủ hoặc thừa → COMPLETED
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.OrderStatus = OrderStatus.Completed;
                }
                else
                {
                    // Khách trả thiếu → PARTIALLY_PAID + tạo DebtRecord, trạng thái đơn vẫn chuyển sang Completed
                    order.PaymentStatus = PaymentStatus.PartiallyPaid;
                    order.OrderStatus = OrderStatus.Completed;

                    var debtRecord = new CustomerDebt
                    {
                        CustomerProfileId = order.CustomerProfileId,
                        OrderId = order.Id,
                        DebtAmount = amountDue,
                        Status = DebtStatus.InDebt,
                        OverdueDays = 0
                    };
                    await _context.CustomerDebts.AddAsync(debtRecord);
                    debtCreated = true;
                    remainingDebt = amountDue;
                }
            }
            else // failed
            {
                if (dto.CustomerRejected)
                    order.DeliveryRejectionReasonCode = dto.RejectionReasonCode;

                order.FailedDeliveryCount++;
                order.DeliveryStatus = DeliveryStatus.Failed;

                var deliveryFailureThresholdRaw = await _systemConfigService.GetEffectiveValueAsync("DELIVERY_FAILURE_MANAGER_THRESHOLD");
                var deliveryFailureThreshold = int.TryParse(deliveryFailureThresholdRaw, out var parsedThreshold) ? parsedThreshold : 3;

                if (order.FailedDeliveryCount >= deliveryFailureThreshold)
                {
                    order.IsBlockedForDelivery = true;
                    order.DeliveryStatus = DeliveryStatus.Failed;
                }
                else
                {
                    order.DeliveryStatus = DeliveryStatus.Rescheduled;
                }
            }

            await _context.SaveChangesAsync();

            // Đơn đã cập nhật trạng thái và commit thành công ở trên -> lỗi gửi notification không
            // được làm fail request ghi nhận kết quả giao hàng, chỉ log để theo dõi.
            if (order.IsBlockedForDelivery)
            {
                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_12_DeliveryFailedThirdTime,
                        SystemRole.SalesManager,
                        "Đơn hàng bị khóa do giao thất bại nhiều lần",
                        $"Đơn hàng {order.OrderCode} đã giao thất bại {order.FailedDeliveryCount} lần và đã bị khóa, cần xử lý.",
                        order.Id,
                        "Order"
                    );
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"[OrderService] Error sending delivery failed notification: {notifyEx.Message}");
                }
            }
            else if (debtCreated)
            {
                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_13_CodUnderpaid,
                        SystemRole.SalesManager,
                        "Thu COD thiếu tiền",
                        $"Đơn hàng {order.OrderCode} thu thiếu {remainingDebt:N0}đ, đã tạo sổ công nợ.",
                        order.Id,
                        "Order"
                    );

                    if (order.CustomerProfile?.AssignedSalesStaffId != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_13_CodUnderpaid,
                            order.CustomerProfile.AssignedSalesStaffId.Value,
                            "Thu COD thiếu tiền",
                            $"Đơn hàng {order.OrderCode} thu thiếu {remainingDebt:N0}đ, đã tạo sổ công nợ.",
                            order.Id,
                            "Order"
                        );
                    }
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"[OrderService] Error sending COD underpaid notification: {notifyEx.Message}");
                }
            }

            var message = order.IsBlockedForDelivery
                ? "Đơn hàng đã bị khóa do thất bại giao hàng vượt ngưỡng cho phép. Hệ thống đã chuyển hồ sơ lên Sales Manager."
                : debtCreated ? $"Giao hàng thành công. Khách còn nợ {remainingDebt:N0}đ. Đã tạo sổ công nợ."
                : "Giao hàng và thu tiền thành công.";

            if (signatureUploadFailed || photoUploadFailed)
            {
                message += " Lưu ý: lỗi tải lên " +
                    (signatureUploadFailed && photoUploadFailed ? "chữ ký và ảnh hiện trường" : signatureUploadFailed ? "chữ ký" : "ảnh hiện trường") +
                    " — vui lòng chụp/upload lại bằng chứng giao hàng.";
            }

            return new DeliveryResultResponseDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                NewDeliveryStatus = order.DeliveryStatus.ToString(),
                NewOrderStatus = order.OrderStatus.ToString(),
                DebtRecordCreated = debtCreated,
                RemainingDebt = remainingDebt,
                IsBlockedByFailures = order.IsBlockedForDelivery,
                Message = message,
                SignatureUploadFailed = signatureUploadFailed,
                PhotoUploadFailed = photoUploadFailed
            };
        }

        // ─── P2-6: Sales Manager xử lý đơn bị khóa & công nợ COD (UC-35) ──────

        public async Task<List<BlockedOrderDto>> GetBlockedOrdersAsync()
        {
            var orders = await _context.Orders
                .AsNoTracking()
                .Include(o => o.CustomerProfile).ThenInclude(cp => cp.User)
                .Include(o => o.CustomerProfile).ThenInclude(cp => cp.AssignedSalesStaff)
                .Where(o => o.IsBlockedForDelivery)
                .OrderByDescending(o => o.FailedDeliveryCount)
                .ToListAsync();

            return orders.Select(o => new BlockedOrderDto
            {
                OrderId = o.Id,
                OrderCode = o.OrderCode,
                CustomerName = o.CustomerProfile?.Representative ?? o.CustomerProfile?.CompanyName ?? "Khách hàng",
                CustomerPhone = o.CustomerProfile?.User?.PhoneNumber ?? o.CustomerProfile?.CompanyPhone ?? string.Empty,
                FailedDeliveryCount = o.FailedDeliveryCount,
                DeliveryRejectionReasonCode = o.DeliveryRejectionReasonCode,
                AssignedSalesStaffName = o.CustomerProfile?.AssignedSalesStaff?.FullName,
                FinalPayment = o.FinalPayment
            }).ToList();
        }

        public async Task UnblockOrderForRedeliveryAsync(Guid orderId, Guid managerId, string reason)
        {
            var order = await _context.Orders
                .Include(o => o.CustomerProfile)
                .FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (!order.IsBlockedForDelivery)
                throw new InvalidOperationException("Đơn hàng hiện không bị khóa.");

            order.IsBlockedForDelivery = false;
            order.FailedDeliveryCount = 0;
            order.DeliveryStatus = DeliveryStatus.Rescheduled;
            order.UnblockedAt = DateTime.UtcNow;
            order.UnblockedByUserId = managerId;
            order.UnblockReason = reason.Trim();

            await _context.SaveChangesAsync();

            if (order.CustomerProfile?.AssignedSalesStaffId != null)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_36_OrderUnblockedForRedelivery,
                        order.CustomerProfile.AssignedSalesStaffId.Value,
                        "Đơn hàng đã được mở khóa",
                        $"Đơn hàng {order.OrderCode} đã được Sales Manager mở khóa, có thể lên lịch giao lại.",
                        order.Id,
                        "Order"
                    );
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"[OrderService] Error sending order unblocked notification: {notifyEx.Message}");
                }
            }
        }

        public async Task<List<CustomerDebtManagementDto>> GetDebtsAsync(string? status = null)
        {
            var query = _context.CustomerDebts
                .AsNoTracking()
                .Include(d => d.CustomerProfile)
                .Include(d => d.Order)
                .Include(d => d.SettledByUser)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DebtStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(d => d.Status == parsedStatus);
            }

            var debts = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

            return debts.Select(d => new CustomerDebtManagementDto
            {
                Id = d.Id,
                CustomerProfileId = d.CustomerProfileId,
                CustomerName = d.CustomerProfile?.Representative ?? d.CustomerProfile?.CompanyName ?? "Khách hàng",
                OrderId = d.OrderId,
                OrderCode = d.Order?.OrderCode ?? string.Empty,
                DebtAmount = d.DebtAmount,
                Status = d.Status.ToString(),
                OverdueDays = d.Status == DebtStatus.InDebt ? Math.Max(0, (int)(DateTime.UtcNow - d.CreatedAt).TotalDays) : d.OverdueDays,
                CreatedAt = d.CreatedAt,
                SettledAt = d.SettledAt,
                SettledByName = d.SettledByUser?.FullName,
                SettlementNote = d.SettlementNote
            }).ToList();
        }

        public async Task SettleDebtAsync(Guid debtId, Guid managerId, string? note)
        {
            var debt = await _context.CustomerDebts
                .FirstOrDefaultAsync(d => d.Id == debtId)
                ?? throw new KeyNotFoundException("Không tìm thấy công nợ.");

            if (debt.Status != DebtStatus.InDebt)
                throw new InvalidOperationException("Công nợ này đã được xử lý.");

            debt.Status = DebtStatus.Settled;
            debt.DebtAmount = 0;
            debt.SettledAt = DateTime.UtcNow;
            debt.SettledByUserId = managerId;
            debt.SettlementNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            await _context.SaveChangesAsync();

            try
            {
                var order = await _context.Orders
                    .Include(o => o.CustomerProfile)
                    .FirstOrDefaultAsync(o => o.Id == debt.OrderId);

                if (order?.CustomerProfile?.AssignedSalesStaffId != null)
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_37_DebtSettled,
                        order.CustomerProfile.AssignedSalesStaffId.Value,
                        "Công nợ đã được tất toán",
                        $"Công nợ đơn hàng {order.OrderCode} đã được Sales Manager đánh dấu tất toán.",
                        debt.OrderId,
                        "Order"
                    );
                }
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[OrderService] Error sending debt settled notification: {notifyEx.Message}");
            }
        }

        public async Task CreateReturnExchangeRequestAsync(Guid orderId, Guid requestedByUserId, CreateReturnExchangeRequestDto dto)
        {
            var order = await _context.Orders
                .Include(o => o.CustomerProfile)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            var isOwner = order.CustomerProfile != null && order.CustomerProfile.UserId == requestedByUserId;
            if (!isOwner)
            {
                var requester = await _context.Users.FindAsync(requestedByUserId);
                var isStaff = requester != null &&
                              (requester.Role == SystemRole.SalesStaff || requester.Role == SystemRole.SalesManager ||
                               requester.Role == SystemRole.Admin);
                if (!isStaff)
                    throw new UnauthorizedAccessException("Bạn không có quyền tạo yêu cầu đổi/trả trên đơn hàng này.");
            }

            if (order.OrderStatus != OrderStatus.Completed ||
                (order.DeliveryStatus != DeliveryStatus.Delivered && order.DeliveryStatus != DeliveryStatus.PartiallyDelivered))
            {
                throw new InvalidOperationException("Chỉ được yêu cầu đổi/trả đối với các đơn đã giao thành công.");
            }

            var request = new ReturnExchangeRequest
            {
                OrderId = orderId,
                CustomerProfileId = order.CustomerProfileId,
                RequestedByUserId = requestedByUserId,
                Reason = dto.Reason,
                EvidenceUrls = dto.EvidenceUrls,
                Status = ReturnExchangeStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                PickupAddress = order.ShippingAddress // Lấy hàng đúng nơi đơn gốc đã giao, không đổi theo địa chỉ mặc định hiện tại của khách
            };

            foreach (var item in dto.ReturnItems)
            {
                var orderItem = await _context.OrderItems.FirstOrDefaultAsync(oi => oi.OrderId == orderId && oi.ProductId == item.ProductId)
                                ?? throw new InvalidOperationException($"Sản phẩm {item.ProductId} không có trong đơn gốc.");
                if (item.Quantity > orderItem.Quantity) throw new InvalidOperationException($"Số lượng trả vượt quá số lượng mua đối với sản phẩm {item.ProductId}.");

                request.ReturnItems.Add(new ReturnExchangeRequestItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceSnapshot = orderItem.PriceSnapshot
                });
            }

            foreach (var item in dto.ExchangeItems)
            {
                var product = await _context.Products.FindAsync(item.ProductId)
                              ?? throw new InvalidOperationException($"Không tìm thấy sản phẩm mới {item.ProductId}.");

                request.ExchangeItems.Add(new ReturnExchangeRequestNewItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceSnapshot = product.StandardListedPrice
                });
            }

            _context.ReturnExchangeRequests.Add(request);
            await _context.SaveChangesAsync();

            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_21_QualityReturnRequested,
                    SystemRole.SalesManager,
                    "Yêu cầu đổi/trả hàng mới",
                    $"Đơn hàng {order.OrderCode} có yêu cầu đổi/trả hàng mới, lý do: {dto.Reason}",
                    request.Id,
                    "ReturnExchangeRequest"
                );
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[OrderService] Error sending return/exchange request notification: {notifyEx.Message}");
            }
        }

        public async Task ProcessReturnExchangeRequestAsync(Guid requestId, Guid managerId, ProcessReturnExchangeRequestDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var request = await _context.ReturnExchangeRequests
                    .Include(r => r.ReturnItems)
                    .Include(r => r.ExchangeItems)
                    .Include(r => r.CustomerProfile)
                    .Include(r => r.Order).ThenInclude(o => o.Debts)
                    .FirstOrDefaultAsync(r => r.Id == requestId);

                if (request == null) throw new KeyNotFoundException("Không tìm thấy yêu cầu.");
                if (request.Status != ReturnExchangeStatus.Pending) throw new InvalidOperationException("Yêu cầu này đã được xử lý trước đó.");

                request.ProcessedByUserId = managerId;
                request.ProcessedAt = DateTime.UtcNow;
                request.ManagerNote = dto.ManagerNote;

                if (!dto.IsApproved)
                {
                    request.Status = ReturnExchangeStatus.Rejected;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return;
                }

                request.Status = ReturnExchangeStatus.Approved;
                request.Order.OrderStatus = OrderStatus.Returned;

                decimal returnTotalValue = request.ReturnItems.Sum(ri => ri.PriceSnapshot * ri.Quantity);
                decimal exchangeTotalValue = request.ExchangeItems.Sum(ei => ei.PriceSnapshot * ei.Quantity);
                
                decimal offsetAmount = returnTotalValue;

                var activeDebts = request.Order.Debts.Where(d => d.Status == DebtStatus.InDebt).ToList();
                foreach (var debt in activeDebts)
                {
                    if (offsetAmount <= 0) break;
                    
                    if (offsetAmount >= debt.DebtAmount)
                    {
                        offsetAmount -= debt.DebtAmount;
                        debt.DebtAmount = 0;
                        debt.Status = DebtStatus.Settled;
                    }
                    else
                    {
                        debt.DebtAmount -= offsetAmount;
                        offsetAmount = 0;
                    }
                }

                if (offsetAmount > 0)
                {
                    request.CustomerProfile.AvailableCredit += offsetAmount;
                    _context.CreditTransactions.Add(new CreditTransaction
                    {
                        CustomerProfileId = request.CustomerProfileId,
                        Amount = offsetAmount,
                        Description = $"Hoàn tiền từ trả hàng đơn {request.Order.OrderCode}",
                        OrderId = request.OrderId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                if (request.ExchangeItems.Any())
                {
                    var replacementCode = $"VT-EX-{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
                    var newOrder = new Order
                    {
                        CustomerProfileId = request.CustomerProfileId,
                        OrderCode = replacementCode,
                        TotalAmount = exchangeTotalValue,
                        FinalPayment = exchangeTotalValue,
                        PaymentMethod = PaymentMethod.COD,
                        PaymentStatus = PaymentStatus.Pending,
                        OrderStatus = OrderStatus.Confirmed,
                        ConfirmedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        ReplacementOrderId = request.OrderId,
                        ShippingAddress = request.Order.ShippingAddress // Đơn đổi hàng giao lại đúng nơi đơn gốc đã giao
                    };

                    if (request.CustomerProfile.AvailableCredit > 0)
                    {
                        decimal creditToApply = Math.Min(exchangeTotalValue, request.CustomerProfile.AvailableCredit);
                        newOrder.CreditApplied = creditToApply;
                        newOrder.FinalPayment -= creditToApply;
                        request.CustomerProfile.AvailableCredit -= creditToApply;

                        _context.CreditTransactions.Add(new CreditTransaction
                        {
                            CustomerProfileId = request.CustomerProfileId,
                            Amount = -creditToApply,
                            Description = $"Thanh toán cho đơn đổi hàng {replacementCode}",
                            // OrderId sẽ để null tạm vì newOrder chưa sinh Id nếu chưa save
                            CreatedAt = DateTime.UtcNow
                        });

                        if (newOrder.FinalPayment <= 0)
                        {
                            newOrder.PaymentStatus = PaymentStatus.Paid;
                            newOrder.OrderStatus = OrderStatus.Confirmed;
                            newOrder.ConfirmedAt = DateTime.UtcNow;
                        }
                    }

                    var newOrderItems = request.ExchangeItems.Select(ei => new OrderItem
                    {
                        ProductId = ei.ProductId,
                        Quantity = ei.Quantity,
                        PriceSnapshot = ei.PriceSnapshot,
                        Order = newOrder
                    }).ToList();
                    
                    newOrder.OrderItems = newOrderItems;
                    await _context.Orders.AddAsync(newOrder);
                }

                foreach (var ri in request.ReturnItems)
                {
                    _context.ReturnedGoodsLogs.Add(new ReturnedGoodsLog
                    {
                        OrderId = request.OrderId,
                        WarehouseStaffId = managerId,
                        Condition = "Chờ kiểm tra",
                        QuantityReturned = ri.Quantity,
                        InspectionDate = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // =====================================================================
        // BƯỚC 5: THU HỒI HÀNG LỖI TỪ KHÁCH HÀNG (PICKUP LOGISTICS)
        // =====================================================================

        public async Task<List<PendingPickupDto>> GetPendingPickupsAsync(Guid userId)
        {
            // Trả về danh sách ReturnExchangeRequest đã duyệt nhưng chưa lấy xong
            var requests = await _context.ReturnExchangeRequests
                .AsNoTracking()
                .AsSplitQuery() // tránh nhân bản dòng khi JOIN nhiều collection 1-nhiều cùng lúc
                .Include(r => r.Order)
                .Include(r => r.CustomerProfile)
                    .ThenInclude(c => c.User)
                .Include(r => r.CustomerProfile)
                    .ThenInclude(c => c.Addresses)
                .Include(r => r.ReturnItems)
                    .ThenInclude(ri => ri.Product)
                .Where(r => r.Status == ReturnExchangeStatus.Approved && r.PickupStatus != PickupStatus.PickedUp)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return requests.Select(r =>
            {
                // Ưu tiên: địa chỉ lấy hàng đã chốt trên yêu cầu -> địa chỉ đã chốt của đơn gốc ->
                // (yêu cầu/đơn tạo trước khi có snapshot) fallback địa chỉ mặc định hiện tại của khách.
                string address;
                if (!string.IsNullOrEmpty(r.PickupAddress))
                {
                    address = r.PickupAddress;
                }
                else if (!string.IsNullOrEmpty(r.Order.ShippingAddress))
                {
                    address = r.Order.ShippingAddress;
                }
                else
                {
                    var defaultAddress = r.CustomerProfile?.Addresses?.FirstOrDefault(a => a.IsDefault)
                                      ?? r.CustomerProfile?.Addresses?.FirstOrDefault();
                    address = defaultAddress != null
                        ? $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}"
                        : r.CustomerProfile?.CompanyAddress ?? "---";
                }

                return new PendingPickupDto
                {
                    RequestId = r.Id,
                    RequestCode = r.Id.ToString().Substring(0, 8).ToUpper(),
                    OrderId = r.OrderId,
                    OrderCode = r.Order.OrderCode,
                    CustomerName = r.CustomerProfile?.Representative ?? r.CustomerProfile?.CompanyName ?? "Khách hàng",
                    CustomerPhone = r.CustomerProfile?.User?.PhoneNumber ?? r.CustomerProfile?.CompanyPhone ?? "---",
                    ShippingAddress = address,
                    PickupStatus = r.PickupStatus.ToString(),
                    PickupVehicleId = r.PickupVehicleId,
                    PickupShift = r.PickupShift,
                    ScheduledPickupDate = r.ScheduledPickupDate,
                    ReturnProductNames = r.ReturnItems.Select(ri => ri.Product.Name).ToList(),
                    Items = r.ReturnItems.Select(ri => new PendingPickupItemDto
                    {
                        ProductId = ri.ProductId,
                        ProductName = ri.Product.Name,
                        Quantity = ri.Quantity,
                        Reason = r.Reason ?? "Lỗi chất lượng"
                    }).ToList()
                };
            }).ToList();
        }

        public async Task SchedulePickupAsync(Guid requestId, Guid userId, SchedulePickupRequestDto dto)
        {
            var req = await _context.ReturnExchangeRequests.FindAsync(requestId);
            if (req == null) throw new KeyNotFoundException("Không tìm thấy yêu cầu đổi/trả.");

            if (req.Status != ReturnExchangeStatus.Approved)
                throw new InvalidOperationException("Chỉ có thể điều xe cho yêu cầu đã được duyệt.");

            req.PickupVehicleId = dto.VehicleId;
            req.PickupShift = dto.Shift;
            req.ScheduledPickupDate = dto.PickupDate;
            req.PickupStatus = PickupStatus.Scheduled;

            await _context.SaveChangesAsync();
        }

        public async Task ConfirmPickupAsync(Guid requestId, Guid userId)
        {
            var req = await _context.ReturnExchangeRequests
                .Include(r => r.ReturnItems)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (req == null) throw new KeyNotFoundException("Không tìm thấy yêu cầu đổi/trả.");

            if (req.PickupStatus != PickupStatus.Scheduled)
                throw new InvalidOperationException("Yêu cầu chưa được lên lịch điều xe hoặc đã lấy rồi.");

            req.PickupStatus = PickupStatus.PickedUp;

            // BR-019: QuarantineQuantity đã được cộng bởi ReceiveToQuarantine (bước "Tiếp nhận xe hoàn"
            // ở kho, chạy trước Confirm này) — không cộng lại ở đây, chỉ cộng OnHand bên dưới.
            var defaultLocationId = await _context.Warehouses
                .Where(w => w.Code == "WH-DEFAULT")
                .SelectMany(w => w.Locations)
                .Select(l => l.Id)
                .FirstOrDefaultAsync();

            foreach (var item in req.ReturnItems)
            {
                var inventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                    i.WarehouseLocationId == defaultLocationId && i.ProductId == item.ProductId);
                if (inventory == null)
                {
                    inventory = new Inventory { ProductId = item.ProductId, WarehouseLocationId = defaultLocationId };
                    _context.Inventories.Add(inventory);
                }
                inventory.OnHandQuantity += item.Quantity;

                _context.StockTransactions.Add(new StockTransaction
                {
                    InventoryId = inventory.Id,
                    ProductId = item.ProductId,
                    WarehouseLocationId = defaultLocationId,
                    QuantityChange = item.Quantity,
                    TransactionType = TransactionType.StockAdjustment,
                    ReferenceId = req.Id,
                    Note = $"Thu hồi hàng đổi/trả {req.Id} vào khu cách ly",
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }


        // =====================================================================
        // LUỒNG 5 – BƯỚC 3: YÊU CẦU HỦY ĐƠN PAID (CR-06)
        // =====================================================================

        public async Task RequestCancelPaidOrderAsync(Guid orderId, Guid requestedByUserId, string reason)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.PaymentStatus != PaymentStatus.Paid)
                throw new InvalidOperationException("Chỉ có thể yêu cầu hủy đơn hàng đã thanh toán (PAID).");

            if (order.OrderStatus == OrderStatus.CancelRequested || order.OrderStatus == OrderStatus.CancelledReallocated)
                throw new InvalidOperationException("Đơn hàng này đã có yêu cầu hủy trước đó.");

            // GH-14 / SRS 4.4.1: đơn đang trên đường giao không được nhận yêu cầu huỷ.
            if (order.DeliveryStatus == DeliveryStatus.InDelivery)
                throw new InvalidOperationException("Không thể yêu cầu hủy đơn hàng đang trên đường giao.");

            order.OrderStatus = OrderStatus.CancelRequested;
            order.CancelReason = reason;
            order.CancelRequestedAt = DateTime.UtcNow;

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();

            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_23_PaidOrderCancelledUnresolved,
                    SystemRole.SalesManager,
                    "Yêu cầu hủy đơn đã thanh toán",
                    $"Đơn hàng {order.OrderCode} (đã thanh toán {order.FinalPayment:N0}đ) có yêu cầu hủy, lý do: {reason}. Cần duyệt và xử lý hoàn tiền.",
                    order.Id,
                    "Order"
                );
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[OrderService] Error sending paid order cancel request notification: {notifyEx.Message}");
            }
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 4: DUYỆT HỦY + TẠO ĐƠN THAY THẾ + CREDIT
        // =====================================================================

        public async Task<ReplacementOrderResponseDto> ApproveCancelAndCreateReplacementAsync(
            Guid originalOrderId, Guid managerId, CreateReplacementOrderDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Lấy đơn gốc
                var originalOrder = await _context.Orders
                    .Include(o => o.CustomerProfile)
                    .Include(o => o.OrderItems)
                    .FirstOrDefaultAsync(o => o.Id == originalOrderId)
                    ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng gốc.");

                if (originalOrder.OrderStatus != OrderStatus.CancelRequested)
                    throw new InvalidOperationException("Đơn hàng không ở trạng thái chờ duyệt hủy.");

                var originalPaidAmount = originalOrder.FinalPayment;

                // 2. Tính giá trị đơn thay thế
                decimal newOrderTotal = 0;
                var newOrderItems = new List<OrderItem>();

                foreach (var item in dto.Items)
                {
                    var product = await _context.Products.FindAsync(item.ProductId)
                        ?? throw new Exception($"Không tìm thấy sản phẩm ID: {item.ProductId}");
                    newOrderTotal += item.Price * item.Quantity;
                    newOrderItems.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        PriceSnapshot = item.Price,
                        CostSnapshot = 0
                    });
                }

                // 3. Tính phân bổ
                decimal reallocatedToNewOrder = Math.Min(newOrderTotal, originalPaidAmount);
                decimal creditToWallet = Math.Max(0, originalPaidAmount - newOrderTotal);

                // 4. Tạo đơn thay thế
                var replacementCode = $"VT-RP-{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
                var replacementOrder = new Order
                {
                    CustomerProfileId = originalOrder.CustomerProfileId,
                    OrderCode = replacementCode,
                    TotalAmount = newOrderTotal,
                    DiscountAmount = 0,
                    VatAmount = 0,
                    FinalPayment = newOrderTotal,
                    PaymentMethod = originalOrder.PaymentMethod,
                    PaymentStatus = reallocatedToNewOrder >= newOrderTotal ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid,
                    OrderStatus = OrderStatus.Confirmed,
                    ConfirmedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    ShippingAddress = originalOrder.ShippingAddress // Đơn thay thế giao lại đúng nơi đơn gốc đã giao
                };
                foreach (var item in newOrderItems) { item.Order = replacementOrder; }
                replacementOrder.OrderItems = newOrderItems;
                await _context.Orders.AddAsync(replacementOrder);

                // 5. Tạo PaymentReallocation
                var reallocation = new PaymentReallocation
                {
                    OriginalOrderId = originalOrderId,
                    ReplacementOrderId = replacementOrder.Id,
                    Amount = reallocatedToNewOrder,
                    Status = "ReallocatedToOrder",
                    Timestamp = DateTime.UtcNow
                };
                await _context.PaymentReallocations.AddAsync(reallocation);

                // 6. Cập nhật đơn gốc → CancelledReallocated
                originalOrder.OrderStatus = OrderStatus.CancelledReallocated;
                originalOrder.ReplacementOrderId = replacementOrder.Id;

                // 7. Nạp Credit vào ví (nếu còn thừa)
                if (creditToWallet > 0)
                {
                    // Optimistic concurrency: reload profile with current state
                    var profile = await _context.CustomerProfiles.FindAsync(originalOrder.CustomerProfileId)
                        ?? throw new Exception("Không tìm thấy hồ sơ khách hàng.");
                    profile.AvailableCredit += creditToWallet;

                    var creditReallocation = new PaymentReallocation
                    {
                        OriginalOrderId = originalOrderId,
                        ReplacementOrderId = null,
                        Amount = creditToWallet,
                        Status = "RefundedToCredit",
                        Timestamp = DateTime.UtcNow
                    };
                    await _context.PaymentReallocations.AddAsync(creditReallocation);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 8. Lấy số dư Credit hiện tại
                var updatedProfile = await _context.CustomerProfiles.FindAsync(originalOrder.CustomerProfileId);

                return new ReplacementOrderResponseDto
                {
                    ReplacementOrderId = replacementOrder.Id,
                    ReplacementOrderCode = replacementOrder.OrderCode,
                    NewOrderValue = newOrderTotal,
                    OriginalPaidAmount = originalPaidAmount,
                    CreditAllocated = creditToWallet,
                    ReallocatedAmount = reallocatedToNewOrder,
                    CustomerCreditBalance = updatedProfile?.AvailableCredit ?? 0,
                    Message = creditToWallet > 0
                        ? $"Đã tạo đơn thay thế {replacementCode}. {creditToWallet:N0}đ đã được chuyển vào ví Credit của khách hàng."
                        : $"Đã tạo đơn thay thế {replacementCode}. Toàn bộ giá trị đơn gốc đã được chuyển sang đơn mới."
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        // L3-AS-02/AS-07: entry point độc lập cho payment reallocation, khác với
        // ApproveCancelAndCreateReplacementAsync — caller chỉ định thẳng SỐ TIỀN muốn phân bổ sang 1
        // đơn khác của cùng khách (hoặc để trống TargetOrderId để chuyển vào ví Credit). "remaining"
        // tính lại từ tổng đã phân bổ trước đó nên tự chặn được double-allocation.
        public async Task<PaymentReallocationResponseDto> CreatePaymentReallocationAsync(
            Guid callerUserId, CreatePaymentReallocationRequestDto request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var originalOrder = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == request.OriginalOrderId)
                    ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng gốc.");

                if (originalOrder.OrderStatus != OrderStatus.CancelRequested)
                    throw new InvalidOperationException(
                        "Đơn hàng không ở trạng thái chờ duyệt hủy, không thể phân bổ lại thanh toán.");

                var alreadyAllocated = await _context.PaymentReallocations
                    .Where(r => r.OriginalOrderId == request.OriginalOrderId)
                    .SumAsync(r => (decimal?)r.Amount) ?? 0m;
                var remaining = originalOrder.FinalPayment - alreadyAllocated;

                if (request.Amount > remaining)
                    throw new ReallocationValueConflictException(
                        $"Số tiền phân bổ ({request.Amount:N0}đ) vượt quá giá trị còn lại của khoản thanh toán gốc ({remaining:N0}đ).");

                if (request.TargetOrderId.HasValue)
                {
                    var targetOrder = await _context.Orders.FirstOrDefaultAsync(o => o.Id == request.TargetOrderId.Value)
                        ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng đích.");

                    if (targetOrder.CustomerProfileId != originalOrder.CustomerProfileId)
                        throw new ReallocationValueConflictException(
                            "Không thể phân bổ thanh toán sang đơn hàng của khách hàng khác.");
                }

                var reallocation = new PaymentReallocation
                {
                    OriginalOrderId = request.OriginalOrderId,
                    ReplacementOrderId = request.TargetOrderId,
                    Amount = request.Amount,
                    Status = request.TargetOrderId.HasValue ? "ReallocatedToOrder" : "RefundedToCredit",
                    Timestamp = DateTime.UtcNow
                };
                await _context.PaymentReallocations.AddAsync(reallocation);

                if (!request.TargetOrderId.HasValue)
                {
                    var profile = await _context.CustomerProfiles.FindAsync(originalOrder.CustomerProfileId)
                        ?? throw new Exception("Không tìm thấy hồ sơ khách hàng.");
                    profile.AvailableCredit += request.Amount;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new PaymentReallocationResponseDto
                {
                    Id = reallocation.Id,
                    OriginalOrderId = request.OriginalOrderId,
                    TargetOrderId = request.TargetOrderId,
                    Amount = request.Amount,
                    RemainingAfter = remaining - request.Amount,
                    Status = reallocation.Status
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<ReplacementOrderResponseDto> CreateExchangeReplacementOrderAsync(Guid requestId, Guid userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Get the ExchangeRequest
                var request = await _context.ReturnExchangeRequests
                    .Include(r => r.Order)
                    .Include(r => r.CustomerProfile)
                    .Include(r => r.ReturnItems)
                    .Include(r => r.ExchangeItems)
                    .FirstOrDefaultAsync(r => r.Id == requestId)
                    ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu đổi trả.");

                if (request.Status != ReturnExchangeStatus.Approved) 
                {
                    throw new InvalidOperationException("Yêu cầu chưa được duyệt.");
                }
                
                if (request.ReplacementOrderId != null)
                    throw new InvalidOperationException("Yêu cầu này đã được tạo đơn thay thế.");

                // 2. Calculate values
                decimal totalReturnedValue = request.ReturnItems.Sum(ri => ri.Quantity * ri.PriceSnapshot);
                decimal newOrderTotal = request.ExchangeItems.Sum(ei => ei.Quantity * ei.PriceSnapshot);

                // 3. Financial logic
                decimal finalPayment = 0;
                decimal creditApplied = 0;
                decimal creditToWallet = 0;

                if (newOrderTotal > totalReturnedValue)
                {
                    creditApplied = totalReturnedValue;
                    finalPayment = newOrderTotal - totalReturnedValue;
                }
                else if (newOrderTotal == totalReturnedValue)
                {
                    creditApplied = newOrderTotal;
                    finalPayment = 0;
                }
                else // newOrderTotal < totalReturnedValue
                {
                    creditApplied = newOrderTotal;
                    finalPayment = 0;
                    creditToWallet = totalReturnedValue - newOrderTotal;
                }

                // 4. Create Replacement Order
                var replacementCode = $"VT-EX-{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
                var replacementOrder = new Order
                {
                    CustomerProfileId = request.CustomerProfileId,
                    OrderCode = replacementCode,
                    TotalAmount = newOrderTotal,
                    DiscountAmount = 0,
                    VatAmount = 0,
                    CreditApplied = creditApplied,
                    FinalPayment = finalPayment,
                    PaymentMethod = PaymentMethod.COD,
                    PaymentStatus = finalPayment > 0 ? PaymentStatus.Pending : PaymentStatus.Paid,
                    OrderStatus = OrderStatus.Confirmed,
                    ConfirmedAt = DateTime.UtcNow,
                    SalesStaffId = request.Order.SalesStaffId,
                    CreatedAt = DateTime.UtcNow,
                    ShippingAddress = request.Order.ShippingAddress // Đơn đổi hàng giao lại đúng nơi đơn gốc đã giao
                };

                var newOrderItems = new List<OrderItem>();
                foreach (var ei in request.ExchangeItems)
                {
                    newOrderItems.Add(new OrderItem
                    {
                        Order = replacementOrder,
                        ProductId = ei.ProductId,
                        Quantity = ei.Quantity,
                        PriceSnapshot = ei.PriceSnapshot,
                        CostSnapshot = 0
                    });
                }
                replacementOrder.OrderItems = newOrderItems;
                await _context.Orders.AddAsync(replacementOrder);
                await _context.SaveChangesAsync(); // to get Id

                request.ReplacementOrderId = replacementOrder.Id;

                // 5. Customer Profile Wallet
                if (creditToWallet > 0)
                {
                    var profile = await _context.CustomerProfiles.FindAsync(request.CustomerProfileId);
                    if (profile != null)
                    {
                        profile.AvailableCredit += creditToWallet;
                        
                        var creditReallocation = new PaymentReallocation
                        {
                            OriginalOrderId = request.OrderId,
                            ReplacementOrderId = replacementOrder.Id,
                            Amount = creditToWallet,
                            Status = "RefundedToCredit_Exchange",
                            Timestamp = DateTime.UtcNow
                        };
                        await _context.PaymentReallocations.AddAsync(creditReallocation);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var updatedProfile = await _context.CustomerProfiles.FindAsync(request.CustomerProfileId);

                return new ReplacementOrderResponseDto
                {
                    ReplacementOrderId = replacementOrder.Id,
                    ReplacementOrderCode = replacementOrder.OrderCode,
                    NewOrderValue = newOrderTotal,
                    OriginalPaidAmount = totalReturnedValue,
                    CreditAllocated = creditToWallet,
                    ReallocatedAmount = creditApplied,
                    CustomerCreditBalance = updatedProfile?.AvailableCredit ?? 0,
                    Message = creditToWallet > 0
                        ? $"Đã tạo đơn thay thế {replacementCode}. {creditToWallet:N0}đ đã được hoàn vào ví Credit."
                        : (finalPayment > 0 ? $"Đã tạo đơn thay thế {replacementCode}. Khách cần thanh toán thêm {finalPayment:N0}đ." : $"Đã tạo đơn thay thế {replacementCode} (Đổi ngang).")
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
