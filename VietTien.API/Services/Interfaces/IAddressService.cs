using VietTien.API.DTOs.UserProfile;

namespace VietTien.API.Services.Interfaces
{
    public interface IAddressService
    {
        /// <summary>Lấy danh sách địa chỉ của user</summary>
        Task<List<AddressDto>> GetAddressesAsync(Guid userId);

        /// <summary>Thêm địa chỉ mới</summary>
        Task<AddressDto> CreateAddressAsync(Guid userId, CreateAddressDto dto);

        /// <summary>Cập nhật địa chỉ</summary>
        Task<AddressDto> UpdateAddressAsync(Guid userId, Guid addressId, UpdateAddressDto dto);

        /// <summary>Xóa địa chỉ</summary>
        Task DeleteAddressAsync(Guid userId, Guid addressId);

        /// <summary>Đặt địa chỉ mặc định</summary>
        Task SetDefaultAddressAsync(Guid userId, Guid addressId);
    }
}
