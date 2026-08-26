using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Exceptions;
using VietTien.API.Models;
using VietTien.API.Services.Helpers;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    // Nhóm C (DEL-01..07): chuyến giao hàng theo xe/ca/ngày (DeliveryTrip), song song với luồng lập
    // lịch theo từng Order hiện có trong OrderService (ScheduleDeliveryAsync/RecordDeliveryResultAsync).
    // Không sửa luồng cũ — chỉ tái sử dụng cùng các field trên Order (DeliveryStatus, AmountPaid,
    // CustomerSignatureUrl, DeliveryPhotoUrl...) và model CustomerDebt.
    public class DeliveryTripService : IDeliveryTripService
    {
        // Ngưỡng riêng cho luồng Trip-based, KHÁC với ngưỡng block@3 (DELIVERY_FAILURE_MANAGER_THRESHOLD)
        // đang dùng trong OrderService.RecordDeliveryResultAsync — cố ý không đụng vào ngưỡng cũ.
        private const int EscalationNotifyThreshold = 3;
        private const int HardBlockThreshold = 4;

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public DeliveryTripService(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        private static DeliveryTripResponseDto ToDto(DeliveryTrip trip)
        {
            var totalWeight = trip.Orders?.Sum(o => o.TotalPackedWeightKg ?? 0) ?? 0;
            var capacity = trip.Vehicle?.Capacity;

            return new DeliveryTripResponseDto
            {
                Id = trip.Id,
                VehicleId = trip.VehicleId,
                VehicleNumber = trip.Vehicle?.VehicleNumber ?? 0,
                Shift = trip.Shift,
                TripDate = trip.TripDate,
                Status = trip.Status.ToString(),
                CreatedByUserId = trip.CreatedByUserId,
                CreatedAt = trip.CreatedAt,
                StartedAt = trip.StartedAt,
                CompletedAt = trip.CompletedAt,
                PlannedDepartureAt = trip.PlannedDepartureAt,
                PlannedArrivalAt = trip.PlannedArrivalAt,
                TotalWeightKg = totalWeight,
                VehicleCapacityKg = capacity,
                RemainingCapacityKg = capacity.HasValue ? capacity.Value - totalWeight : null,
                OrderIds = trip.Orders?.Select(o => o.Id).ToList() ?? new List<Guid>(),
                OrderCodes = trip.Orders?.Select(o => o.OrderCode).ToList() ?? new List<string>()
            };
        }

        private async Task<DeliveryTrip> LoadTripAsync(Guid tripId)
        {
            var trip = await _context.DeliveryTrips
                .Include(t => t.Vehicle)
                .Include(t => t.Orders).ThenInclude(o => o.CustomerProfile)
                .FirstOrDefaultAsync(t => t.Id == tripId);
            return trip ?? throw new KeyNotFoundException("Không tìm thấy chuyến giao hàng.");
        }

        // Báo KHÁCH HÀNG (không phải nhân viên) giờ nhận hàng dự kiến ngay khi Sale xác định được —
        // lỗi gửi thông báo cho 1 khách không được chặn các khách còn lại (best-effort, cô lập theo đơn).
        private async Task NotifyCustomersOfDeliveryTimeAsync(IEnumerable<Order> orders, DateTime? plannedArrivalAt, DateTime? plannedDepartureAt)
        {
            if (plannedArrivalAt == null && plannedDepartureAt == null) return;

            var timeText = plannedArrivalAt.HasValue
                ? $"khoảng {plannedArrivalAt.Value:HH:mm} ngày {plannedArrivalAt.Value:dd/MM/yyyy}"
                : $"sau khi xuất phát lúc {plannedDepartureAt!.Value:HH:mm} ngày {plannedDepartureAt.Value:dd/MM/yyyy}";

            foreach (var order in orders)
            {
                if (order.CustomerProfile == null) continue;
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_52_DeliveryTimeNotice,
                        order.CustomerProfile.UserId,
                        "Dự kiến giờ nhận hàng",
                        $"Đơn hàng {order.OrderCode} dự kiến giao đến bạn {timeText}.",
                        order.Id,
                        "Order");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DeliveryTripService] Lỗi gửi thông báo giờ nhận hàng cho đơn {order.Id}: {ex.Message}");
                }
            }
        }

        public async Task<DeliveryTripResponseDto> CreateTripAsync(Guid createdByUserId, CreateDeliveryTripRequestDto dto)
        {
            var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == dto.VehicleId);
            if (vehicle == null || !vehicle.IsActive)
                throw new VehicleNotAvailableException("Mã xe không hợp lệ hoặc xe đã ngừng hoạt động.");

            // BUGFIX: trước đây chỉ dựa vào RegularExpression cứng trên CreateDeliveryTripRequestDto.Shift
            // ("^(Sáng|Trưa|Chiều)$") — Admin đổi tên ca ở trang "Ca làm việc" không ảnh hưởng gì tới đây,
            // khiến Sale không thể tạo chuyến với tên ca mới đã đổi. Đọc trực tiếp từ WarehouseShifts.
            var validShifts = await _context.WarehouseShifts.OrderBy(s => s.StartTime).Select(s => s.Name).ToListAsync();
            if (!validShifts.Contains(dto.Shift))
                throw new InvalidOperationException($"Ca giao hàng không hợp lệ. Chọn: {string.Join(" / ", validShifts)}.");

            var tripDate = dto.TripDate.Date;

            // Kiểm tra ngày và ca giao hàng hết hạn (cùng quy tắc với ScheduleDeliveryAsync - OrderService.cs)
            var localNow = DateTime.UtcNow.AddHours(7); // Giả định múi giờ GMT+7 Việt Nam
            var localToday = localNow.Date;

            if (tripDate < localToday)
                throw new InvalidOperationException("Không thể lên lịch giao hàng cho ngày trong quá khứ.");

            if (tripDate == localToday)
            {
                var currentHour = localNow.Hour;
                if (dto.Shift == "Sáng" && currentHour >= 10)
                    throw new InvalidOperationException("Đã quá 10:00 AM, không thể thêm chuyến giao cho Ca sáng ngày hôm nay.");
                if (dto.Shift == "Trưa" && currentHour >= 14)
                    throw new InvalidOperationException("Đã quá 14:00 (2:00 PM), không thể thêm chuyến giao cho Ca trưa ngày hôm nay.");
                if (dto.Shift == "Chiều" && currentHour >= 22)
                    throw new InvalidOperationException("Đã quá 22:00 (10:00 PM), không thể thêm chuyến giao cho Ca chiều ngày hôm nay.");
            }

            var hasConflict = await _context.DeliveryTrips.AnyAsync(t =>
                t.VehicleId == dto.VehicleId
                && t.Shift == dto.Shift
                && t.TripDate.Date == tripDate
                && (t.Status == DeliveryTripStatus.Scheduled || t.Status == DeliveryTripStatus.Loading || t.Status == DeliveryTripStatus.InDelivery));

            if (hasConflict)
                throw new VehicleShiftConflictException(
                    $"Xe {vehicle.VehicleNumber} đã có chuyến giao trùng ca {dto.Shift} ngày {tripDate:dd/MM/yyyy}.");

            var candidateOrders = await _context.Orders
                .Where(o => dto.OrderIds.Contains(o.Id))
                .ToListAsync();

            var eligibleOrders = candidateOrders
                // Chỉ gom vào chuyến những đơn chưa được lập lịch giao ở luồng nào khác — cùng ràng buộc
                // với ScheduleDeliveryAsync (OrderService.cs) để 2 luồng không giẫm lên nhau.
                .Where(o => o.DeliveryStatus == DeliveryStatus.NotScheduled || o.DeliveryStatus == DeliveryStatus.Rescheduled)
                .ToList();

            VehicleCapacityGuard.EnsureWithinCapacity(vehicle, 0m, eligibleOrders.Sum(o => o.TotalPackedWeightKg ?? 0), eligibleOrders.Select(o => o.OrderCode).ToList());

            var trip = new DeliveryTrip
            {
                VehicleId = dto.VehicleId,
                Shift = dto.Shift,
                TripDate = tripDate,
                Status = DeliveryTripStatus.Scheduled,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.DeliveryTrips.Add(trip);

            foreach (var order in eligibleOrders)
            {
                order.DeliveryTripId = trip.Id;
                order.DeliveryStatus = DeliveryStatus.Scheduled;
                // Đồng bộ ngược 3 field cũ (DeliveryVehicleId/Shift/ScheduledDeliveryDate) mà
                // OrderDetail.jsx/SalesOrderDetailPage.tsx vẫn đang đọc để hiện ngày/ca giao —
                // luồng Trip-based không thay thế các field này, chỉ thêm DeliveryTripId song song.
                order.DeliveryVehicleId = vehicle.VehicleNumber;
                order.DeliveryShift = dto.Shift;
                order.ScheduledDeliveryDate = tripDate;
            }

            await _context.SaveChangesAsync();

            trip.Vehicle = vehicle;
            trip.Orders = eligibleOrders;
            return ToDto(trip);
        }

        public async Task<DeliveryTripResponseDto> StartLoadingAsync(Guid tripId, StartLoadingRequestDto dto)
        {
            var trip = await LoadTripAsync(tripId);

            if (trip.Status != DeliveryTripStatus.Scheduled)
                throw new InvalidOperationException("Chuyến giao không ở trạng thái chờ bốc hàng.");

            // Giờ xuất phát/đến dự kiến nhập tay ở bước bốc hàng — cùng quy tắc grace 5 phút với
            // StockTransferService.CreateAsync để không chặn nhầm khi nhập đúng "bây giờ".
            var localNow = DateTime.UtcNow.AddHours(7); // Giả định múi giờ GMT+7 Việt Nam
            if (dto.PlannedDepartureAt.HasValue && dto.PlannedDepartureAt.Value < localNow.AddMinutes(-5))
                throw new InvalidOperationException("Giờ xuất phát dự kiến không được ở trong quá khứ.");
            if (dto.PlannedArrivalAt.HasValue && dto.PlannedArrivalAt.Value < localNow.AddMinutes(-5))
                throw new InvalidOperationException("Giờ đến dự kiến không được ở trong quá khứ.");
            if (dto.PlannedDepartureAt.HasValue && dto.PlannedArrivalAt.HasValue && dto.PlannedArrivalAt.Value < dto.PlannedDepartureAt.Value)
                throw new InvalidOperationException("Giờ đến dự kiến không được trước giờ xuất phát dự kiến.");

            trip.Status = DeliveryTripStatus.Loading;
            if (dto.PlannedDepartureAt.HasValue) trip.PlannedDepartureAt = dto.PlannedDepartureAt;
            if (dto.PlannedArrivalAt.HasValue) trip.PlannedArrivalAt = dto.PlannedArrivalAt;

            await _context.SaveChangesAsync();

            await NotifyCustomersOfDeliveryTimeAsync(trip.Orders, trip.PlannedArrivalAt, trip.PlannedDepartureAt);

            return ToDto(trip);
        }

        public async Task<DeliveryTripResponseDto> AddOrdersToTripAsync(Guid tripId, AddOrdersToTripRequestDto dto)
        {
            var trip = await LoadTripAsync(tripId);

            if (trip.Status != DeliveryTripStatus.Loading)
                throw new InvalidOperationException("Chỉ có thể thêm đơn khi chuyến đang ở trạng thái Bốc hàng (Loading).");

            var candidateOrders = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Where(o => dto.OrderIds.Contains(o.Id))
                .ToListAsync();

            var eligibleOrders = candidateOrders
                .Where(o => o.DeliveryStatus == DeliveryStatus.NotScheduled || o.DeliveryStatus == DeliveryStatus.Rescheduled)
                .ToList();

            var currentWeight = trip.Orders.Sum(o => o.TotalPackedWeightKg ?? 0);
            VehicleCapacityGuard.EnsureWithinCapacity(trip.Vehicle, currentWeight, eligibleOrders.Sum(o => o.TotalPackedWeightKg ?? 0), eligibleOrders.Select(o => o.OrderCode).ToList());

            foreach (var order in eligibleOrders)
            {
                order.DeliveryTripId = trip.Id;
                order.DeliveryStatus = DeliveryStatus.Scheduled;
                order.DeliveryVehicleId = trip.Vehicle.VehicleNumber;
                order.DeliveryShift = trip.Shift;
                order.ScheduledDeliveryDate = trip.TripDate;
            }

            await _context.SaveChangesAsync();

            // Chuyến đã ở Loading nên có thể đã có giờ dự kiến từ StartLoadingAsync trước đó — báo
            // ngay cho khách của các đơn VỪA thêm (đơn cũ trong chuyến đã được báo từ trước rồi).
            await NotifyCustomersOfDeliveryTimeAsync(eligibleOrders, trip.PlannedArrivalAt, trip.PlannedDepartureAt);

            // BUGFIX: KHÔNG nối thủ công eligibleOrders vào trip.Orders — EF Core tự động fixup
            // navigation collection ngay khi order.DeliveryTripId được set ở trên (cùng DbContext),
            // nên trip.Orders đã chứa sẵn các đơn mới; Concat lại ở đây từng khiến ToDto() trả về
            // OrderIds/OrderCodes bị lặp đôi cho mọi đơn vừa thêm.
            return ToDto(trip);
        }

        public async Task<DeliveryTripResponseDto> RemoveOrderFromTripAsync(Guid tripId, Guid orderId)
        {
            var trip = await LoadTripAsync(tripId);

            if (trip.Status != DeliveryTripStatus.Loading)
                throw new InvalidOperationException("Chỉ có thể rút đơn khi chuyến đang ở trạng thái Bốc hàng (Loading).");

            var order = trip.Orders.FirstOrDefault(o => o.Id == orderId)
                ?? throw new KeyNotFoundException("Đơn hàng không thuộc chuyến giao này.");

            order.DeliveryTripId = null;
            order.DeliveryStatus = DeliveryStatus.NotScheduled;
            order.DeliveryVehicleId = null;
            order.DeliveryShift = null;
            order.ScheduledDeliveryDate = null;

            await _context.SaveChangesAsync();

            trip.Orders = trip.Orders.Where(o => o.Id != orderId).ToList();
            return ToDto(trip);
        }

        // Hủy cả chuyến (chưa xuất phát) để xếp các đơn đang gán sang chuyến/xe khác — trước đây
        // DeliveryTripStatus.Cancelled tồn tại trong enum nhưng không có nghiệp vụ nào gán tới.
        public async Task<DeliveryTripResponseDto> CancelTripAsync(Guid tripId)
        {
            var trip = await LoadTripAsync(tripId);

            if (trip.Status != DeliveryTripStatus.Scheduled && trip.Status != DeliveryTripStatus.Loading)
                throw new InvalidOperationException("Chỉ có thể hủy chuyến khi đang ở trạng thái Chờ bốc hàng hoặc Bốc hàng — chuyến đã xuất phát không thể hủy.");

            foreach (var order in trip.Orders)
            {
                order.DeliveryTripId = null;
                order.DeliveryStatus = DeliveryStatus.NotScheduled;
                order.DeliveryVehicleId = null;
                order.DeliveryShift = null;
                order.ScheduledDeliveryDate = null;
            }

            trip.Status = DeliveryTripStatus.Cancelled;
            trip.Orders = new List<Order>();

            await _context.SaveChangesAsync();
            return ToDto(trip);
        }

        public async Task<DeliveryTripResponseDto> StartTripAsync(Guid tripId)
        {
            var trip = await LoadTripAsync(tripId);

            if (trip.Status != DeliveryTripStatus.Loading)
                throw new InvalidOperationException("Chuyến giao phải ở trạng thái Bốc hàng (Loading) trước khi xuất phát.");

            var orderIds = trip.Orders.Select(o => o.Id).ToList();
            var confirmedHandoverOrderIds = await _context.HandoverRecords
                .Where(h => orderIds.Contains(h.OrderId) && h.Status == HandoverStatus.Confirmed)
                .Select(h => h.OrderId)
                .ToListAsync();

            var missingHandover = orderIds.Except(confirmedHandoverOrderIds).ToList();
            if (missingHandover.Any())
                throw new HandoverNotReadyException(
                    $"Còn {missingHandover.Count} đơn hàng trong chuyến chưa được bàn giao kho-sale xác nhận (HandoverRecord).");

            trip.Status = DeliveryTripStatus.InDelivery;
            trip.StartedAt = DateTime.UtcNow;
            foreach (var order in trip.Orders)
            {
                order.DeliveryStatus = DeliveryStatus.InDelivery;
            }

            await _context.SaveChangesAsync();
            return ToDto(trip);
        }

        public async Task<DeliveryTripResponseDto> GetTripByIdAsync(Guid tripId)
        {
            var trip = await LoadTripAsync(tripId);
            return ToDto(trip);
        }

        public async Task<List<DeliveryTripResponseDto>> GetTripsAsync(DateTime? date, string? status)
        {
            var query = _context.DeliveryTrips
                .Include(t => t.Vehicle)
                .Include(t => t.Orders)
                .AsQueryable();

            if (date.HasValue)
                query = query.Where(t => t.TripDate.Date == date.Value.Date);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<DeliveryTripStatus>(status, out var statusEnum))
                query = query.Where(t => t.Status == statusEnum);

            var trips = await query.OrderByDescending(t => t.TripDate).ThenBy(t => t.Shift).ToListAsync();
            return trips.Select(ToDto).ToList();
        }

        public async Task<RecordDeliveryAttemptResponseDto> RecordAttemptAsync(Guid recordedByUserId, RecordDeliveryAttemptRequestDto dto)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId)
                ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.DeliveryTripId == null)
                throw new InvalidOperationException("Đơn hàng chưa được gán vào chuyến giao nào.");

            if (order.IsBlockedForDelivery)
                throw new DeliveryEscalationRequiredException(
                    "Đơn hàng đã bị khóa do giao thất bại vượt ngưỡng, cần Sales Manager xử lý.");

            var tripId = order.DeliveryTripId.Value;
            var attemptNumber = await _context.DeliveryAttempts
                .Where(a => a.OrderId == order.Id && a.DeliveryTripId == tripId)
                .CountAsync() + 1;

            var outcome = Enum.Parse<DeliveryAttemptOutcome>(dto.Outcome);

            if (outcome == DeliveryAttemptOutcome.Delivered)
            {
                if (string.IsNullOrWhiteSpace(dto.PhotoUrl) || string.IsNullOrWhiteSpace(dto.SignatureUrl))
                    throw new ArgumentException("Giao hàng thành công bắt buộc phải có ảnh hiện trường và chữ ký khách hàng.");

                order.DeliveredAt = DateTime.UtcNow;
                order.DeliveryStatus = DeliveryStatus.Delivered;
                order.CustomerSignatureUrl = dto.SignatureUrl;
                order.DeliveryPhotoUrl = dto.PhotoUrl;

                _context.DeliveryAttempts.Add(new DeliveryAttempt
                {
                    OrderId = order.Id,
                    DeliveryTripId = tripId,
                    AttemptNumber = attemptNumber,
                    Outcome = DeliveryAttemptOutcome.Delivered,
                    PhotoUrl = dto.PhotoUrl,
                    SignatureUrl = dto.SignatureUrl,
                    RecordedByUserId = recordedByUserId,
                    AttemptedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return new RecordDeliveryAttemptResponseDto
                {
                    OrderId = order.Id,
                    AttemptNumber = attemptNumber,
                    Outcome = outcome.ToString(),
                    NewDeliveryStatus = order.DeliveryStatus.ToString(),
                    FailedDeliveryCount = order.FailedDeliveryCount,
                    IsBlockedForDelivery = order.IsBlockedForDelivery,
                    Message = "Giao hàng thành công."
                };
            }

            // Outcome == Failed
            order.FailedDeliveryCount++;
            order.DeliveryStatus = DeliveryStatus.Failed;

            _context.DeliveryAttempts.Add(new DeliveryAttempt
            {
                OrderId = order.Id,
                DeliveryTripId = tripId,
                AttemptNumber = attemptNumber,
                Outcome = DeliveryAttemptOutcome.Failed,
                FailureReason = dto.FailureReason,
                RecordedByUserId = recordedByUserId,
                AttemptedAt = DateTime.UtcNow
            });

            var reachedHardBlock = order.FailedDeliveryCount >= HardBlockThreshold;
            if (reachedHardBlock)
            {
                order.IsBlockedForDelivery = true;
            }

            await _context.SaveChangesAsync();

            if (order.FailedDeliveryCount == EscalationNotifyThreshold)
            {
                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_39_DeliveryTripAttemptEscalation,
                        SystemRole.SalesManager,
                        "Đơn hàng giao thất bại nhiều lần",
                        $"Đơn hàng {order.OrderCode} đã giao thất bại {order.FailedDeliveryCount} lần trong chuyến giao. Cần theo dõi.",
                        order.Id,
                        "Order");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DeliveryTripService] Error sending escalation notification: {ex.Message}");
                }
            }

            if (reachedHardBlock)
                throw new DeliveryEscalationRequiredException(
                    $"Đơn hàng {order.OrderCode} đã giao thất bại {order.FailedDeliveryCount} lần, hệ thống đã khóa và cần Sales Manager xử lý.");

            return new RecordDeliveryAttemptResponseDto
            {
                OrderId = order.Id,
                AttemptNumber = attemptNumber,
                Outcome = outcome.ToString(),
                NewDeliveryStatus = order.DeliveryStatus.ToString(),
                FailedDeliveryCount = order.FailedDeliveryCount,
                IsBlockedForDelivery = order.IsBlockedForDelivery,
                Message = "Đã ghi nhận giao hàng thất bại."
            };
        }

        public async Task<RecordCollectionResponseDto> RecordCollectionAsync(RecordCollectionRequestDto dto)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId)
                ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("Đơn hàng đã được thanh toán, không được thu thêm tiền mặt (COD).");

            var remaining = order.FinalPayment - order.AmountPaid;
            if (dto.AmountCollected < 0 || dto.AmountCollected > remaining)
                throw new ArgumentException($"Số tiền thu không hợp lệ. Số tiền còn phải thu tối đa là {remaining:N0}đ.");

            order.AmountPaid += dto.AmountCollected;
            var remainingAfter = order.FinalPayment - order.AmountPaid;

            var debtCreated = false;
            if (remainingAfter > 0)
            {
                await UpsertCustomerDebtAsync(order, remainingAfter);
                debtCreated = true;
                order.PaymentStatus = PaymentStatus.PartiallyPaid;
            }
            else
            {
                order.PaymentStatus = PaymentStatus.Paid;
                // Thu đủ ở lần thu này -> nếu trước đó đã tạo công nợ (thu từng phần), phải tất toán
                // luôn, nếu không dòng CustomerDebt cũ sẽ bị "mồ côi": còn Status=InDebt với DebtAmount
                // cũ dù đơn đã trả đủ.
                await SettleOpenDebtIfAnyAsync(order.Id);
            }

            await _context.SaveChangesAsync();

            return new RecordCollectionResponseDto
            {
                OrderId = order.Id,
                AmountPaid = order.AmountPaid,
                RemainingDebt = Math.Max(remainingAfter, 0),
                DebtRecordCreated = debtCreated,
                Message = debtCreated
                    ? $"Đã thu {dto.AmountCollected:N0}đ. Khách còn nợ {remainingAfter:N0}đ, đã cập nhật sổ công nợ."
                    : $"Đã thu {dto.AmountCollected:N0}đ. Đơn hàng đã thanh toán đủ."
            };
        }

        // Cùng pattern tạo CustomerDebt đã dùng trong OrderService.RecordDeliveryResultAsync
        // (L2453-2464) — tách thành helper riêng cho luồng Trip-based, không đụng tới OrderService
        // để tránh ảnh hưởng luồng theo Order cũ đã pass test. Nếu đơn đã có khoản nợ đang mở (InDebt)
        // thì cập nhật số tiền thay vì tạo dòng mới (RecordCollectionAsync có thể được gọi nhiều lần
        // cho cùng 1 đơn trong 1 chuyến — thu COD từng phần).
        private async Task UpsertCustomerDebtAsync(Order order, decimal debtAmount)
        {
            var existingDebt = await _context.CustomerDebts
                .FirstOrDefaultAsync(d => d.OrderId == order.Id && d.Status == DebtStatus.InDebt);

            if (existingDebt != null)
            {
                existingDebt.DebtAmount = debtAmount;
                return;
            }

            _context.CustomerDebts.Add(new CustomerDebt
            {
                CustomerProfileId = order.CustomerProfileId,
                OrderId = order.Id,
                DebtAmount = debtAmount,
                Status = DebtStatus.InDebt,
                OverdueDays = 0
            });
        }

        private async Task SettleOpenDebtIfAnyAsync(Guid orderId)
        {
            var existingDebt = await _context.CustomerDebts
                .FirstOrDefaultAsync(d => d.OrderId == orderId && d.Status == DebtStatus.InDebt);
            if (existingDebt == null) return;

            existingDebt.Status = DebtStatus.Settled;
            existingDebt.DebtAmount = 0;
            existingDebt.SettledAt = DateTime.UtcNow;
            existingDebt.SettlementNote = "Tự động tất toán: khách đã thu đủ qua COD.";
        }
    }
}
