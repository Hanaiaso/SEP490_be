using FluentAssertions;
using Moq;
using VietTien.API.DTOs.UserProfile;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: AddressService — L1-ADDR-01..04</summary>
    public class AddressServiceTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAddressRepository> _addressRepo = new();
        private readonly AddressService _sut;

        private readonly Guid _userId = Guid.NewGuid();
        private readonly CustomerProfile _profile;

        public AddressServiceTests()
        {
            _profile = new CustomerProfile { UserId = _userId };
            _uow.SetupGet(u => u.Users).Returns(_userRepo.Object);
            _uow.SetupGet(u => u.Addresses).Returns(_addressRepo.Object);
            _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
            _userRepo.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync(_profile);

            _sut = new AddressService(_uow.Object);
        }

        private Address NewAddress(bool isDefault = false, Guid? profileId = null) => new()
        {
            CustomerProfileId = profileId ?? _profile.Id,
            ReceiverName = "Người nhận",
            ReceiverPhone = "0912345678",
            City = "Hà Nội",
            Ward = "Phường 1",
            SpecificAddress = "1 Phố X",
            IsDefault = isDefault,
        };

        private static UpdateAddressDto UpdateDto() => new()
        {
            Name = "Tên mới",
            Phone = "0999999999",
            City = "HCM",
            Ward = "Phường 2",
            AddressLine = "2 Phố Y",
            Type = "Nhà riêng",
        };

        // L1-ADDR-01 | EP-Valid | Đặt mặc định -> ResetDefaultAsync gọi 1 lần, địa chỉ chọn IsDefault=true
        [Fact]
        public async Task L1_ADDR_01_SetDefault_ResetsOthersAndSetsChosen()
        {
            var a2 = NewAddress(isDefault: false);
            _addressRepo.Setup(r => r.GetByIdAsync(a2.Id)).ReturnsAsync(a2);

            await _sut.SetDefaultAddressAsync(_userId, a2.Id);

            _addressRepo.Verify(r => r.ResetDefaultAsync(_profile.Id), Times.Once);
            a2.IsDefault.Should().BeTrue();
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        // L1-ADDR-02 | EP-Invalid | Update địa chỉ của user khác -> KeyNotFoundException, không đổi gì
        [Fact]
        public async Task L1_ADDR_02_Update_AddressOfAnotherUser_Rejected()
        {
            var a9 = NewAddress(profileId: Guid.NewGuid()); // thuộc profile khác
            _addressRepo.Setup(r => r.GetByIdAsync(a9.Id)).ReturnsAsync(a9);
            var originalName = a9.ReceiverName;

            var act = () => _sut.UpdateAddressAsync(_userId, a9.Id, UpdateDto());

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Không tìm thấy địa chỉ với ID này.");
            a9.ReceiverName.Should().Be(originalName);
            _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
        }

        // L1-ADDR-03 | EP-Valid | Xóa địa chỉ (không phải mặc định) -> Delete được gọi với đúng địa chỉ
        [Fact]
        public async Task L1_ADDR_03_Delete_RemovesAddress()
        {
            var a2 = NewAddress(isDefault: false);
            _addressRepo.Setup(r => r.GetByIdAsync(a2.Id)).ReturnsAsync(a2);

            await _sut.DeleteAddressAsync(_userId, a2.Id);

            _addressRepo.Verify(r => r.Delete(a2), Times.Once);
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        // L1-ADDR-04 | EP-Valid | Địa chỉ đầu tiên của profile -> tự động IsDefault = true
        [Fact]
        public async Task L1_ADDR_04_Create_FirstAddress_BecomesDefault()
        {
            _addressRepo.Setup(r => r.CountByCustomerProfileIdAsync(_profile.Id)).ReturnsAsync(0);
            Address? saved = null;
            _addressRepo.Setup(r => r.AddAsync(It.IsAny<Address>())).Callback<Address>(a => saved = a).Returns(Task.CompletedTask);

            var result = await _sut.CreateAddressAsync(_userId, new CreateAddressDto
            {
                Name = "Người nhận",
                Phone = "0912345678",
                City = "Hà Nội",
                Ward = "Phường 1",
                AddressLine = "1 Phố X",
                Type = "Nhà riêng",
                IsDefault = false, // dù client không yêu cầu
            });

            saved.Should().NotBeNull();
            saved!.IsDefault.Should().BeTrue();
            result.IsDefault.Should().BeTrue();
        }

        // L1-ADDR-05 | EP-Valid | Địa chỉ đầu tiên, user chưa có SĐT, SĐT người nhận CHƯA ai dùng
        // -> đồng bộ vào User.PhoneNumber như thiết kế.
        [Fact]
        public async Task L1_ADDR_05_Create_FirstAddress_SyncsPhoneToUser_WhenPhoneNotTaken()
        {
            var user = new User { Id = _userId, PhoneNumber = string.Empty };
            _addressRepo.Setup(r => r.CountByCustomerProfileIdAsync(_profile.Id)).ReturnsAsync(0);
            _addressRepo.Setup(r => r.AddAsync(It.IsAny<Address>())).Returns(Task.CompletedTask);
            _userRepo.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(user);
            _userRepo.Setup(r => r.PhoneExistsAsync("0912345678")).ReturnsAsync(false);

            await _sut.CreateAddressAsync(_userId, new CreateAddressDto
            {
                Name = "Người nhận", Phone = "0912345678", City = "Hà Nội",
                Ward = "Phường 1", AddressLine = "1 Phố X", Type = "Nhà riêng",
            });

            user.PhoneNumber.Should().Be("0912345678");
            _userRepo.Verify(r => r.Update(user), Times.Once);
        }

        // L1-ADDR-06 | BC-TRUE | Địa chỉ đầu tiên, SĐT người nhận đã thuộc VỀ TÀI KHOẢN KHÁC
        // (IX_Users_PhoneNumber duy nhất toàn hệ thống) -> KHÔNG ghi đè User.PhoneNumber, nhưng
        // địa chỉ vẫn phải lưu thành công (trước đây toàn bộ SaveChanges bị lỗi 500 theo).
        [Fact]
        public async Task L1_ADDR_06_Create_FirstAddress_SkipsPhoneSync_WhenPhoneAlreadyTakenByAnotherUser()
        {
            var user = new User { Id = _userId, PhoneNumber = string.Empty };
            _addressRepo.Setup(r => r.CountByCustomerProfileIdAsync(_profile.Id)).ReturnsAsync(0);
            Address? saved = null;
            _addressRepo.Setup(r => r.AddAsync(It.IsAny<Address>())).Callback<Address>(a => saved = a).Returns(Task.CompletedTask);
            _userRepo.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(user);
            _userRepo.Setup(r => r.PhoneExistsAsync("0901234567")).ReturnsAsync(true);

            var result = await _sut.CreateAddressAsync(_userId, new CreateAddressDto
            {
                Name = "Người nhận", Phone = "0901234567", City = "Hà Nội",
                Ward = "Phường 1", AddressLine = "1 Phố X", Type = "Nhà riêng",
            });

            saved.Should().NotBeNull();
            result.Phone.Should().Be("0901234567"); // địa chỉ vẫn lưu đúng SĐT người nhận
            user.PhoneNumber.Should().BeEmpty(); // nhưng KHÔNG đụng vào hồ sơ user
            _userRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }
    }
}
