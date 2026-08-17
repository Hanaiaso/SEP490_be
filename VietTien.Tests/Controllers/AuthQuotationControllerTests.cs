using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.DTOs.Auth;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>
    /// Case code-driven phủ AuthController (190 dòng, trước đó 35,1%).
    /// Service trả tuple `(bool Success, string Message[, AuthResponseDto Data])` nên mỗi action
    /// có đúng 3 nhánh: ModelState hỏng → 400 · Success=false → 400/401 · Success=true → 200.
    /// </summary>
    public class AuthControllerTests
    {
        private readonly Mock<IAuthService> _service = new();
        private readonly AuthController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public AuthControllerTests() => _sut = new AuthController(_service.Object).WithUser(_userId, "Customer");

        // ── đăng ký / OTP email ──────────────────────────────────────────────

        [Fact]
        public async Task Register_WhenModelStateInvalid_Returns400WithoutCallingService()
        {
            _sut.WithInvalidModelState("Email", "sai dinh dang");

            (await _sut.Register(new RegisterDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.RegisterAsync(It.IsAny<RegisterDto>()), Times.Never);
        }

        [Fact]
        public async Task Register_WhenEmailAlreadyUsed_Returns400()
        {
            _service.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
                .ReturnsAsync((false, "Email đã được sử dụng"));

            (await _sut.Register(new RegisterDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Register_Success_ReturnsOk()
        {
            _service.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
                .ReturnsAsync((true, "Đã gửi OTP"));

            (await _sut.Register(new RegisterDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task VerifyOtp_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("OtpCode", "bat buoc");

            (await _sut.VerifyOtp(new VerifyOtpDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task VerifyOtp_WhenCodeWrongOrExpired_Returns400()
        {
            _service.Setup(s => s.VerifyOtpAsync(It.IsAny<VerifyOtpDto>()))
                .ReturnsAsync((false, "Mã OTP không đúng hoặc đã hết hạn"));

            (await _sut.VerifyOtp(new VerifyOtpDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task VerifyOtp_Success_ReturnsOk()
        {
            _service.Setup(s => s.VerifyOtpAsync(It.IsAny<VerifyOtpDto>())).ReturnsAsync((true, "Kích hoạt thành công"));

            (await _sut.VerifyOtp(new VerifyOtpDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ResendOtp_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Email", "bat buoc");

            (await _sut.ResendOtp(new ResendOtpDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ResendOtp_ForwardsEmailToService()
        {
            _service.Setup(s => s.ResendEmailOtpAsync("a@vt.vn")).ReturnsAsync((true, "Đã gửi lại"));

            (await _sut.ResendOtp(new ResendOtpDto { Email = "a@vt.vn" })).StatusOf().Should().Be(200);
            _service.Verify(s => s.ResendEmailOtpAsync("a@vt.vn"), Times.Once);
        }

        [Fact]
        public async Task ResendOtp_WhenRateLimited_Returns400()
        {
            _service.Setup(s => s.ResendEmailOtpAsync(It.IsAny<string>()))
                .ReturnsAsync((false, "Vui lòng đợi trước khi gửi lại"));

            (await _sut.ResendOtp(new ResendOtpDto())).StatusOf().Should().Be(400);
        }

        // ── đăng nhập ────────────────────────────────────────────────────────

        [Fact]
        public async Task Login_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Password", "bat buoc");

            (await _sut.Login(new LoginDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.LoginAsync(It.IsAny<LoginDto>()), Times.Never);
        }

        [Fact]
        public async Task Login_WhenCredentialsWrong_Returns401NotBadRequest()
        {
            _service.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                .ReturnsAsync((false, "Email hoặc mật khẩu không đúng", (AuthResponseDto?)null));

            (await _sut.Login(new LoginDto())).StatusOf().Should().Be(401,
                "sai thông tin đăng nhập phải là 401 để FE phân biệt với lỗi validate");
        }

        [Fact]
        public async Task Login_Success_ReturnsOkWithTokenPayload()
        {
            _service.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                .ReturnsAsync((true, "OK", new AuthResponseDto { AccessToken = "jwt-token" }));

            var result = await _sut.Login(new LoginDto());

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value!.ToString().Should().Contain("AuthResponseDto");
        }

        [Fact]
        public async Task GoogleLogin_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("IdToken", "bat buoc");

            (await _sut.GoogleLogin(new GoogleLoginDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GoogleLogin_WhenIdTokenInvalid_Returns401()
        {
            _service.Setup(s => s.LoginWithGoogleAsync(It.IsAny<GoogleLoginDto>()))
                .ReturnsAsync((false, "ID Token không hợp lệ", (AuthResponseDto?)null));

            (await _sut.GoogleLogin(new GoogleLoginDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GoogleLogin_Success_ReturnsOk()
        {
            _service.Setup(s => s.LoginWithGoogleAsync(It.IsAny<GoogleLoginDto>()))
                .ReturnsAsync((true, "OK", new AuthResponseDto()));

            (await _sut.GoogleLogin(new GoogleLoginDto())).StatusOf().Should().Be(200);
        }

        // ── quên / đặt lại mật khẩu ─────────────────────────────────────────

        [Fact]
        public async Task ForgotPassword_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Email", "sai dinh dang");

            (await _sut.ForgotPassword(new ForgotPasswordDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ForgotPassword_WhenEmailNotRegistered_StillReturns200()
        {
            _service.Setup(s => s.ForgotPasswordAsync(It.IsAny<ForgotPasswordDto>()))
                .ReturnsAsync((false, "Không tìm thấy tài khoản"));

            (await _sut.ForgotPassword(new ForgotPasswordDto())).StatusOf().Should().Be(200,
                "cố ý luôn trả 200 để không lộ email nào đã đăng ký (chống user enumeration)");
        }

        [Fact]
        public async Task ForgotPassword_WhenEmailRegistered_Returns200()
        {
            _service.Setup(s => s.ForgotPasswordAsync(It.IsAny<ForgotPasswordDto>()))
                .ReturnsAsync((true, "Đã gửi email"));

            (await _sut.ForgotPassword(new ForgotPasswordDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ResetPassword_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("NewPassword", "qua ngan");

            (await _sut.ResetPassword(new ResetPasswordDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ResetPassword_WhenTokenExpired_Returns400()
        {
            _service.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
                .ReturnsAsync((false, "Token đã hết hạn"));

            (await _sut.ResetPassword(new ResetPasswordDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ResetPassword_Success_ReturnsOk()
        {
            _service.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
                .ReturnsAsync((true, "Đổi mật khẩu thành công"));

            (await _sut.ResetPassword(new ResetPasswordDto())).StatusOf().Should().Be(200);
        }

        // ── refresh / logout ─────────────────────────────────────────────────

        [Fact]
        public async Task RefreshToken_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("RefreshToken", "bat buoc");

            (await _sut.RefreshToken(new RefreshTokenDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RefreshToken_WhenTokenRevoked_Returns401()
        {
            _service.Setup(s => s.RefreshTokenAsync(It.IsAny<RefreshTokenDto>()))
                .ReturnsAsync((false, "Refresh Token không hợp lệ", (AuthResponseDto?)null));

            (await _sut.RefreshToken(new RefreshTokenDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task RefreshToken_Success_ReturnsOk()
        {
            _service.Setup(s => s.RefreshTokenAsync(It.IsAny<RefreshTokenDto>()))
                .ReturnsAsync((true, "OK", new AuthResponseDto()));

            (await _sut.RefreshToken(new RefreshTokenDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Logout_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.Logout()).StatusOf().Should().Be(401);
            _service.Verify(s => s.LogoutAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Logout_Success_RevokesTokenOfCaller()
        {
            _service.Setup(s => s.LogoutAsync(_userId)).ReturnsAsync((true, "Đã đăng xuất"));

            (await _sut.Logout()).StatusOf().Should().Be(200);
            _service.Verify(s => s.LogoutAsync(_userId), Times.Once);
        }

        [Fact]
        public async Task Logout_WhenServiceReportsFailure_Returns400()
        {
            _service.Setup(s => s.LogoutAsync(It.IsAny<Guid>())).ReturnsAsync((false, "Không tìm thấy phiên"));

            (await _sut.Logout()).StatusOf().Should().Be(400);
        }

        // ── hoàn thiện hồ sơ + OTP SMS ───────────────────────────────────────

        [Fact]
        public async Task CompleteProfile_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("PhoneNumber", "sai dinh dang");

            (await _sut.CompleteProfile(new CompleteProfileDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CompleteProfile_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.CompleteProfile(new CompleteProfileDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task CompleteProfile_Success_ReturnsOk()
        {
            _service.Setup(s => s.CompleteProfileAsync(_userId, It.IsAny<CompleteProfileDto>()))
                .ReturnsAsync((true, "OK"));

            (await _sut.CompleteProfile(new CompleteProfileDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CompleteProfile_WhenPhoneAlreadyUsed_Returns400()
        {
            _service.Setup(s => s.CompleteProfileAsync(It.IsAny<Guid>(), It.IsAny<CompleteProfileDto>()))
                .ReturnsAsync((false, "Số điện thoại đã được dùng"));

            (await _sut.CompleteProfile(new CompleteProfileDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestPhoneOtp_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("PhoneNumber", "bat buoc");

            (await _sut.RequestPhoneOtp(new RequestPhoneOtpDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestPhoneOtp_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.RequestPhoneOtp(new RequestPhoneOtpDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task RequestPhoneOtp_Success_ForwardsPhoneNumber()
        {
            _service.Setup(s => s.RequestPhoneVerificationAsync(_userId, "0900000000")).ReturnsAsync((true, "Đã gửi"));

            (await _sut.RequestPhoneOtp(new RequestPhoneOtpDto { PhoneNumber = "0900000000" }))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task RequestPhoneOtp_WhenRateLimited_Returns400()
        {
            _service.Setup(s => s.RequestPhoneVerificationAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync((false, "Gửi quá nhiều lần"));

            (await _sut.RequestPhoneOtp(new RequestPhoneOtpDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task VerifyPhoneOtp_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("OtpCode", "bat buoc");

            (await _sut.VerifyPhoneOtp(new VerifyPhoneOtpDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task VerifyPhoneOtp_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.VerifyPhoneOtp(new VerifyPhoneOtpDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task VerifyPhoneOtp_Success_ReturnsOk()
        {
            _service.Setup(s => s.VerifyPhoneOtpAsync(_userId, "123456", "0900000000")).ReturnsAsync((true, "OK"));

            (await _sut.VerifyPhoneOtp(new VerifyPhoneOtpDto { OtpCode = "123456", PhoneNumber = "0900000000" }))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task VerifyPhoneOtp_WhenCodeWrong_Returns400()
        {
            _service.Setup(s => s.VerifyPhoneOtpAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((false, "Mã OTP không đúng"));

            (await _sut.VerifyPhoneOtp(new VerifyPhoneOtpDto())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ QuotationController (283 dòng, trước đó 27,2%).</summary>
    public class QuotationControllerTests
    {
        private readonly Mock<IQuotationService> _service = new();
        private readonly Guid _userId = Guid.NewGuid();

        private QuotationController Build(string role = "Customer")
            => new QuotationController(_service.Object).WithUser(_userId, role);

        // ── tạo báo giá từ giỏ ───────────────────────────────────────────────

        [Fact]
        public async Task CreateQuotationFromCart_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateQuotationFromCartAsync(_userId, It.IsAny<CreateQuotationRequest>()))
                .ReturnsAsync(new QuotationDto());

            (await Build().CreateQuotationFromCart(new CreateQuotationRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreateQuotationFromCart_WhenCartEmpty_Returns404()
        {
            _service.Setup(s => s.CreateQuotationFromCartAsync(It.IsAny<Guid>(), It.IsAny<CreateQuotationRequest>()))
                .ThrowsAsync(new KeyNotFoundException("Gio hang trong"));

            (await Build().CreateQuotationFromCart(new CreateQuotationRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateQuotationFromCart_WhenBelowThreshold_Returns400()
        {
            _service.Setup(s => s.CreateQuotationFromCartAsync(It.IsAny<Guid>(), It.IsAny<CreateQuotationRequest>()))
                .ThrowsAsync(new InvalidOperationException("Gia tri chua dat nguong bao gia"));

            (await Build().CreateQuotationFromCart(new CreateQuotationRequest())).StatusOf().Should().Be(400);
        }

        // ── danh sách theo vai trò ───────────────────────────────────────────

        [Fact]
        public async Task GetQuotations_AsCustomer_ReturnsOnlyOwnQuotations()
        {
            _service.Setup(s => s.GetCustomerQuotationsAsync(_userId)).ReturnsAsync(new List<QuotationDto>());

            (await Build("Customer").GetQuotations()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetCustomerQuotationsAsync(_userId), Times.Once);
            _service.Verify(s => s.GetAllQuotationsAsync(), Times.Never, "khách không được xem toàn bộ báo giá");
        }

        [Fact]
        public async Task GetQuotations_AsSalesStaff_ReturnsOwnPlusPendingPool()
        {
            _service.Setup(s => s.GetSalesQuotationsAsync(_userId)).ReturnsAsync(new List<QuotationDto>());
            _service.Setup(s => s.GetAllPendingQuotationsAsync()).ReturnsAsync(new List<QuotationDto>());

            (await Build("SalesStaff").GetQuotations()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesQuotationsAsync(_userId), Times.Once);
            _service.Verify(s => s.GetAllPendingQuotationsAsync(), Times.Once,
                "Sale thấy cả báo giá của mình lẫn pool chưa ai nhận");
        }

        [Fact]
        public async Task GetQuotations_AsSalesManager_ReturnsAll()
        {
            _service.Setup(s => s.GetAllQuotationsAsync()).ReturnsAsync(new List<QuotationDto>());

            (await Build("SalesManager").GetQuotations()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetAllQuotationsAsync(), Times.Once);
        }

        [Theory]
        [InlineData("CEO")]
        [InlineData("Admin")]
        public async Task GetQuotations_AsCeoOrAdmin_ReturnsAll(string role)
        {
            _service.Setup(s => s.GetAllQuotationsAsync()).ReturnsAsync(new List<QuotationDto>());

            (await Build(role).GetQuotations()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetQuotations_AsUnrelatedRole_Returns403()
        {
            (await Build("WarehouseStaff").GetQuotations()).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GetQuotations_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetCustomerQuotationsAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await Build("Customer").GetQuotations()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetQuotationById_Success_PassesRoleForScoping()
        {
            _service.Setup(s => s.GetQuotationByIdAsync(It.IsAny<Guid>(), _userId, "Customer"))
                .ReturnsAsync(new QuotationDto());

            (await Build().GetQuotationById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetQuotationById_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetQuotationByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().GetQuotationById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetQuotationById_WhenNotOwner_Returns403()
        {
            _service.Setup(s => s.GetQuotationByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().GetQuotationById(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GetQuotationById_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetQuotationByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetQuotationById(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── Sale nhận báo giá ────────────────────────────────────────────────

        [Fact]
        public async Task PickUpQuotation_Success_ReturnsOk()
        {
            _service.Setup(s => s.PickUpQuotationAsync(It.IsAny<Guid>(), _userId)).ReturnsAsync(new QuotationDto());

            (await Build("SalesStaff").PickUpQuotation(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task PickUpQuotation_WhenMissing_Returns404()
        {
            _service.Setup(s => s.PickUpQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesStaff").PickUpQuotation(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task PickUpQuotation_WhenAnotherSaleTookItFirst_Returns409()
        {
            _service.Setup(s => s.PickUpQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Bao gia da co nguoi nhan"));

            (await Build("SalesStaff").PickUpQuotation(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task PickUpQuotation_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.PickUpQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build("SalesStaff").PickUpQuotation(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task PickUpQuotation_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.PickUpQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesStaff").PickUpQuotation(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── Sales Manager phân công thủ công ────────────────────────────────

        [Fact]
        public async Task AssignQuotation_Success_ReturnsOk()
        {
            _service.Setup(s => s.AssignQuotationAsync(It.IsAny<Guid>(), _userId, It.IsAny<AssignQuotationRequest>()))
                .ReturnsAsync(new QuotationDto());

            (await Build("SalesManager").AssignQuotation(Guid.NewGuid(), new AssignQuotationRequest { StaffId = Guid.NewGuid() }))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task AssignQuotation_WhenMissing_Returns404()
        {
            _service.Setup(s => s.AssignQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AssignQuotationRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesManager").AssignQuotation(Guid.NewGuid(), new AssignQuotationRequest { StaffId = Guid.NewGuid() }))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task AssignQuotation_WhenAlreadyAssigned_Returns409()
        {
            _service.Setup(s => s.AssignQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AssignQuotationRequest>()))
                .ThrowsAsync(new InvalidOperationException("Da phan cong cho nguoi khac"));

            (await Build("SalesManager").AssignQuotation(Guid.NewGuid(), new AssignQuotationRequest { StaffId = Guid.NewGuid() }))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task AssignQuotation_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.AssignQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AssignQuotationRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build("SalesManager").AssignQuotation(Guid.NewGuid(), new AssignQuotationRequest { StaffId = Guid.NewGuid() }))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task AssignQuotation_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.AssignQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AssignQuotationRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").AssignQuotation(Guid.NewGuid(), new AssignQuotationRequest { StaffId = Guid.NewGuid() }))
                .StatusOf().Should().Be(400);
        }

        // ── phiên bản báo giá ────────────────────────────────────────────────

        [Fact]
        public async Task CreateVersion_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateVersionAsync(It.IsAny<Guid>(), _userId, It.IsAny<CreateQuotationVersionRequest>()))
                .ReturnsAsync(new QuotationVersionDto());

            (await Build("SalesStaff").CreateVersion(Guid.NewGuid(), new CreateQuotationVersionRequest()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreateVersion_WhenMissing_Returns404()
        {
            _service.Setup(s => s.CreateVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateQuotationVersionRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesStaff").CreateVersion(Guid.NewGuid(), new CreateQuotationVersionRequest()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateVersion_WhenNotTheAssignedSale_Returns403()
        {
            _service.Setup(s => s.CreateVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateQuotationVersionRequest>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build("SalesStaff").CreateVersion(Guid.NewGuid(), new CreateQuotationVersionRequest()))
                .StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task CreateVersion_WhenPreviousVersionStillPending_Returns409()
        {
            _service.Setup(s => s.CreateVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateQuotationVersionRequest>()))
                .ThrowsAsync(new InvalidOperationException("Ban truoc dang cho duyet"));

            (await Build("SalesStaff").CreateVersion(Guid.NewGuid(), new CreateQuotationVersionRequest()))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateVersion_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CreateVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateQuotationVersionRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build("SalesStaff").CreateVersion(Guid.NewGuid(), new CreateQuotationVersionRequest()))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateVersion_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateQuotationVersionRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesStaff").CreateVersion(Guid.NewGuid(), new CreateQuotationVersionRequest()))
                .StatusOf().Should().Be(400);
        }

        // ── duyệt: Manager / CEO / khách ────────────────────────────────────

        [Fact]
        public async Task ManagerDecision_Success_ReturnsOk()
        {
            _service.Setup(s => s.ManagerReviewVersionAsync(It.IsAny<Guid>(), _userId, It.IsAny<ManagerReviewRequest>()))
                .ReturnsAsync(new QuotationVersionDto());

            (await Build("SalesManager").ManagerDecision(Guid.NewGuid(), new ManagerReviewRequest()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ManagerDecision_WhenMissing_Returns404()
        {
            _service.Setup(s => s.ManagerReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ManagerReviewRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesManager").ManagerDecision(Guid.NewGuid(), new ManagerReviewRequest()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ManagerDecision_WhenAlreadyDecided_Returns409()
        {
            _service.Setup(s => s.ManagerReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ManagerReviewRequest>()))
                .ThrowsAsync(new InvalidOperationException("Da duyet truoc do"));

            (await Build("SalesManager").ManagerDecision(Guid.NewGuid(), new ManagerReviewRequest()))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ManagerDecision_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.ManagerReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ManagerReviewRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build("SalesManager").ManagerDecision(Guid.NewGuid(), new ManagerReviewRequest()))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ManagerDecision_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ManagerReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ManagerReviewRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").ManagerDecision(Guid.NewGuid(), new ManagerReviewRequest()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CeoDecision_Success_ReturnsOk()
        {
            _service.Setup(s => s.CeoReviewVersionAsync(It.IsAny<Guid>(), _userId, It.IsAny<CeoReviewRequest>()))
                .ReturnsAsync(new QuotationVersionDto());

            (await Build("CEO").CeoDecision(Guid.NewGuid(), new CeoReviewRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CeoDecision_WhenMissing_Returns404()
        {
            _service.Setup(s => s.CeoReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CeoReviewRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("CEO").CeoDecision(Guid.NewGuid(), new CeoReviewRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CeoDecision_WhenManagerHasNotApprovedYet_Returns409()
        {
            _service.Setup(s => s.CeoReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CeoReviewRequest>()))
                .ThrowsAsync(new InvalidOperationException("Chua qua buoc Manager"));

            (await Build("CEO").CeoDecision(Guid.NewGuid(), new CeoReviewRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CeoDecision_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CeoReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CeoReviewRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build("CEO").CeoDecision(Guid.NewGuid(), new CeoReviewRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CeoDecision_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CeoReviewVersionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CeoReviewRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("CEO").CeoDecision(Guid.NewGuid(), new CeoReviewRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CustomerDecision_Success_ReturnsOk()
        {
            _service.Setup(s => s.CustomerDecisionAsync(It.IsAny<Guid>(), _userId, It.IsAny<CustomerDecisionRequest>()))
                .ReturnsAsync(new QuotationVersionDto());

            (await Build().CustomerDecision(Guid.NewGuid(), new CustomerDecisionRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CustomerDecision_WhenNotOwner_Returns403()
        {
            _service.Setup(s => s.CustomerDecisionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CustomerDecisionRequest>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().CustomerDecision(Guid.NewGuid(), new CustomerDecisionRequest())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task CustomerDecision_WhenQuotationExpired_Returns409()
        {
            _service.Setup(s => s.CustomerDecisionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CustomerDecisionRequest>()))
                .ThrowsAsync(new InvalidOperationException("Bao gia da het han"));

            (await Build().CustomerDecision(Guid.NewGuid(), new CustomerDecisionRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CustomerDecision_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CustomerDecisionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CustomerDecisionRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build().CustomerDecision(Guid.NewGuid(), new CustomerDecisionRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CustomerDecision_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CustomerDecisionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CustomerDecisionRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().CustomerDecision(Guid.NewGuid(), new CustomerDecisionRequest())).StatusOf().Should().Be(400);
        }

        // ── huỷ báo giá ──────────────────────────────────────────────────────

        [Fact]
        public async Task CancelQuotation_Success_ReturnsOk()
        {
            _service.Setup(s => s.CancelQuotationAsync(It.IsAny<Guid>(), _userId)).ReturnsAsync(new QuotationDto());

            (await Build().CancelQuotation(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CancelQuotation_WhenNotOwner_Returns403()
        {
            _service.Setup(s => s.CancelQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().CancelQuotation(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task CancelQuotation_WhenAlreadyAccepted_Returns409()
        {
            _service.Setup(s => s.CancelQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Bao gia da duoc chap nhan"));

            (await Build().CancelQuotation(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CancelQuotation_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CancelQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await Build().CancelQuotation(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CancelQuotation_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CancelQuotationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().CancelQuotation(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── chat trong báo giá ───────────────────────────────────────────────

        [Fact]
        public async Task GetMessages_Success_PassesRoleForScoping()
        {
            _service.Setup(s => s.GetMessagesAsync(It.IsAny<Guid>(), _userId, "Customer"))
                .ReturnsAsync(new List<ChatMessageDto>());

            (await Build().GetMessages(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetMessages_WhenNotParticipant_Returns403()
        {
            _service.Setup(s => s.GetMessagesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().GetMessages(Guid.NewGuid())).StatusOf().Should().Be(403,
                "không được đọc hội thoại của báo giá mình không tham gia");
        }

        [Fact]
        public async Task GetMessages_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetMessagesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetMessages(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SendMessage_Success_ReturnsOk()
        {
            _service.Setup(s => s.SendMessageAsync(It.IsAny<Guid>(), _userId, It.IsAny<SendChatMessageRequest>()))
                .ReturnsAsync(new ChatMessageDto());

            (await Build().SendMessage(Guid.NewGuid(), new SendChatMessageRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SendMessage_WhenNotParticipant_Returns403()
        {
            _service.Setup(s => s.SendMessageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SendChatMessageRequest>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().SendMessage(Guid.NewGuid(), new SendChatMessageRequest())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task SendMessage_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.SendMessageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SendChatMessageRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().SendMessage(Guid.NewGuid(), new SendChatMessageRequest())).StatusOf().Should().Be(400);
        }
    }
}
