using System.Threading.Tasks;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IMakeWebhookService
    {
        /// <summary>Gửi webhook tới Make.com. Trả về true nếu request tới được Make.com thành công
        /// (HTTP 2xx) — không đồng nghĩa bài đã đăng thật sự, việc đó do callback
        /// HandleMakeWebhookCallbackAsync xác nhận sau. Trả về false nếu request thất bại
        /// (network lỗi, timeout, HTTP lỗi) — khi đó Make.com sẽ không bao giờ gọi callback.</summary>
        Task<bool> TriggerPostToMakeAsync(MarketingPost post);
    }
}
