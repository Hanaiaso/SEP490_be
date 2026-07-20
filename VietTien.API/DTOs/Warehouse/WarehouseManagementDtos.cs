using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class LocationDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class WarehouseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public List<LocationDto> Locations { get; set; } = new List<LocationDto>();
    }

    public class CreateWarehouseDto
    {
        [Required(ErrorMessage = "Tên kho không được để trống")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã kho không được để trống")]
        public string Code { get; set; } = string.Empty;

        public List<string> LocationNames { get; set; } = new List<string>();
    }

    public class UpdateWarehouseDto
    {
        [Required(ErrorMessage = "Tên kho không được để trống")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã kho không được để trống")]
        public string Code { get; set; } = string.Empty;

        public List<string> LocationNames { get; set; } = new List<string>();
    }
}
