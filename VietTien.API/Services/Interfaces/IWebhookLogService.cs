using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;

namespace VietTien.API.Services.Interfaces
{
    public interface IWebhookLogService
    {
        Task<Guid> LogReceivedAsync(SePayWebhookDto payload);
        Task MarkProcessedAsync(Guid id);
        Task MarkFailedAsync(Guid id, Exception ex);

        // Dùng chung bởi SePayWebhookRetryJob (tự động) và API "retry 1 webhook" (thủ công).
        // Tự đọc token đáng tin cậy từ cấu hình server, không bao giờ dùng token đã lưu (vì không lưu).
        Task<WebhookLogDto> RetryAsync(Guid id);

        Task<PagedResultDto<WebhookLogDto>> SearchAsync(WebhookLogQueryDto query);
    }
}
