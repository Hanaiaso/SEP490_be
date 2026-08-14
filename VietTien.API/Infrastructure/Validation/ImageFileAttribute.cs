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

            var error = Validate(file, _maxBytes);
            return error is null ? ValidationResult.Success : new ValidationResult(error);
        }

        /// <summary>Validate thủ công 1 IFormFile (dùng cho danh sách nhiều file, nơi ValidationAttribute không áp dụng trực tiếp được)</summary>
        public static string? Validate(IFormFile file, int maxMegabytes)
            => Validate(file, (long)maxMegabytes * 1024 * 1024);

        private static string? Validate(IFormFile file, long maxBytes)
        {
            if (file.Length == 0)
                return "File ảnh không được để trống.";

            if (file.Length > maxBytes)
                return $"Kích thước ảnh vượt quá giới hạn {maxBytes / (1024 * 1024)}MB.";

            if (!AllowedContentTypes.Contains(file.ContentType?.ToLowerInvariant()))
                return "Định dạng ảnh không hợp lệ. Chỉ chấp nhận JPEG, PNG, WEBP hoặc GIF.";

            return null;
        }
    }
}
