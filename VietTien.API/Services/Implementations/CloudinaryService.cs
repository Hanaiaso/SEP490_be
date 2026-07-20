using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(IOptions<CloudinarySettings> options)
        {
            var settings = options.Value;
            var account = new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret);
            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
        }

        public async Task<string> UploadImageAsync(IFormFile file, string folder)
        {
            if (file.Length <= 0)
                throw new ArgumentException("File rỗng");

            await using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                Transformation = new Transformation()
                    .Width(500).Height(500).Crop("fill").Gravity("face")
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new Exception($"Cloudinary upload error: {result.Error.Message}");

            return result.SecureUrl.ToString();
        }

        public async Task<string> UploadBase64ImageAsync(string base64String, string folder, string fileName)
        {
            if (string.IsNullOrEmpty(base64String))
                throw new ArgumentException("Base64 string rỗng");

            // Bỏ prefix base64 nếu có (e.g. data:image/png;base64,)
            var base64Data = base64String;
            if (base64Data.Contains(","))
            {
                base64Data = base64Data[(base64Data.IndexOf(",") + 1)..];
            }

            var bytes = Convert.FromBase64String(base64Data);
            var stream = new MemoryStream(bytes);

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = folder
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new Exception($"Cloudinary upload error: {result.Error.Message}");

            return result.SecureUrl.ToString();
        }
        
        // Bản upload không crop dành cho ảnh bằng chứng (bản UploadImageAsync crop 500x500 chỉ hợp avatar)
        public async Task<string> UploadEvidenceAsync(IFormFile file, string folder)
        {
            if (file.Length <= 0)
                throw new ArgumentException("File rỗng");

            await using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new Exception($"Cloudinary upload error: {result.Error.Message}");

            return result.SecureUrl.ToString();
        }

        // Đuôi file Cloudinary xử lý được theo resource "image" (PDF được Cloudinary coi như tài liệu ảnh nhiều trang)
        private static readonly string[] ImageLikeExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

        // File đính kèm đa định dạng: ảnh/PDF upload theo resource image, còn lại (DOC/DOCX...) theo resource raw
        public async Task<string> UploadAttachmentAsync(IFormFile file, string folder)
        {
            if (file.Length <= 0)
                throw new ArgumentException("File rỗng");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            await using var stream = file.OpenReadStream();

            if (ImageLikeExtensions.Contains(extension))
            {
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder
                };
                var result = await _cloudinary.UploadAsync(uploadParams);
                if (result.Error != null)
                    throw new Exception($"Cloudinary upload error: {result.Error.Message}");
                return result.SecureUrl.ToString();
            }
            else
            {
                // Giữ tên file gốc (kèm hậu tố chống trùng) để URL đọc được tên tài liệu
                var rawParams = new RawUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder,
                    UseFilename = true,
                    UniqueFilename = true
                };
                var result = await _cloudinary.UploadAsync(rawParams);
                if (result.Error != null)
                    throw new Exception($"Cloudinary upload error: {result.Error.Message}");
                return result.SecureUrl.ToString();
            }
        }

        public async Task<bool> DeleteImageAsync(string publicId)
        {
            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deleteParams);
            return result.Result == "ok";
        }

        public string? ExtractPublicId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                // Cloudinary URL format: https://res.cloudinary.com/{cloud}/image/upload/v{version}/{folder}/{filename}
                var uri = new Uri(url);
                var path = uri.AbsolutePath;

                // Tìm phần sau "/upload/" và bỏ extension
                var uploadIndex = path.IndexOf("/upload/", StringComparison.Ordinal);
                if (uploadIndex < 0) return null;

                var afterUpload = path[(uploadIndex + "/upload/".Length)..];

                // Bỏ version prefix (v1234567890/)
                if (afterUpload.StartsWith("v") && afterUpload.Contains('/'))
                {
                    var slashIndex = afterUpload.IndexOf('/');
                    afterUpload = afterUpload[(slashIndex + 1)..];
                }

                // Bỏ extension
                var dotIndex = afterUpload.LastIndexOf('.');
                if (dotIndex > 0)
                    afterUpload = afterUpload[..dotIndex];

                return afterUpload;
            }
            catch
            {
                return null;
            }
        }
    }

    public class CloudinarySettings
    {
        public string CloudName { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }
}
