using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Admin
{
    public class VehicleDto
    {
        public Guid Id { get; set; }
        public int VehicleNumber { get; set; }
        public string LicensePlate { get; set; } = string.Empty;
        public decimal? Capacity { get; set; }
        public bool IsActive { get; set; }
        public string? Note { get; set; }
    }

    public class CreateVehicleRequest
    {
        [Range(1, int.MaxValue, ErrorMessage = "Số hiệu xe phải lớn hơn 0")]
        public int VehicleNumber { get; set; }

        [Required(ErrorMessage = "Biển số xe là bắt buộc.")]
        [MaxLength(20, ErrorMessage = "Biển số xe không được vượt quá 20 ký tự.")]
        public string LicensePlate { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Tải trọng phải lớn hơn hoặc bằng 0")]
        public decimal? Capacity { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class UpdateVehicleRequest
    {
        [Required(ErrorMessage = "Biển số xe là bắt buộc.")]
        [MaxLength(20, ErrorMessage = "Biển số xe không được vượt quá 20 ký tự.")]
        public string LicensePlate { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Tải trọng phải lớn hơn hoặc bằng 0")]
        public decimal? Capacity { get; set; }

        public bool IsActive { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
