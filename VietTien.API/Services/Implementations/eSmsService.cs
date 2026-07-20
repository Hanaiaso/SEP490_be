using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class eSmsService : ISmsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _secretKey;
        private readonly string _brandname;

        public eSmsService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["eSMS:ApiKey"] ?? string.Empty;
            _secretKey = configuration["eSMS:SecretKey"] ?? string.Empty;
            _brandname = configuration["eSMS:Brandname"] ?? "Baotrimac"; // Default brandname or just use SmsType=2
        }

        public async Task<(bool Success, string ErrorMessage)> SendSmsAsync(string phoneNumber, string message)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_secretKey))
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
                    ApiKey = _apiKey,
                    SecretKey = _secretKey,
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
