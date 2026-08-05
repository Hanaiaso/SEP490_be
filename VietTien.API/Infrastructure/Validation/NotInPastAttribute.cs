using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Infrastructure.Validation
{
    /// <summary>
    /// Validates a DateTime (or nullable DateTime) is not earlier than today,
    /// compared using GMT+7 (Vietnam) local date to match the rest of the delivery flow.
    /// </summary>
    public class NotInPastAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not DateTime date)
                return ValidationResult.Success;

            var localToday = DateTime.UtcNow.AddHours(7).Date;
            if (date.Date < localToday)
                return new ValidationResult(ErrorMessage ?? "Ngày không được ở trong quá khứ.");

            return ValidationResult.Success;
        }
    }
}
