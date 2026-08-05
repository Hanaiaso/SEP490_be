using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Admin
{
    public class DiscountTierDto
    {
        public Guid Id { get; set; }
        public decimal MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public bool IsActive { get; set; }
        public string? Description { get; set; }
    }

    public class CreateDiscountTierRequest : IValidatableObject
    {
        [Range(0, double.MaxValue, ErrorMessage = "Ngưỡng tiền tối thiểu phải lớn hơn hoặc bằng 0")]
        public decimal MinAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Ngưỡng tiền tối đa phải lớn hơn hoặc bằng 0")]
        public decimal? MaxAmount { get; set; }

        [Range(0, 1, ErrorMessage = "Phần trăm giảm giá phải trong khoảng 0-1 (ví dụ 0.05 = 5%)")]
        public decimal DiscountPercent { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (MaxAmount.HasValue && MaxAmount.Value <= MinAmount)
            {
                yield return new ValidationResult(
                    "Ngưỡng tiền tối đa phải lớn hơn ngưỡng tiền tối thiểu.",
                    new[] { nameof(MaxAmount) });
            }
        }
    }

    public class UpdateDiscountTierRequest : IValidatableObject
    {
        [Range(0, double.MaxValue, ErrorMessage = "Ngưỡng tiền tối thiểu phải lớn hơn hoặc bằng 0")]
        public decimal MinAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Ngưỡng tiền tối đa phải lớn hơn hoặc bằng 0")]
        public decimal? MaxAmount { get; set; }

        [Range(0, 1, ErrorMessage = "Phần trăm giảm giá phải trong khoảng 0-1 (ví dụ 0.05 = 5%)")]
        public decimal DiscountPercent { get; set; }

        public bool IsActive { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (MaxAmount.HasValue && MaxAmount.Value <= MinAmount)
            {
                yield return new ValidationResult(
                    "Ngưỡng tiền tối đa phải lớn hơn ngưỡng tiền tối thiểu.",
                    new[] { nameof(MaxAmount) });
            }
        }
    }
}
