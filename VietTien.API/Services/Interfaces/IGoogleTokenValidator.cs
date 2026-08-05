using Google.Apis.Auth;

namespace VietTien.API.Services.Interfaces
{
    /// <summary>
    /// Bọc lệnh gọi tĩnh GoogleJsonWebSignature.ValidateAsync (gọi thẳng ra server Google, không
    /// mock được ở unit test) sau 1 interface để AuthService.LoginWithGoogleAsync có thể unit-test
    /// được (mock interface này) thay vì phải chuyển hẳn sang integration test (L1-AUTH-10/11).
    /// </summary>
    public interface IGoogleTokenValidator
    {
        /// <summary>Xác thực Google ID Token, trả về payload đã giải mã. Ném InvalidJwtException nếu token không hợp lệ.</summary>
        Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, string audience);
    }
}
