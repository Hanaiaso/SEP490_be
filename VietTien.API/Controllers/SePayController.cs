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

        public SePayController(IOrderService orderService)
        {
            _orderService = orderService;
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

                Console.WriteLine($"[SePay Webhook] Extracted Token: '{providedToken}'");

                var isDevelopment = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(providedToken) && !isDevelopment)
                    return Unauthorized(new { success = false, message = "Missing Token" });

                await _orderService.ProcessSePayWebhookAsync(payload, providedToken ?? "");
                return Ok(new { success = true });
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[SePay Webhook] Unauthorized access: {ex.Message}");
                return Unauthorized(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SePay Webhook] Error processing callback: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
