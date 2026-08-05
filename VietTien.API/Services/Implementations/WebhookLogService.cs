using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class WebhookLogService : IWebhookLogService
    {
        public const int MaxAttempts = 5;

        private readonly ApplicationDbContext _context;
        private readonly IOrderService _orderService;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;

        public WebhookLogService(
            ApplicationDbContext context,
            IOrderService orderService,
            INotificationService notificationService,
            IConfiguration configuration)
        {
            _context = context;
            _orderService = orderService;
            _notificationService = notificationService;
            _configuration = configuration;
        }

        public async Task<Guid> LogReceivedAsync(SePayWebhookDto payload)
        {
            var log = new WebhookLog
            {
                Source = "SePay",
                RawPayload = JsonSerializer.Serialize(payload),
                Status = WebhookLogStatus.Received,
                ReceivedAt = DateTime.UtcNow
            };

            _context.WebhookLogs.Add(log);
            await _context.SaveChangesAsync();
            return log.Id;
        }

        public async Task MarkProcessedAsync(Guid id)
        {
            var log = await _context.WebhookLogs.FirstOrDefaultAsync(w => w.Id == id);
            if (log == null) return;

            log.Status = WebhookLogStatus.Processed;
            log.ProcessedAt = DateTime.UtcNow;
            log.LastAttemptAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task MarkFailedAsync(Guid id, Exception ex)
        {
            var log = await _context.WebhookLogs.FirstOrDefaultAsync(w => w.Id == id);
            if (log == null) return;

            log.AttemptCount++;
            log.LastError = SensitiveDataRedactor.RedactPlainText(ex.Message, ex.GetType().Name);
            log.LastAttemptAt = DateTime.UtcNow;
            log.Status = log.AttemptCount >= MaxAttempts ? WebhookLogStatus.Abandoned : WebhookLogStatus.Failed;

            await _context.SaveChangesAsync();

            // Trạng thái đã được lưu thành công ở trên -> lỗi gửi notification không được che khuất
            // lỗi webhook gốc đang được xử lý ở nhánh catch gọi hàm này, chỉ log để theo dõi.
            if (log.Status == WebhookLogStatus.Abandoned)
            {
                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_26_WebhookRetryExhausted,
                        SystemRole.Admin,
                        "Webhook SePay thất bại sau nhiều lần thử",
                        $"Webhook {log.Id} đã thử {log.AttemptCount} lần đều thất bại, cần kiểm tra thủ công.",
                        log.Id,
                        "WebhookLog");
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"[WebhookLogService] Error sending webhook retry exhausted notification: {notifyEx.Message}");
                }
            }
            else if (log.AttemptCount == 1)
            {
                // Chỉ báo lần thử đầu tiên thất bại để tránh spam Admin ở mỗi lần retry tiếp theo.
                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_06_SePayError,
                        SystemRole.Admin,
                        "Lỗi xử lý webhook SePay",
                        $"Webhook {log.Id} xử lý thất bại: {log.LastError}",
                        log.Id,
                        "WebhookLog");
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"[WebhookLogService] Error sending SePay webhook error notification: {notifyEx.Message}");
                }
            }
        }

        public async Task<WebhookLogDto> RetryAsync(Guid id)
        {
            var log = await _context.WebhookLogs.FirstOrDefaultAsync(w => w.Id == id);
            if (log == null) throw new Exception("Không tìm thấy webhook log.");

            var trustedToken = _configuration["SePaySettings:ApiToken"] ?? string.Empty;

            try
            {
                var payload = JsonSerializer.Deserialize<SePayWebhookDto>(log.RawPayload)
                    ?? throw new Exception("Payload webhook không hợp lệ, không thể parse lại.");

                await _orderService.ProcessSePayWebhookAsync(payload, trustedToken);
                await MarkProcessedAsync(id);
            }
            catch (Exception ex)
            {
                await MarkFailedAsync(id, ex);
            }

            var updated = await _context.WebhookLogs.AsNoTracking().FirstAsync(w => w.Id == id);
            return ToDto(updated);
        }

        public async Task<PagedResultDto<WebhookLogDto>> SearchAsync(WebhookLogQueryDto query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 or > 200 ? 20 : query.PageSize;

            var source = _context.WebhookLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Source))
                source = source.Where(w => w.Source == query.Source);

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                if (!Enum.TryParse<WebhookLogStatus>(query.Status, true, out var status))
                    throw new Exception($"Status '{query.Status}' không hợp lệ.");
                source = source.Where(w => w.Status == status);
            }

            if (query.FromDate.HasValue)
                source = source.Where(w => w.ReceivedAt >= query.FromDate.Value);

            if (query.ToDate.HasValue)
                source = source.Where(w => w.ReceivedAt <= query.ToDate.Value);

            var totalCount = await source.CountAsync();

            var entities = await source
                .OrderByDescending(w => w.ReceivedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResultDto<WebhookLogDto>
            {
                Items = entities.Select(ToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }

        private static WebhookLogDto ToDto(WebhookLog w) => new()
        {
            Id = w.Id,
            Source = w.Source,
            RawPayload = w.RawPayload,
            Status = w.Status.ToString(),
            AttemptCount = w.AttemptCount,
            LastError = w.LastError,
            ReceivedAt = w.ReceivedAt,
            LastAttemptAt = w.LastAttemptAt,
            ProcessedAt = w.ProcessedAt
        };
    }
}
