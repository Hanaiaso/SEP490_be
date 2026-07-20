using System.Threading.Tasks;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IEmailService
    {
        /// <summary>Gửi email chứa mã OTP xác minh tài khoản</summary>
        Task SendOtpEmailAsync(string toEmail, string toName, string otpCode);

        /// <summary>Gửi email chứa link đặt lại mật khẩu</summary>
        Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetLink);

        /// <summary>Gửi email hóa đơn và xác nhận đơn hàng</summary>
        Task SendOrderInvoiceEmailAsync(string toEmail, string toName, Order order, bool isSalesNotify = false);

        /// <summary>Gửi email thông báo điều chuyển kho</summary>
        Task SendStockTransferNotificationAsync(string toEmail, string toName, string transferCode, string sourceWarehouse, string destinationWarehouse, string? note);

        /// <summary>Gửi email với nội dung tùy chỉnh</summary>
        Task SendGenericEmailAsync(string toEmail, string subject, string body);
    }
}
