using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class MakeWebhookService : IMakeWebhookService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MakeWebhookService> _logger;
        private readonly ISystemConfigService _systemConfigService;

        public MakeWebhookService(HttpClient httpClient, IConfiguration configuration, ILogger<MakeWebhookService> logger, ISystemConfigService systemConfigService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _systemConfigService = systemConfigService;
        }

        public async Task<bool> TriggerPostToMakeAsync(MarketingPost post)
        {
            var webhookUrl = await _systemConfigService.GetEffectiveValueAsync("MAKE_WEBHOOK_URL")
                ?? _configuration["MakeCom:WebhookUrl"]
                ?? "https://hook.eu1.make.com/placeholder-viettien-marketing";

            var payload = new
            {
                postId = post.Id,
                postCode = post.Code,
                productId = post.ProductId,
                productName = post.Product?.Name ?? "",
                productSku = post.Product?.Sku ?? "",
                productPrice = post.Product?.StandardListedPrice ?? 0,
                imageUrl = !string.IsNullOrEmpty(post.SelectedImageUrl) ? post.SelectedImageUrl : post.GeneratedImageUrl,
                caption = post.EditedCaption,
                hashtags = post.Hashtags,
                ctaText = post.CtaText,
                scheduledTime = post.ScheduledTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                createdAt = post.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                _logger.LogInformation("Sending Webhook payload to Make.com for MarketingPost ID {PostId}", post.Id);
                var response = await _httpClient.PostAsync(webhookUrl, jsonContent);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Make.com Webhook returned non-success status code {StatusCode} for Post ID {PostId}", response.StatusCode, post.Id);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Webhook to Make.com for MarketingPost ID {PostId}", post.Id);
                return false;
            }
        }
    }
}
