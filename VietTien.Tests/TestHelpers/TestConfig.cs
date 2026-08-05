using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VietTien.API.Infrastructure.Security;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>Cấu hình in-memory dùng chung cho các service đọc IConfiguration / IOptions.</summary>
    public static class TestConfig
    {
        public const string SePayApiToken = "test-sepay-token";

        public static IConfiguration Create(Dictionary<string, string?>? overrides = null)
        {
            var settings = new Dictionary<string, string?>
            {
                ["SePaySettings:ApiToken"] = SePayApiToken,
                ["SePaySettings:BankAccount"] = "0123456789",
                ["SePaySettings:BankId"] = "TPBank",
                ["GoogleSettings:ClientId"] = "test-google-client-id",
                ["AppSettings:FrontendUrl"] = "http://localhost:3000",
            };
            if (overrides != null)
                foreach (var kv in overrides) settings[kv.Key] = kv.Value;

            return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        }

        public static IOptions<JwtSettings> JwtOptions(int accessMinutes = 15, int refreshDays = 7)
            => Options.Create(new JwtSettings
            {
                SecretKey = "unit-test-secret-key-which-is-long-enough-0123456789",
                Issuer = "VietTien.Tests",
                Audience = "VietTien.Tests.Audience",
                AccessTokenExpiryMinutes = accessMinutes,
                RefreshTokenExpiryDays = refreshDays
            });
    }
}
