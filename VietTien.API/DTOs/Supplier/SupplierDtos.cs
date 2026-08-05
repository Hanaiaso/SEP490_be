using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Supplier
{
    public class SupplierDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? TaxCode { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateSupplierRequest
    {
        [Required(ErrorMessage = "Tên nhà cung cấp là bắt buộc.")]
        [MaxLength(300)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã nhà cung cấp là bắt buộc.")]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? ContactPerson { get; set; }

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? Phone { get; set; }

        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [MaxLength(256)]
        public string? Email { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(50)]
        public string? TaxCode { get; set; }
    }

    public class UpdateSupplierRequest : CreateSupplierRequest
    {
        public bool IsActive { get; set; }
    }
}
