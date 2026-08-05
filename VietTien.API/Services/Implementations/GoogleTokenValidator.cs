using Google.Apis.Auth;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class GoogleTokenValidator : IGoogleTokenValidator
    {
        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, string audience)
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { audience }
            };
            return GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
    }
}
