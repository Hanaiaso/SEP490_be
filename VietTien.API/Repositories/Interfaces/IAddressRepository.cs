using VietTien.API.Models;

namespace VietTien.API.Repositories.Interfaces
{
    public interface IAddressRepository
    {
        /// <summary>Lấy tất cả địa chỉ theo CustomerProfileId, mặc định đứng đầu</summary>
        Task<List<Address>> GetByCustomerProfileIdAsync(Guid customerProfileId);

        /// <summary>Lấy địa chỉ theo ID</summary>
        Task<Address?> GetByIdAsync(Guid id);

        /// <summary>Thêm địa chỉ mới</summary>
        Task AddAsync(Address address);

        /// <summary>Cập nhật địa chỉ</summary>
        void Update(Address address);

        /// <summary>Xóa địa chỉ</summary>
        void Delete(Address address);

        /// <summary>Đếm số địa chỉ của customer</summary>
        Task<int> CountByCustomerProfileIdAsync(Guid customerProfileId);

        /// <summary>Bỏ mặc định tất cả địa chỉ của customer</summary>
        Task ResetDefaultAsync(Guid customerProfileId);
    }
}
