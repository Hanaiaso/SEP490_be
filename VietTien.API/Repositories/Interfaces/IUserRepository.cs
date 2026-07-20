using VietTien.API.Models;

namespace VietTien.API.Repositories.Interfaces
{
    public interface IUserRepository
    {
        /// <summary>Lấy user theo ID</summary>
        Task<User?> GetByIdAsync(Guid id);

        /// <summary>Lấy user theo Email</summary>
        Task<User?> GetByEmailAsync(string email);

        /// <summary>Lấy user theo Google ID</summary>
        Task<User?> GetByGoogleIdAsync(string googleId);

        /// <summary>Lấy user theo Refresh Token</summary>
        Task<User?> GetByRefreshTokenAsync(string refreshToken);

        /// <summary>Lấy user theo Password Reset Token</summary>
        Task<User?> GetByResetTokenAsync(string resetToken);

        /// <summary>Kiểm tra email đã tồn tại chưa</summary>
        Task<bool> EmailExistsAsync(string email);

        /// <summary>Kiểm tra số điện thoại đã tồn tại chưa</summary>
        Task<bool> PhoneExistsAsync(string phone);

        /// <summary>Thêm user mới</summary>
        Task AddAsync(User user);

        /// <summary>Cập nhật user</summary>
        void Update(User user);

        /// <summary>Lấy tất cả users</summary>
        Task<IEnumerable<User>> GetAllAsync();
        
        /// <summary>Lấy thông tin khách hàng theo ID user</summary>
        Task<CustomerProfile?> GetCustomerProfileByUserIdAsync(Guid userId);

        /// <summary>Thêm hồ sơ khách hàng mới</summary>
        Task AddCustomerProfileAsync(CustomerProfile profile);

        /// <summary>Lấy thông tin khách hàng theo Số điện thoại</summary>
        Task<CustomerProfile?> GetCustomerProfileByPhoneAsync(string phone);
    }
}
