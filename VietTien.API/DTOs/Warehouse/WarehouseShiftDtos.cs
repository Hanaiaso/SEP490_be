using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class WarehouseShiftDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class CreateWarehouseShiftRequest
    {
        [Required(ErrorMessage = "Tên ca là bắt buộc.")]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Giờ bắt đầu là bắt buộc.")]
        public string StartTime { get; set; } = string.Empty; // "HH:mm"

        [Required(ErrorMessage = "Giờ kết thúc là bắt buộc.")]
        public string EndTime { get; set; } = string.Empty; // "HH:mm"

        [MaxLength(500)]
        public string? Description { get; set; }
    }

    public class UpdateWarehouseShiftRequest : CreateWarehouseShiftRequest
    {
    }
}
