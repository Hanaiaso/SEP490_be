using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class eSmsService : ISmsService
    {
        private readonly HttpClient _httpClient;
        private readonly ISystemConfigService _systemConfigService;
        private readonly string _fallbackApiKey;
        private readonly string _fallbackSecretKey;

        public eSmsService(HttpClient httpClient, IConfiguration configuration, ISystemConfigService systemConfigService)
        {
            _httpClient = httpClient;
            _systemConfigService = systemConfigService;
            _fallbackApiKey = configuration["eSMS:ApiKey"] ?? string.Empty;
            _fallbackSecretKey = configuration["eSMS:SecretKey"] ?? string.Empty;
        }

        public async Task<(bool Success, string ErrorMessage)> SendSmsAsync(string phoneNumber, string message)
        {
            var apiKey = await _systemConfigService.GetEffectiveValueAsync("ESMS_API_KEY") ?? _fallbackApiKey;
            var secretKey = await _systemConfigService.GetEffectiveValueAsync("ESMS_SECRET_KEY") ?? _fallbackSecretKey;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
            {
                // Mặc định trả về true nếu chưa cấu hình để không block luồng dev
                Console.WriteLine($"[Mock SMS to {phoneNumber}]: {message}");
                return (true, string.Empty);
            }

            try
            {
                var requestUrl = "https://rest.esms.vn/MainService.svc/json/SendMultipleMessage_V4_post_json/";

                var payload = new
                {
                    ApiKey = apiKey,
                    SecretKey = secretKey,
                    Content = message,
                    Phone = phoneNumber,
                    Brandname = "Baotrixemay", // Hardcode for eSMS trial template
                    SmsType = "2",
                    IsUnicode = "0"
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(requestUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    if (responseBody.Contains("\"CodeResult\":\"100\""))
                    {
                        return (true, string.Empty);
                    }
                }
                
                Console.WriteLine($"[eSMS Failed] Response: {responseBody}");
                return (false, $"Lỗi từ eSMS: {responseBody}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[eSMS Exception]: {ex.Message}");
                return (false, $"Lỗi kết nối eSMS: {ex.Message}");
            }
        }
    }
}
