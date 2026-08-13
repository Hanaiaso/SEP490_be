using VietTien.API.DTOs.UserProfile;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class UserProfileService : IUserProfileService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICloudinaryService _cloudinaryService;
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

        public UserProfileService(IUnitOfWork unitOfWork, ICloudinaryService cloudinaryService)
        {
            _unitOfWork = unitOfWork;
            _cloudinaryService = cloudinaryService;
        }

        public async Task<UserProfileDto> GetProfileAsync(Guid userId)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

            // Tìm địa chỉ mặc định qua CustomerProfile
            string? defaultAddressId = null;
            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile != null)
            {
                var addresses = await _unitOfWork.Addresses.GetByCustomerProfileIdAsync(profile.Id);
                var defaultAddr = addresses.FirstOrDefault(a => a.IsDefault);
                defaultAddressId = defaultAddr?.Id.ToString();
            }

            return new UserProfileDto
            {
                Id = user.Id.ToString(),
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                AvatarUrl = user.AvatarUrl,
                DefaultAddressId = defaultAddressId,
                CreatedAt = user.CreatedAt,
                EmailVerified = user.IsEmailVerified
            };
        }

        public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileDto dto)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

            // Cập nhật thông tin
            user.FullName = dto.FullName.Trim();
            user.PhoneNumber = dto.PhoneNumber.Trim();

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return new UserProfileDto
            {
                Id = user.Id.ToString(),
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                AvatarUrl = user.AvatarUrl
            };
        }

        public async Task<AvatarResponseDto> UploadAvatarAsync(Guid userId, IFormFile file)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

            // Validate file
            if (file == null || file.Length == 0)
                throw new ArgumentException("File không được để trống.");

            if (file.Length > MaxFileSize)
                throw new ArgumentException("File phải có kích thước không vượt quá 5MB.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                throw new ArgumentException("File phải có định dạng JPG, PNG hoặc WEBP.");

            // L3-SEC-14: đuôi file do chính người gửi đặt, không đáng tin — đọc magic byte thật của
            // nội dung để chặn file PE/EXE (hay bất kỳ định dạng nào khác) đổi đuôi thành .png/.jpg.
            await using (var contentStream = file.OpenReadStream())
            {
                if (!await FileSignatureValidator.IsAllowedImageAsync(contentStream))
                    throw new ArgumentException("Nội dung file không đúng định dạng ảnh JPG, PNG hoặc WEBP.");
            }

            // Xóa avatar cũ trên Cloudinary nếu có
            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                var oldPublicId = _cloudinaryService.ExtractPublicId(user.AvatarUrl);
                if (oldPublicId != null)
                {
                    await _cloudinaryService.DeleteImageAsync(oldPublicId);
                }
            }

            // Upload avatar mới
            var avatarUrl = await _cloudinaryService.UploadImageAsync(file, "viettien/avatars");

            // Cập nhật DB
            user.AvatarUrl = avatarUrl;
            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return new AvatarResponseDto { AvatarUrl = avatarUrl };
        }

        public async Task DeleteAvatarAsync(Guid userId)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                var publicId = _cloudinaryService.ExtractPublicId(user.AvatarUrl);
                if (publicId != null)
                {
                    await _cloudinaryService.DeleteImageAsync(publicId);
                }
            }

            user.AvatarUrl = null;
            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId)
                ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

            // User đăng nhập bằng Google không có password
            if (string.IsNullOrEmpty(user.PasswordHash))
                throw new InvalidOperationException("Tài khoản đăng nhập bằng Google không thể đổi mật khẩu bằng cách này.");

            // Xác minh mật khẩu hiện tại
            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                throw new UnauthorizedAccessException("Mật khẩu hiện tại không chính xác.");

            // Hash mật khẩu mới
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
