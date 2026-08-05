using System.ComponentModel.DataAnnotations;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Infrastructure.Validation;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Integrations
{
    /// <summary>
    /// Sheet: ExternalIntegrations — L1-EXT-01..08. Test HỢP ĐỒNG gọi ra ngoài bằng
    /// FakeHttpMessageHandler / mock, tuyệt đối không phát sinh network thật.
    ///
    /// ⚠ Khác doc về signature:
    ///   • ISmsService.SendSmsAsync(phone, message) trả (bool Success, string ErrorMessage)
    ///     — doc ghi "eSmsService.SendAsync(...)".
    ///   • ICloudinaryService.UploadImageAsync(IFormFile file, string folder) trả string URL
    ///     (không trả publicId); publicId lấy lại bằng ExtractPublicId(url).
    ///   • IAiGeneratorService chỉ có GenerateMarketingOptionsAsync(request), không phải GenerateAsync(prompt).
    ///   Xem DOC_MISMATCHES.md.
    /// </summary>
    public class ExternalIntegrationsTests
    {
        // ── Block: EmailService / eSmsService ───────────────────────────────

        // L1-EXT-01 | EP-Valid | OTP KHÔNG được rò rỉ ra thông điệp lỗi / log khi gửi mail thất bại.
        //
        // ⚠ GIỚI HẠN đã biết (rà soát 01/08/2026): doc yêu cầu assert cả "đúng template tiếng Việt",
        //   nhưng EmailService tự dựng MimeMessage và gửi bằng SmtpClient KHỞI TẠO BÊN TRONG method —
        //   không có seam nào để chặn và đọc nội dung mail ở tầng L1. Muốn assert template thì phải
        //   refactor production code (tách ITemplateRenderer hoặc inject ISmtpClient).
        //   Ở đây giữ phần KHẲNG ĐỊNH ĐƯỢC và có giá trị bảo mật thật: OTP không lọt ra ngoài.
        //   Phần template chuyển sang L2/L4. Xem DOC_MISMATCHES.md.
        [Fact]
        public async Task L1_EXT_01_SendOtpEmail_NeverLeaksOtp()
        {
            // SMTP host không tồn tại -> MailKit ném lỗi, nhưng điều cần khẳng định là
            // OTP không bị rò rỉ ra ngoài qua thông điệp lỗi.
            var settings = Options.Create(new EmailSettings
            {
                SmtpHost = "smtp.invalid.local",
                SmtpPort = 2525,
                SenderEmail = "no-reply@viettien.com",
                SenderName = "VietTien",
                SenderPassword = "x"
            });
            var sut = new EmailService(settings);

            var act = () => sut.SendOtpEmailAsync("khach@test.com", "Nguyễn Văn A", "123456");

            var ex = await act.Should().ThrowAsync<Exception>();
            ex.Which.Message.Should().NotContain("123456", "mã OTP không được xuất hiện trong lỗi/log");
        }

        // L1-EXT-02 | EP-Invalid | SMTP lỗi -> lỗi phải nổi lên để tầng gọi xử lý (NFR-A02),
        // không được nuốt im lặng và coi như đã gửi.
        [Fact]
        public async Task L1_EXT_02_SmtpFailure_IsSurfacedToCaller()
        {
            var settings = Options.Create(new EmailSettings
            {
                SmtpHost = "smtp.invalid.local",
                SmtpPort = 2525,
                SenderEmail = "no-reply@viettien.com",
                SenderName = "VietTien",
                SenderPassword = "x"
            });
            var sut = new EmailService(settings);
            var order = TestData.Order(Guid.NewGuid());

            var act = () => sut.SendOrderInvoiceEmailAsync("khach@test.com", "Nguyễn Văn A", order);

            await act.Should().ThrowAsync<Exception>("tầng gọi phải biết email chưa gửi được để bù trừ");
        }

        // L1-EXT-03 | EP-Valid | Gửi SMS phát đúng 1 request tới endpoint eSMS với tham số cấu hình
        [Fact]
        public async Task L1_EXT_03_SendSms_IssuesExactlyOneRequestToEsms()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, "{\"CodeResult\":\"100\"}");
            var config = TestConfig.Create(new Dictionary<string, string?>
            {
                ["eSMS:ApiKey"] = "test-api-key",
                ["eSMS:SecretKey"] = "test-secret",
                ["eSMS:Brandname"] = "VietTien",
            });
            var sut = new eSmsService(handler.CreateClient(), config);

            var (success, error) = await sut.SendSmsAsync("0912345678", "Ma xac thuc cua ban la 123456");

            success.Should().BeTrue();
            error.Should().BeEmpty();
            handler.CallCount.Should().Be(1);
            handler.Requests[0].RequestUri!.Host.Should().Be("rest.esms.vn");
            handler.RequestBodies[0].Should().Contain("test-api-key").And.Contain("0912345678");
        }

        // L1-EXT-04 | EP-Invalid | eSMS trả 500 -> báo thất bại rõ ràng, KHÔNG coi là đã gửi
        [Fact]
        public async Task L1_EXT_04_EsmsError_ReportsFailure()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.InternalServerError, "{\"CodeResult\":\"99\"}");
            var config = TestConfig.Create(new Dictionary<string, string?>
            {
                ["eSMS:ApiKey"] = "test-api-key",
                ["eSMS:SecretKey"] = "test-secret",
            });
            var sut = new eSmsService(handler.CreateClient(), config);

            var (success, error) = await sut.SendSmsAsync("0912345678", "Ma xac thuc cua ban la 123456");

            success.Should().BeFalse("tầng gọi không được đánh dấu đã gửi OTP");
            error.Should().NotBeNullOrWhiteSpace();
        }

        // ── Block: CloudinaryService / MakeWebhookService / AiGeneratorService ──

        // L1-EXT-05 | EP-Valid | URL ảnh trả về phải trích xuất lại được publicId để dùng khi xoá
        [Theory]
        [InlineData("https://res.cloudinary.com/demo/image/upload/v1712345678/GoodsIssues/proof-abc.png", "GoodsIssues/proof-abc")]
        [InlineData("https://res.cloudinary.com/demo/image/upload/Products/sku-123.jpg", "Products/sku-123")]
        public void L1_EXT_05_ExtractPublicId_RoundTripsUploadUrl(string url, string expectedPublicId)
        {
            var sut = new CloudinaryService(Options.Create(new CloudinarySettings
            {
                CloudName = "demo",
                ApiKey = "key",
                ApiSecret = "secret"
            }));

            sut.ExtractPublicId(url).Should().Be(expectedPublicId);
        }

        // L1-EXT-06 | EP-Invalid | File ngoài allowlist hoặc vượt kích thước bị chặn TRƯỚC khi gọi ra ngoài
        // Lưu ý: guard nằm ở ImageFileAttribute (tầng validation DTO), KHÔNG nằm trong CloudinaryService.
        [Theory]
        [InlineData("virus.exe", "application/x-msdownload", 1024)]        // ngoài allowlist
        [InlineData("huge.png", "image/png", 6 * 1024 * 1024)]             // vượt 5MB
        [InlineData("empty.png", "image/png", 0)]                          // file rỗng
        public void L1_EXT_06_DisallowedFile_IsRejectedBeforeAnyOutboundCall(string fileName, string contentType, long length)
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.FileName).Returns(fileName);
            file.SetupGet(f => f.ContentType).Returns(contentType);
            file.SetupGet(f => f.Length).Returns(length);

            var attribute = new ImageFileAttribute();
            var result = attribute.GetValidationResult(file.Object, new ValidationContext(new object()));

            result.Should().NotBe(ValidationResult.Success);
            result!.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        }

        // L1-EXT-07 | EP-Valid | MakeWebhook gửi đúng 1 request tới URL cấu hình với payload khớp bài đăng
        [Fact]
        public async Task L1_EXT_07_MakeWebhook_SendsExpectedPayloadOnce()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
            var config = TestConfig.Create(new Dictionary<string, string?>
            {
                ["MakeCom:WebhookUrl"] = "https://hook.make.test/viettien-marketing",
            });
            var sut = new MakeWebhookService(handler.CreateClient(), config, NullLogger<MakeWebhookService>.Instance);

            var post = TestData.MarketingPost(Guid.NewGuid(), Guid.NewGuid(), p => p.EditedCaption = "Ống PVC bền đẹp");
            var triggered = await sut.TriggerPostToMakeAsync(post);

            triggered.Should().BeTrue();
            handler.CallCount.Should().Be(1);
            handler.Requests[0].RequestUri!.ToString().Should().Be("https://hook.make.test/viettien-marketing");

            // JsonSerializer escape ký tự ngoài ASCII (Ống…) nên phải parse lại thay vì so khớp chuỗi thô.
            using var body = System.Text.Json.JsonDocument.Parse(handler.RequestBodies[0]);
            body.RootElement.GetProperty("postId").GetGuid().Should().Be(post.Id);
            body.RootElement.GetProperty("caption").GetString().Should().Be("Ống PVC bền đẹp");
            body.RootElement.GetProperty("imageUrl").GetString().Should().Be(post.SelectedImageUrl);
        }

        // L1-EXT-07 (nhánh lỗi) | EP-Invalid | Make.com không phản hồi -> trả false có kiểm soát, không ném ra ngoài
        [Fact]
        public async Task L1_EXT_07_MakeWebhook_TimeoutReturnsFalseWithoutThrowing()
        {
            var handler = FakeHttpMessageHandler.Throwing(new TaskCanceledException("The request timed out."));
            var sut = new MakeWebhookService(handler.CreateClient(), TestConfig.Create(), NullLogger<MakeWebhookService>.Instance);

            var triggered = await sut.TriggerPostToMakeAsync(TestData.MarketingPost(Guid.NewGuid(), Guid.NewGuid()));

            triggered.Should().BeFalse("bài phải được đánh dấu thất bại thay vì treo ở Posting");
        }

        // L1-EXT-08 | EP-Invalid | Gemini quá thời gian -> SUY GIẢM CÓ KIỂM SOÁT sang bộ sinh dự phòng,
        // KHÔNG ném lỗi ra ngoài và KHÔNG thử lại vô hạn.
        //
        // ⚠ Bản trước của case này assert `Task.WhenAny(...).Should().NotBe(default)` — luôn đúng bất kể
        //   code làm gì (Task.WhenAny không bao giờ trả default), tức test xanh vĩnh viễn và vô giá trị.
        //   Viết lại 01/08/2026 để bám đúng hành vi thật: AiGeneratorService bắt exception của Gemini,
        //   ghi log warning rồi rơi về GenerateFallbackOptions.
        [Fact]
        public async Task L1_EXT_08_AiGenerator_Timeout_FallsBackWithoutThrowing()
        {
            var db = TestDbFactory.Create();
            var product = TestData.SeedProduct(db, p => p.Name = "Ống PVC D21");
            var handler = FakeHttpMessageHandler.Throwing(new TaskCanceledException("The request timed out."));
            // Phải có API key thì service mới đi vào nhánh gọi Gemini (không có key sẽ bỏ qua luôn).
            var config = TestConfig.Create(new Dictionary<string, string?>
            {
                ["GeminiSettings:ApiKey"] = "test-gemini-key",
            });
            var sut = new AiGeneratorService(db, handler.CreateClient(), config, NullLogger<AiGeneratorService>.Instance);

            var result = await sut.GenerateMarketingOptionsAsync(new API.DTOs.Marketing.GenerateAiContentRequestDto
            {
                ProductId = product.Id
            });

            result.Options.Should().NotBeEmpty("timeout của Gemini phải rơi về bộ sinh nội dung dự phòng");
            result.ProductName.Should().Be("Ống PVC D21");
            handler.CallCount.Should().Be(1, "chỉ thử gọi Gemini đúng 1 lần, không retry vô hạn làm treo request");
        }

        // L1-EXT-08b | EP-Invalid | Sản phẩm đã ngừng kinh doanh -> từ chối TRƯỚC khi gọi ra ngoài
        [Fact]
        public async Task L1_EXT_08b_AiGenerator_DiscontinuedProduct_RejectedBeforeOutboundCall()
        {
            var db = TestDbFactory.Create();
            var product = TestData.SeedProduct(db, p => p.IsDiscontinued = true);
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
            var config = TestConfig.Create(new Dictionary<string, string?>
            {
                ["GeminiSettings:ApiKey"] = "test-gemini-key",
            });
            var sut = new AiGeneratorService(db, handler.CreateClient(), config, NullLogger<AiGeneratorService>.Instance);

            var act = () => sut.GenerateMarketingOptionsAsync(new API.DTOs.Marketing.GenerateAiContentRequestDto
            {
                ProductId = product.Id
            });

            await act.Should().ThrowAsync<Exception>();
            handler.CallCount.Should().Be(0, "không được tốn quota AI cho sản phẩm đã ngừng bán");
        }
    }
}
