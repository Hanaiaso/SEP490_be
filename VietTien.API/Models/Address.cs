namespace VietTien.API.Models
{
    public class Address
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerProfileId { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string Ward { get; set; } = string.Empty;
        public string SpecificAddress { get; set; } = string.Empty;

        // Mã đơn vị hành chính (lấy từ API tỉnh thành bên FE) — phục vụ chuẩn hoá, không nhập tay
        public string? ProvinceCode { get; set; }
        public string? DistrictCode { get; set; }
        public string? WardCode { get; set; }

        // Toạ độ từ nút "Lấy vị trí hiện tại" (geolocation)
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public AddressType Type { get; set; } = AddressType.NhaRieng;
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        public CustomerProfile CustomerProfile { get; set; } = null!;
    }
}
