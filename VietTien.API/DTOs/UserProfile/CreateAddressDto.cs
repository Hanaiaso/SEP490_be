using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.UserProfile
{
    public class CreateAddressDto
    {
        [Required(ErrorMessage = "Tên người nhận không được để trống")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên người nhận phải từ 2 đến 100 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại không được để trống")]
        [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải có 10 số và bắt đầu bằng 0")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tỉnh/Thành phố không được để trống")]
        public string City { get; set; } = string.Empty;

        // Sau sáp nhập 07/2025 bỏ cấp Quận/Huyện (mô hình 2 cấp Tỉnh → Phường/Xã) → không còn bắt buộc
        public string District { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phường/Xã không được để trống")]
        public string Ward { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số nhà, tên đường không được để trống")]
        public string AddressLine { get; set; } = string.Empty;

        // Mã đơn vị hành chính (FE gửi kèm từ API tỉnh thành) — tuỳ chọn
        public string? ProvinceCode { get; set; }
        public string? DistrictCode { get; set; }
        public string? WardCode { get; set; }

        // Toạ độ từ nút "Lấy vị trí hiện tại" — tuỳ chọn
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [Required(ErrorMessage = "Loại địa chỉ không được để trống")]
        [RegularExpression(@"^(Nhà riêng|Công ty|Kho hàng)$",
            ErrorMessage = "Loại địa chỉ phải là: Nhà riêng, Công ty hoặc Kho hàng")]
        public string Type { get; set; } = "Nhà riêng";

        public bool IsDefault { get; set; } = false;
    }
}

