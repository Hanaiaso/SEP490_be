using VietTien.API.DTOs.UserProfile;

namespace VietTien.API.Services.Interfaces
{
    public interface IUserProfileService
    {
        /// <summary>Lấy thông tin cá nhân</summary>
        Task<UserProfileDto> GetProfileAsync(Guid userId);

        /// <summary>Cập nhật thông tin cá nhân</summary>
        Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileDto dto);

        /// <summary>Upload ảnh đại diện</summary>
        Task<AvatarResponseDto> UploadAvatarAsync(Guid userId, IFormFile file);

        /// <summary>Xóa ảnh đại diện</summary>
        Task DeleteAvatarAsync(Guid userId);

        /// <summary>Đổi mật khẩu</summary>
        Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto);
    }
}
