using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using VietTien.API.DTOs.Marketing;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Các fake IO dùng cho hạ tầng L2 (SqlServerFixture). KHÔNG đăng ký cho L3 —
    /// CustomWebApplicationFactory giữ nguyên như cũ.
    ///
    /// Lý do phải fake chứ không chặn ở tầng HTTP (WireMock):
    ///  - eSmsService.cs:33 hard-code URL TUYỆT ĐỐI (https://rest.esms.vn/...) nên override
    ///    HttpClient.BaseAddress vô tác dụng; muốn chặn phải thay hẳn HttpMessageHandler.
    ///  - EmailService.cs:366,390 tự `new SmtpClient()` inline và ép SecureSocketOptions.StartTls
    ///    — SMTP không phải HttpClient, WireMock không có cửa nào chen vào.
    ///  - AiGeneratorService đăng ký AddScoped (Program.cs:72) chứ không AddHttpClient nên không có
    ///    typed-client seam để thay handler.
    ///  - CloudinaryDotNet tự dựng HTTP stack riêng trong constructor.
    /// Và quan trọng nhất: appsettings.Development.json (mà L2 buộc phải dùng để có JwtSettings:SecretKey)
    /// chứa API KEY THẬT của eSMS/Gemini/Cloudinary/MakeCom + SMTP password thật, nên nhánh "mock khi
    /// thiếu key" của eSmsService.cs:24 KHÔNG kích hoạt — không fake là gửi SMS/email thật.
    ///
    /// Tất cả đăng ký singleton để test đọc được bản ghi xuyên nhiều scope.
    /// </summary>
    public class FakeEmailService : IEmailService
    {
        public record SentEmail(string To, string Subject, string Body);

        public ConcurrentQueue<SentEmail> Sent { get; } = new();

        public Task SendOtpEmailAsync(string toEmail, string toName, string otpCode)
        {
            Sent.Enqueue(new SentEmail(toEmail, "OTP", otpCode));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetLink)
        {
            Sent.Enqueue(new SentEmail(toEmail, "PasswordReset", resetLink));
            return Task.CompletedTask;
        }

        public Task SendOrderInvoiceEmailAsync(string toEmail, string toName, Order order, bool isSalesNotify = false)
        {
            Sent.Enqueue(new SentEmail(toEmail, "OrderInvoice", order.OrderCode ?? order.Id.ToString()));
            return Task.CompletedTask;
        }

        public Task SendStockTransferNotificationAsync(string toEmail, string toName, string transferCode,
            string sourceWarehouse, string destinationWarehouse, string? note)
        {
            Sent.Enqueue(new SentEmail(toEmail, "StockTransfer", transferCode));
            return Task.CompletedTask;
        }

        public Task SendGenericEmailAsync(string toEmail, string subject, string body)
        {
            Sent.Enqueue(new SentEmail(toEmail, subject, body));
            return Task.CompletedTask;
        }
    }

    public class FakeSmsService : ISmsService
    {
        public record SentSms(string PhoneNumber, string Message);

        public ConcurrentQueue<SentSms> Sent { get; } = new();

        public Task<(bool Success, string ErrorMessage)> SendSmsAsync(string phoneNumber, string message)
        {
            Sent.Enqueue(new SentSms(phoneNumber, message));
            return Task.FromResult((true, string.Empty));
        }
    }

    public class FakeMakeWebhookService : IMakeWebhookService
    {
        public ConcurrentQueue<Guid> Triggered { get; } = new();

        /// <summary>Đặt false để mô phỏng Make.com trả lỗi (test retry/idempotency của MarketingPostMakeScheduleJob).</summary>
        public bool NextResult { get; set; } = true;

        public Task<bool> TriggerPostToMakeAsync(MarketingPost post)
        {
            Triggered.Enqueue(post.Id);
            return Task.FromResult(NextResult);
        }
    }

    public class FakeAiGeneratorService : IAiGeneratorService
    {
        public ConcurrentQueue<GenerateAiContentRequestDto> Requests { get; } = new();

        public Task<GenerateAiContentResponseDto> GenerateMarketingOptionsAsync(GenerateAiContentRequestDto request)
        {
            Requests.Enqueue(request);
            return Task.FromResult(new GenerateAiContentResponseDto
            {
                ProductId = request.ProductId,
                ProductName = "Fake Product",
                ProductSku = "FAKE-SKU",
                ProductPrice = 0m,
                ProductUnit = "Cái",
                Options = new List<MarketingOptionDto>()
            });
        }
    }

    public class FakeCloudinaryService : ICloudinaryService
    {
        public ConcurrentQueue<string> Uploaded { get; } = new();

        private string Fake(string folder)
        {
            var url = $"https://fake.local/{folder}/{Guid.NewGuid():N}.jpg";
            Uploaded.Enqueue(url);
            return url;
        }

        public Task<string> UploadImageAsync(IFormFile file, string folder) => Task.FromResult(Fake(folder));

        public Task<string> UploadBase64ImageAsync(string base64String, string folder, string fileName)
            => Task.FromResult(Fake(folder));

        public Task<string> UploadEvidenceAsync(IFormFile file, string folder) => Task.FromResult(Fake(folder));

        public Task<string> UploadAttachmentAsync(IFormFile file, string folder) => Task.FromResult(Fake(folder));

        public Task<bool> DeleteImageAsync(string publicId) => Task.FromResult(true);

        public string? ExtractPublicId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var name = url.Split('/').LastOrDefault();
            if (string.IsNullOrEmpty(name)) return null;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }
    }
}
