using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class VehicleService : IVehicleService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public VehicleService(ApplicationDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        public async Task<List<VehicleDto>> GetAllAsync()
        {
            var vehicles = await _context.Vehicles.AsNoTracking().OrderBy(v => v.VehicleNumber).ToListAsync();
            return vehicles.Select(ToDto).ToList();
        }

        public async Task<VehicleDto> GetByIdAsync(Guid id)
        {
            var vehicle = await _context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle == null) throw new KeyNotFoundException("Không tìm thấy xe.");
            return ToDto(vehicle);
        }

        public async Task<VehicleDto> CreateAsync(CreateVehicleRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            if (request.VehicleNumber <= 0)
                throw new Exception("Số xe phải lớn hơn 0.");

            if (string.IsNullOrWhiteSpace(request.LicensePlate))
                throw new Exception("Biển số xe không được để trống.");

            if (await _context.Vehicles.AnyAsync(v => v.VehicleNumber == request.VehicleNumber))
                throw new InvalidOperationException($"Số xe {request.VehicleNumber} đã tồn tại.");

            var plate = request.LicensePlate.Trim();
            if (await _context.Vehicles.AnyAsync(v => v.LicensePlate == plate))
                throw new InvalidOperationException($"Biển số xe {plate} đã tồn tại.");

            var vehicle = new Vehicle
            {
                VehicleNumber = request.VehicleNumber,
                LicensePlate = plate,
                Capacity = request.Capacity,
                Note = request.Note,
                IsActive = true
            };

            _context.Vehicles.Add(vehicle);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Lưới an toàn cuối cho race check-then-insert ở 2 AnyAsync (VehicleNumber/LicensePlate)
                // phía trên: 2 request đồng thời cùng tạo xe trùng số xe hoặc biển số đều pass check rồi
                // mới đụng unique index lúc SaveChanges -> request thua cuộc nhận lỗi rõ ràng thay vì 500.
                throw new InvalidOperationException($"Số xe {request.VehicleNumber} hoặc biển số xe {plate} đã tồn tại.");
            }

            var dto = ToDto(vehicle);

            await _auditLogService.LogAsync(
                entityName: "Vehicle",
                entityId: vehicle.Id.ToString(),
                action: "CREATE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: null,
                after: dto,
                reason: "Admin tạo xe mới",
                ipAddress: ipAddress);

            return dto;
        }

        public async Task<VehicleDto> UpdateAsync(Guid id, UpdateVehicleRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle == null) throw new KeyNotFoundException("Không tìm thấy xe.");

            if (string.IsNullOrWhiteSpace(request.LicensePlate))
                throw new Exception("Biển số xe không được để trống.");

            var updatedPlate = request.LicensePlate.Trim();
            if (await _context.Vehicles.AnyAsync(v => v.Id != id && v.LicensePlate == updatedPlate))
                throw new InvalidOperationException($"Biển số xe {updatedPlate} đã tồn tại.");

            // Chuyển sang bảo trì (IsActive: true -> false) khi xe đang có chuyến giao chưa hoàn tất
            // xếp lịch từ hôm nay trở đi -> phải xử lý/chuyển chuyến trước, không cho khoá thẳng.
            if (vehicle.IsActive && !request.IsActive)
            {
                var today = DateTime.UtcNow.Date;
                var hasUpcomingDelivery = await _context.Orders.AnyAsync(o =>
                    o.DeliveryVehicleId == vehicle.VehicleNumber &&
                    o.ScheduledDeliveryDate.HasValue && o.ScheduledDeliveryDate.Value.Date >= today &&
                    o.DeliveryStatus != DeliveryStatus.Delivered &&
                    o.DeliveryStatus != DeliveryStatus.Cancelled);

                if (hasUpcomingDelivery)
                    throw new InvalidOperationException("Xe đang có chuyến giao đã xếp lịch chưa hoàn tất — vui lòng chuyển chuyến sang xe khác trước khi đưa xe vào bảo trì.");
            }

            var before = ToDto(vehicle);

            vehicle.LicensePlate = updatedPlate;
            vehicle.Capacity = request.Capacity;
            vehicle.IsActive = request.IsActive;
            vehicle.Note = request.Note;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                throw new InvalidOperationException($"Biển số xe {updatedPlate} đã tồn tại.");
            }

            var after = ToDto(vehicle);

            await _auditLogService.LogAsync(
                entityName: "Vehicle",
                entityId: vehicle.Id.ToString(),
                action: "UPDATE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: before,
                after: after,
                reason: "Admin cập nhật thông tin xe",
                ipAddress: ipAddress);

            return after;
        }

        // SQL Server error 2601 (unique index) / 2627 (unique constraint) — dùng để phân biệt vi phạm
        // unique index (Vehicle.VehicleNumber/LicensePlate) khỏi các lỗi DbUpdateException khác không
        // nên bị nuốt thành 409.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

        private static VehicleDto ToDto(Vehicle v) => new()
        {
            Id = v.Id,
            VehicleNumber = v.VehicleNumber,
            LicensePlate = v.LicensePlate,
            Capacity = v.Capacity,
            IsActive = v.IsActive,
            Note = v.Note
        };
    }
}
