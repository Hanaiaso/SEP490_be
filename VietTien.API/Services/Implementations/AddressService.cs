using VietTien.API.DTOs.UserProfile;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class AddressService : IAddressService
    {
        private readonly IUnitOfWork _unitOfWork;
        private const int MaxAddressesPerUser = 10;

        public AddressService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<AddressDto>> GetAddressesAsync(Guid userId)
        {
            var profile = await GetOrCreateCustomerProfileAsync(userId);
            var addresses = await _unitOfWork.Addresses.GetByCustomerProfileIdAsync(profile.Id);
            return addresses.Select(MapToDto).ToList();
        }

        public async Task<AddressDto> CreateAddressAsync(Guid userId, CreateAddressDto dto)
        {
            var profile = await GetOrCreateCustomerProfileAsync(userId);

            // Kiểm tra giới hạn 10 địa chỉ
            var count = await _unitOfWork.Addresses.CountByCustomerProfileIdAsync(profile.Id);
            if (count >= MaxAddressesPerUser)
            {
                // Lấy danh sách để gợi ý xóa
                var existingAddresses = await _unitOfWork.Addresses.GetByCustomerProfileIdAsync(profile.Id);
                var nonDefaultAddresses = existingAddresses.Where(a => !a.IsDefault).ToList();
                var suggestion = nonDefaultAddresses.Any()
                    ? $"Gợi ý xóa: \"{nonDefaultAddresses.Last().ReceiverName} - {nonDefaultAddresses.Last().SpecificAddress}\""
                    : "Vui lòng bỏ mặc định 1 địa chỉ rồi xóa.";

                throw new InvalidOperationException(
                    $"Bạn đã đạt giới hạn tối đa {MaxAddressesPerUser} địa chỉ. Vui lòng xóa 1 địa chỉ trước khi thêm mới. {suggestion}");
            }

            // Nếu đặt làm mặc định, bỏ mặc định tất cả địa chỉ cũ
            if (dto.IsDefault)
            {
                await _unitOfWork.Addresses.ResetDefaultAsync(profile.Id);
            }

            // Nếu là địa chỉ đầu tiên, tự động đặt làm mặc định
            if (count == 0)
            {
                dto.IsDefault = true;
            }

            var address = new Address
            {
                CustomerProfileId = profile.Id,
                ReceiverName = dto.Name.Trim(),
                ReceiverPhone = dto.Phone.Trim(),
                City = dto.City.Trim(),
                District = dto.District.Trim(),
                Ward = dto.Ward?.Trim() ?? string.Empty,
                SpecificAddress = dto.AddressLine.Trim(),
                ProvinceCode = dto.ProvinceCode?.Trim(),
                DistrictCode = dto.DistrictCode?.Trim(),
                WardCode = dto.WardCode?.Trim(),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                Type = ParseAddressType(dto.Type),
                IsDefault = dto.IsDefault,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Addresses.AddAsync(address);

            // Địa chỉ đầu tiên khi hoàn thiện hồ sơ: nếu tài khoản chưa có SĐT riêng,
            // đồng bộ luôn SĐT người nhận của địa chỉ này vào hồ sơ cá nhân (User.PhoneNumber),
            // tránh việc khách nhập SĐT ở đây nhưng không thấy ở tab "Thông tin cá nhân".
            if (count == 0 && !string.IsNullOrWhiteSpace(dto.Phone))
            {
                var user = await _unitOfWork.Users.GetByIdAsync(userId);
                if (user != null && string.IsNullOrWhiteSpace(user.PhoneNumber))
                {
                    user.PhoneNumber = dto.Phone.Trim();
                    _unitOfWork.Users.Update(user);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return MapToDto(address);
        }

        public async Task<AddressDto> UpdateAddressAsync(Guid userId, Guid addressId, UpdateAddressDto dto)
        {
            var profile = await GetOrCreateCustomerProfileAsync(userId);
            var address = await _unitOfWork.Addresses.GetByIdAsync(addressId)
                ?? throw new KeyNotFoundException("Không tìm thấy địa chỉ với ID này.");

            // Đảm bảo địa chỉ thuộc về user này
            if (address.CustomerProfileId != profile.Id)
                throw new KeyNotFoundException("Không tìm thấy địa chỉ với ID này.");

            // Nếu đặt làm mặc định, bỏ mặc định tất cả địa chỉ cũ
            if (dto.IsDefault && !address.IsDefault)
            {
                await _unitOfWork.Addresses.ResetDefaultAsync(profile.Id);
            }
            // Không cho bỏ mặc định của địa chỉ mặc định hiện tại mà không có địa chỉ thay thế
            // (tránh còn 0 địa chỉ default), giống guard của DeleteAddressAsync.
            else if (!dto.IsDefault && address.IsDefault)
            {
                throw new InvalidOperationException(
                    "Không thể bỏ mặc định địa chỉ này. Vui lòng đặt địa chỉ khác làm mặc định trước.");
            }

            address.ReceiverName = dto.Name.Trim();
            address.ReceiverPhone = dto.Phone.Trim();
            address.City = dto.City.Trim();
            address.District = dto.District.Trim();
            address.Ward = dto.Ward?.Trim() ?? string.Empty;
            address.SpecificAddress = dto.AddressLine.Trim();
            address.ProvinceCode = dto.ProvinceCode?.Trim();
            address.DistrictCode = dto.DistrictCode?.Trim();
            address.WardCode = dto.WardCode?.Trim();
            address.Latitude = dto.Latitude;
            address.Longitude = dto.Longitude;
            address.Type = ParseAddressType(dto.Type);
            address.IsDefault = dto.IsDefault;
            address.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Addresses.Update(address);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(address);
        }

        public async Task DeleteAddressAsync(Guid userId, Guid addressId)
        {
            var profile = await GetOrCreateCustomerProfileAsync(userId);
            var address = await _unitOfWork.Addresses.GetByIdAsync(addressId)
                ?? throw new KeyNotFoundException("Không tìm thấy địa chỉ với ID này.");

            // Đảm bảo địa chỉ thuộc về user này
            if (address.CustomerProfileId != profile.Id)
                throw new KeyNotFoundException("Không tìm thấy địa chỉ với ID này.");

            // Không cho xóa địa chỉ mặc định
            if (address.IsDefault)
                throw new InvalidOperationException(
                    "Không thể xóa địa chỉ mặc định. Vui lòng đặt địa chỉ khác làm mặc định trước.");

            _unitOfWork.Addresses.Delete(address);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task SetDefaultAddressAsync(Guid userId, Guid addressId)
        {
            var profile = await GetOrCreateCustomerProfileAsync(userId);
            var address = await _unitOfWork.Addresses.GetByIdAsync(addressId)
                ?? throw new KeyNotFoundException("Không tìm thấy địa chỉ với ID này.");

            // Đảm bảo địa chỉ thuộc về user này
            if (address.CustomerProfileId != profile.Id)
                throw new KeyNotFoundException("Không tìm thấy địa chỉ với ID này.");

            // Bỏ mặc định tất cả địa chỉ cũ
            await _unitOfWork.Addresses.ResetDefaultAsync(profile.Id);

            // Đặt mặc định cho địa chỉ mới
            address.IsDefault = true;
            address.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Addresses.Update(address);

            await _unitOfWork.SaveChangesAsync();
        }

        // ─── Private Helpers ─────────────────────────────────────────────────

        /// <summary>Lấy hoặc tự tạo CustomerProfile cho user</summary>
        private async Task<CustomerProfile> GetOrCreateCustomerProfileAsync(Guid userId)
        {
            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile != null) return profile;

            // Auto-create CustomerProfile
            profile = new CustomerProfile
            {
                UserId = userId
            };
            await _unitOfWork.Users.AddCustomerProfileAsync(profile);
            await _unitOfWork.SaveChangesAsync();

            return profile;
        }

        /// <summary>Map Address entity sang AddressDto</summary>
        private static AddressDto MapToDto(Address address)
        {
            return new AddressDto
            {
                Id = address.Id.ToString(),
                Name = address.ReceiverName,
                Phone = address.ReceiverPhone,
                City = address.City,
                District = address.District,
                Ward = address.Ward,
                AddressLine = address.SpecificAddress,
                FullAddress = BuildFullAddress(address),
                ProvinceCode = address.ProvinceCode,
                DistrictCode = address.DistrictCode,
                WardCode = address.WardCode,
                Latitude = address.Latitude,
                Longitude = address.Longitude,
                Type = MapAddressTypeToString(address.Type),
                IsDefault = address.IsDefault,
                CreatedAt = address.CreatedAt,
                UpdatedAt = address.UpdatedAt
            };
        }

        /// <summary>Ghép địa chỉ đầy đủ từ các trường</summary>
        private static string BuildFullAddress(Address address)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(address.SpecificAddress)) parts.Add(address.SpecificAddress);
            if (!string.IsNullOrWhiteSpace(address.Ward)) parts.Add(address.Ward);
            if (!string.IsNullOrWhiteSpace(address.District)) parts.Add(address.District);
            if (!string.IsNullOrWhiteSpace(address.City)) parts.Add(address.City);
            return string.Join(", ", parts);
        }

        /// <summary>Parse chuỗi tiếng Việt sang enum AddressType</summary>
        private static AddressType ParseAddressType(string type)
        {
            return type switch
            {
                "Công ty" => AddressType.CongTy,
                "Kho hàng" => AddressType.KhoHang,
                _ => AddressType.NhaRieng
            };
        }

        /// <summary>Map enum AddressType sang chuỗi tiếng Việt</summary>
        private static string MapAddressTypeToString(AddressType type)
        {
            return type switch
            {
                AddressType.CongTy => "Công ty",
                AddressType.KhoHang => "Kho hàng",
                _ => "Nhà riêng"
            };
        }
    }
}
