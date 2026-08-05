using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace VietTien.API.Infrastructure.Validation
{
    /// <summary>
    /// Validates an optional uploaded IFormFile is within a max size and has an
    /// allowed image content type.
    /// </summary>
    public class ImageFileAttribute : ValidationAttribute
    {
        private static readonly string[] AllowedContentTypes =
        {
            "image/jpeg", "image/png", "image/webp", "image/gif"
        };

        private readonly long _maxBytes;

        public ImageFileAttribute(int maxMegabytes = 5)
        {
            _maxBytes = (long)maxMegabytes * 1024 * 1024;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
                return ValidationResult.Success;

            if (file.Length == 0)
                return new ValidationResult("File ảnh không được để trống.");

            if (file.Length > _maxBytes)
                return new ValidationResult($"Kích thước ảnh vượt quá giới hạn {_maxBytes / (1024 * 1024)}MB.");

            if (!AllowedContentTypes.Contains(file.ContentType?.ToLowerInvariant()))
                return new ValidationResult("Định dạng ảnh không hợp lệ. Chỉ chấp nhận JPEG, PNG, WEBP hoặc GIF.");

            return ValidationResult.Success;
        }
    }
}
