using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using VietTien.API.DTOs.UserProfile;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: UserProfileService — L1-UP-01..05</summary>
    public class UserProfileServiceTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<ICloudinaryService> _cloudinary = new();
        private readonly UserProfileService _sut;

        public UserProfileServiceTests()
        {
            _uow.SetupGet(u => u.Users).Returns(_userRepo.Object);
            _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
            _sut = new UserProfileService(_uow.Object, _cloudinary.Object);
        }

        // L1-UP-01 | EP-Invalid | Sai mật khẩu hiện tại -> UnauthorizedAccessException, hash không đổi
        [Fact]
        public async Task L1_UP_01_ChangePassword_WrongCurrent_Rejected()
        {
            var user = TestData.User(u => u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Old!123"));
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            var oldHash = user.PasswordHash;

            var act = () => _sut.ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "WRONG",
                NewPassword = "New!1234",
                ConfirmNewPassword = "New!1234"
            });

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Mật khẩu hiện tại không chính xác.");
            user.PasswordHash.Should().Be(oldHash);
            _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
        }

        // L1-UP-02 | EP-Valid | Đúng mật khẩu hiện tại -> hash mới BCrypt, không lưu plaintext, mật khẩu cũ hết hiệu lực
        [Fact]
        public async Task L1_UP_02_ChangePassword_Valid_NewHashStored()
        {
            var user = TestData.User(u => u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Old!123"));
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

            await _sut.ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "Old!123",
                NewPassword = "New!1234",
                ConfirmNewPassword = "New!1234"
            });

            user.PasswordHash.Should().NotBe("New!1234");
            BCrypt.Net.BCrypt.Verify("New!1234", user.PasswordHash).Should().BeTrue();
            BCrypt.Net.BCrypt.Verify("Old!123", user.PasswordHash).Should().BeFalse(); // mật khẩu cũ hết hiệu lực
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        // L1-UP-03 | EP-Valid | Update profile chỉ đổi FullName + PhoneNumber; Email/Role/PasswordHash giữ nguyên
        [Fact]
        public async Task L1_UP_03_UpdateProfile_OnlyPermittedFieldsChanged()
        {
            var user = TestData.User();
            var (email, role, hash) = (user.Email, user.Role, user.PasswordHash);
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await _sut.UpdateProfileAsync(user.Id, new UpdateUserProfileDto
            {
                FullName = "Tên Mới",
                PhoneNumber = "0987654321"
            });

            user.FullName.Should().Be("Tên Mới");
            user.PhoneNumber.Should().Be("0987654321");
            user.Email.Should().Be(email);
            user.Role.Should().Be(role);
            user.PasswordHash.Should().Be(hash);
            result.FullName.Should().Be("Tên Mới");
        }

        // L1-UP-04 | EP-Valid | Upload avatar -> Cloudinary được gọi 1 lần, AvatarUrl = URL trả về
        [Fact]
        public async Task L1_UP_04_UploadAvatar_DelegatesToCloudinary()
        {
            var user = TestData.User(u => u.AvatarUrl = null);
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

            // Magic byte PNG thật (89 50 4E 47 0D 0A 1A 0A) — L3-SEC-14: UploadAvatarAsync giờ đọc
            // header nội dung file thật, không chỉ tin vào phần mở rộng tên file.
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.FileName).Returns("avatar.png");
            file.SetupGet(f => f.Length).Returns(1024);
            file.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(pngBytes));
            _cloudinary.Setup(c => c.UploadImageAsync(file.Object, "viettien/avatars"))
                       .ReturnsAsync("https://cdn/x.png");

            var result = await _sut.UploadAvatarAsync(user.Id, file.Object);

            _cloudinary.Verify(c => c.UploadImageAsync(file.Object, "viettien/avatars"), Times.Once);
            user.AvatarUrl.Should().Be("https://cdn/x.png");
            result.AvatarUrl.Should().Be("https://cdn/x.png");
        }

        // L1-UP-05 | EP-Valid | Xóa avatar -> DeleteImageAsync('pid') gọi 1 lần, AvatarUrl bị xóa
        [Fact]
        public async Task L1_UP_05_DeleteAvatar_RemovesCloudinaryAssetAndClearsUrl()
        {
            var user = TestData.User(u => u.AvatarUrl = "https://cdn/old.png");
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _cloudinary.Setup(c => c.ExtractPublicId("https://cdn/old.png")).Returns("pid");
            _cloudinary.Setup(c => c.DeleteImageAsync("pid")).ReturnsAsync(true);

            await _sut.DeleteAvatarAsync(user.Id);

            _cloudinary.Verify(c => c.DeleteImageAsync("pid"), Times.Once);
            user.AvatarUrl.Should().BeNull();
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }
    }
}
