using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Infrastructure.Validation
{
    /// <summary>
    /// Validates that a (optionally data-URI prefixed) base64 string decodes to a
    /// non-empty PDF file (magic header "%PDF") within a maximum size.
    /// </summary>
    public class Base64PdfAttribute : ValidationAttribute
    {
        private readonly int _maxBytes;

        public Base64PdfAttribute(int maxMegabytes = 10)
        {
            _maxBytes = maxMegabytes * 1024 * 1024;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return ValidationResult.Success;

            var base64Data = raw.Contains(',') ? raw.Split(',')[1] : raw;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64Data);
            }
            catch (FormatException)
            {
                return new ValidationResult("Dữ liệu file không đúng định dạng base64.");
            }

            if (bytes.Length == 0)
                return new ValidationResult("Dữ liệu file không được để trống.");

            if (bytes.Length > _maxBytes)
                return new ValidationResult($"Kích thước file vượt quá giới hạn {_maxBytes / (1024 * 1024)}MB.");

            if (bytes.Length < 4 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
                return new ValidationResult("File tải lên không phải định dạng PDF hợp lệ.");

            return ValidationResult.Success;
        }
    }
}
