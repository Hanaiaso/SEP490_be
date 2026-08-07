using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using VietTien.API.DTOs.SePay;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/webhooks")]
    [ApiController]
    public class SePayController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly IWebhookLogService _webhookLogService;

        public SePayController(IOrderService orderService, IWebhookLogService webhookLogService)
        {
            _orderService = orderService;
            _webhookLogService = webhookLogService;
        }

        [HttpPost("sepay-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook([FromBody] SePayWebhookDto payload)
        {
            try
            {
                // Diagnostic logging
                var headersString = string.Join(", ", Request.Headers.Select(h => $"{h.Key}: {h.Value}"));
                Console.WriteLine($"[SePay Webhook] Received request. Headers: {headersString}");
                Console.WriteLine($"[SePay Webhook] Body Content: transferContent='{payload.transferContent}', content='{payload.content}', transferAmount={payload.transferAmount}, referenceCode='{payload.referenceCode}', referenceNumber='{payload.referenceNumber}'");

                string? providedToken = Request.Headers["x-sepay-token"].FirstOrDefault()?.Trim();
                if (string.IsNullOrEmpty(providedToken))
                {
                    var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(authHeader))
                    {
                        providedToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? authHeader.Substring(7).Trim()
                            : authHeader.Trim();
                    }
                }

                // Fallback to URL query parameter
                if (string.IsNullOrEmpty(providedToken))
                {
                    providedToken = Request.Query["token"].FirstOrDefault()?.Trim();
                }

                Console.WriteLine($"[SePay Webhook] Token extracted: {(string.IsNullOrEmpty(providedToken) ? "(none)" : "(present)")}");

                // GH-01/SEC-03: xác thực webhook KHÔNG được nới lỏng theo môi trường — thiếu token luôn
                // bị từ chối, kể cả khi ASPNETCORE_ENVIRONMENT=Development (biến này dễ bị set sai trên
                // server thật và không đáng tin để làm điều kiện bảo mật).
                if (string.IsNullOrEmpty(providedToken))
                    return Unauthorized(new { success = false, message = "Missing Token" });

                // Ghi lại payload đã xác thực (không ghi token) để có thể replay/retry nếu xử lý lỗi.
                var webhookLogId = await _webhookLogService.LogReceivedAsync(payload);

                try
                {
                    await _orderService.ProcessSePayWebhookAsync(payload, providedToken ?? "");
                    await _webhookLogService.MarkProcessedAsync(webhookLogId);
                    return Ok(new { success = true });
                }
                catch (UnauthorizedAccessException)
                {
                    // Sai token: giữ nguyên hành vi bảo mật cũ (không xử lý), không đưa vào diện tự động retry
                    // (retry dùng token đáng tin cậy phía server nên sẽ "hợp thức hóa" ngược 1 request sai token nếu cho retry ở đây).
                    throw;
                }
                catch (Exception innerEx)
                {
                    await _webhookLogService.MarkFailedAsync(webhookLogId, innerEx);
                    throw;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[SePay Webhook] Unauthorized access: {ex.Message}");
                return Unauthorized(new { success = false, message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // GH-02: đơn đã bị 1 luồng khác (vd. xác nhận thủ công) xử lý song song và commit trước.
                Console.WriteLine("[SePay Webhook] Concurrency conflict — đơn đã được xử lý bởi request khác.");
                return Conflict(new { success = false, message = "Đơn hàng đã được xử lý bởi một yêu cầu khác." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SePay Webhook] Error processing callback: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
