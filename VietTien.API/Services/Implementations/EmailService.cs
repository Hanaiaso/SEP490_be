using System.IO;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _emailSettings;

        public EmailService(IOptions<EmailSettings> emailSettings)
        {
            _emailSettings = emailSettings.Value;
        }

        public async Task SendOtpEmailAsync(string toEmail, string toName, string otpCode)
        {
            var subject = "🔐 Mã xác minh tài khoản VietTien";
            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
  <meta charset='UTF-8'>
  <style>
    body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
    .container {{ max-width: 600px; margin: 30px auto; background-color: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
    .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; }}
    .header h1 {{ color: white; margin: 0; font-size: 24px; }}
    .content {{ padding: 40px 30px; }}
    .otp-box {{ background: #f0f4ff; border: 2px dashed #667eea; border-radius: 8px; text-align: center; padding: 20px; margin: 25px 0; }}
    .otp-code {{ font-size: 42px; font-weight: bold; color: #667eea; letter-spacing: 8px; }}
    .expire-note {{ color: #999; font-size: 14px; text-align: center; }}
    .footer {{ background: #f9f9f9; padding: 20px; text-align: center; color: #aaa; font-size: 12px; border-top: 1px solid #eee; }}
  </style>
</head>
<body>
  <div class='container'>
    <div class='header'>
      <h1>🛍️ VietTien</h1>
    </div>
    <div class='content'>
      <p>Xin chào <strong>{toName}</strong>,</p>
      <p>Cảm ơn bạn đã đăng ký tài khoản tại <strong>VietTien</strong>. Vui lòng sử dụng mã OTP bên dưới để xác minh địa chỉ email của bạn:</p>
      <div class='otp-box'>
        <div class='otp-code'>{otpCode}</div>
      </div>
      <p class='expire-note'>⏱️ Mã OTP có hiệu lực trong <strong>5 phút</strong>. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
      <p>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này.</p>
    </div>
    <div class='footer'>
      <p>© 2024 VietTien. Tất cả quyền được bảo lưu.</p>
    </div>
  </div>
</body>
</html>";

            await SendEmailAsync(toEmail, toName, subject, body);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetLink)
        {
            var subject = "🔑 Đặt lại mật khẩu VietTien";
            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
  <meta charset='UTF-8'>
  <style>
    body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
    .container {{ max-width: 600px; margin: 30px auto; background-color: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
    .header {{ background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); padding: 30px; text-align: center; }}
    .header h1 {{ color: white; margin: 0; font-size: 24px; }}
    .content {{ padding: 40px 30px; }}
    .btn {{ display: inline-block; background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); color: white !important; text-decoration: none; padding: 15px 35px; border-radius: 50px; font-size: 16px; font-weight: bold; margin: 25px 0; }}
    .btn-wrapper {{ text-align: center; }}
    .expire-note {{ color: #999; font-size: 14px; text-align: center; }}
    .link-fallback {{ word-break: break-all; color: #667eea; font-size: 12px; }}
    .footer {{ background: #f9f9f9; padding: 20px; text-align: center; color: #aaa; font-size: 12px; border-top: 1px solid #eee; }}
  </style>
</head>
<body>
  <div class='container'>
    <div class='header'>
      <h1>🛍️ VietTien</h1>
    </div>
    <div class='content'>
      <p>Xin chào <strong>{toName}</strong>,</p>
      <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn. Nhấn vào nút bên dưới để tiến hành:</p>
      <div class='btn-wrapper'>
        <a href='{resetLink}' class='btn'>🔑 Đặt lại mật khẩu</a>
      </div>
      <p class='expire-note'>⏱️ Liên kết có hiệu lực trong <strong>1 giờ</strong>. Sau thời gian này, bạn sẽ cần yêu cầu lại.</p>
      <p>Nếu liên kết không hoạt động, hãy sao chép và dán vào trình duyệt:</p>
      <p class='link-fallback'>{resetLink}</p>
      <p>Nếu bạn không thực hiện yêu cầu này, mật khẩu của bạn sẽ không bị thay đổi.</p>
    </div>
    <div class='footer'>
      <p>© 2024 VietTien. Tất cả quyền được bảo lưu.</p>
    </div>
  </div>
</body>
</html>";

            await SendEmailAsync(toEmail, toName, subject, body);
        }

        public async Task SendOrderInvoiceEmailAsync(string toEmail, string toName, Order order, bool isSalesNotify = false)
        {
            var paymentMethodText = order.PaymentMethod switch
            {
                PaymentMethod.COD => "Thanh toán khi nhận hàng (COD)",
                PaymentMethod.SePay => "Chuyển khoản SePay (Đã thanh toán)",
                PaymentMethod.Cash => "Tiền mặt tại quầy (Đã thanh toán)",
                _ => order.PaymentMethod.ToString()
            };

            var subject = isSalesNotify 
                ? $"🔔 Đơn hàng mới: {order.OrderCode} ({paymentMethodText})" 
                : $"🛒 Hóa đơn & Xác nhận đơn hàng {order.OrderCode} - VietTien";

            var itemsHtml = "";
            if (order.OrderItems != null)
            {
                foreach (var item in order.OrderItems)
                {
                    var productName = item.Product?.Name ?? "Sản phẩm";
                    var qty = item.Quantity;
                    var price = item.PriceSnapshot;
                    var lineTotal = price * qty;
                    itemsHtml += $@"
                    <tr>
                      <td style='padding: 10px; border-bottom: 1px solid #eee;'>{productName}</td>
                      <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: center;'>{qty}</td>
                      <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>{price.ToString("N0")} đ</td>
                      <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right; font-weight: bold;'>{lineTotal.ToString("N0")} đ</td>
                    </tr>";
                }
            }

            var title = isSalesNotify ? "ĐƠN HÀNG MỚI ĐÃ ĐẶT" : "XÁC NHẬN ĐƠN HÀNG & HÓA ĐƠN";
            var intro = isSalesNotify 
                ? $"Hệ thống VietTien ghi nhận đơn hàng {order.OrderCode} đã được tạo thành công với hình thức thanh toán: {paymentMethodText}." 
                : $"Cảm ơn bạn đã mua hàng tại <strong>VietTien</strong>. Đơn hàng của bạn đã được tiếp nhận và đang được xử lý.";

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
  <meta charset='UTF-8'>
  <style>
    body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
    .container {{ max-width: 600px; margin: 30px auto; background-color: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
    .header {{ background: linear-gradient(135deg, #1f3b64 0%, #2e5b9a 100%); padding: 30px; text-align: center; }}
    .header h1 {{ color: white; margin: 0; font-size: 24px; }}
    .content {{ padding: 30px; }}
    .invoice-details {{ background: #f9f9f9; padding: 15px; border-radius: 8px; margin: 20px 0; font-size: 14px; line-height: 1.6; }}
    .table {{ width: 100%; border-collapse: collapse; margin: 20px 0; font-size: 14px; }}
    .table th {{ background: #f4f4f4; padding: 10px; text-align: left; border-bottom: 2px solid #ddd; }}
    .summary-box {{ float: right; width: 250px; margin-top: 15px; font-size: 14px; line-height: 1.8; }}
    .summary-line {{ display: flex; justify-content: space-between; }}
    .summary-total {{ font-size: 16px; font-weight: bold; border-top: 1px solid #ddd; padding-top: 5px; margin-top: 5px; }}
    .footer {{ background: #f9f9f9; padding: 20px; text-align: center; color: #aaa; font-size: 12px; border-top: 1px solid #eee; clear: both; }}
  </style>
</head>
<body>
  <div class='container'>
    <div class='header'>
      <h1>🛍️ VietTien - {title}</h1>
    </div>
    <div class='content'>
      <p>Xin chào <strong>{toName}</strong>,</p>
      <p>{intro}</p>
      
      <div class='invoice-details'>
        <strong>Mã đơn hàng:</strong> {order.OrderCode}<br/>
        <strong>Ngày đặt:</strong> {order.CreatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm")}<br/>
        <strong>Hình thức thanh toán:</strong> {paymentMethodText}<br/>
        <strong>Khách hàng:</strong> {order.CustomerProfile?.Representative ?? order.CustomerProfile?.CompanyName ?? "Khách lẻ"}<br/>
        <strong>Địa chỉ giao hàng:</strong> {order.CustomerProfile?.CompanyAddress ?? "Tại quầy"}<br/>
        <strong>Số điện thoại:</strong> {order.CustomerProfile?.CompanyPhone ?? ""}
      </div>

      <h3>Chi tiết sản phẩm</h3>
      <table class='table'>
        <thead>
          <tr>
            <th style='padding: 10px; text-align: left;'>Sản phẩm</th>
            <th style='padding: 10px; text-align: center;'>SL</th>
            <th style='padding: 10px; text-align: right;'>Đơn giá</th>
            <th style='padding: 10px; text-align: right;'>Thành tiền</th>
          </tr>
        </thead>
        <tbody>
          {itemsHtml}
        </tbody>
      </table>

      <div class='summary-box'>
        <div class='summary-line'>
          <span>Tạm tính:</span>
          <span>{order.TotalAmount.ToString("N0")} đ</span>
        </div>
        {HtmlDiscountLine(order.DiscountAmount)}
        <div class='summary-line'>
          <span>VAT (10%):</span>
          <span>{order.VatAmount.ToString("N0")} đ</span>
        </div>
        <div class='summary-line summary-total'>
          <span>Tổng thanh toán:</span>
          <span>{order.FinalPayment.ToString("N0")} đ</span>
        </div>
      </div>
      <div style='clear: both;'></div>
    </div>
    <div class='footer'>
      <p>© 2024 VietTien. Tất cả quyền được bảo lưu.</p>
    </div>
  </div>
</body>
</html>";

            // Attach PDF if exists
            var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var filePath = Path.Combine(wwwrootPath, "invoices", $"{order.OrderCode}.pdf");
            string? attachmentPath = File.Exists(filePath) ? filePath : null;

            await SendEmailAsync(toEmail, toName, subject, body, attachmentPath);
        }

        private string HtmlDiscountLine(decimal discount)
        {
            if (discount <= 0) return "";
            return $@"
            <div class='summary-line' style='color: #16a34a;'>
              <span>Chiết khấu:</span>
              <span>-{discount.ToString("N0")} đ</span>
            </div>";
        }

        public async Task SendStockTransferNotificationAsync(string toEmail, string toName, string transferCode, string sourceWarehouse, string destinationWarehouse, string? note)
        {
            var subject = $"📦 Thông báo Yêu cầu Điều chuyển kho: {transferCode}";
            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
  <meta charset='UTF-8'>
  <style>
    body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
    .container {{ max-width: 600px; margin: 30px auto; background-color: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
    .header {{ background: linear-gradient(135deg, #1f3b64 0%, #2e5b9a 100%); padding: 30px; text-align: center; }}
    .header h1 {{ color: white; margin: 0; font-size: 24px; }}
    .content {{ padding: 30px; }}
    .info-box {{ background: #f9f9f9; padding: 15px; border-radius: 8px; margin: 20px 0; font-size: 14px; line-height: 1.6; border-left: 4px solid #2e5b9a; }}
    .footer {{ background: #f9f9f9; padding: 20px; text-align: center; color: #aaa; font-size: 12px; border-top: 1px solid #eee; }}
  </style>
</head>
<body>
  <div class='container'>
    <div class='header'>
      <h1>🛍️ VietTien - Thông báo Điều chuyển</h1>
    </div>
    <div class='content'>
      <p>Xin chào <strong>{toName}</strong>,</p>
      <p>Bạn nhận được một thông báo về yêu cầu điều chuyển hàng hóa nội bộ.</p>
      
      <div class='info-box'>
        <strong>Mã điều chuyển:</strong> {transferCode}<br/>
        <strong>Từ kho:</strong> {sourceWarehouse}<br/>
        <strong>Đến kho:</strong> {destinationWarehouse}<br/>
        <strong>Ghi chú:</strong> {(string.IsNullOrWhiteSpace(note) ? "Không có" : note)}
      </div>

      <p>Vui lòng đăng nhập vào hệ thống để xem chi tiết và thực hiện xuất/nhập kho tương ứng.</p>
    </div>
    <div class='footer'>
      <p>© 2024 VietTien. Tất cả quyền được bảo lưu.</p>
    </div>
  </div>
</body>
</html>";

            await SendEmailAsync(toEmail, toName, subject, body);
        }

        // ─── Private Helper ────────────────────────────────────────────────────────

        private async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string? attachmentPath = null)
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            email.To.Add(new MailboxAddress(toName, toEmail));
            email.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
            {
                bodyBuilder.Attachments.Add(attachmentPath);
            }
            email.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_emailSettings.SmtpHost, _emailSettings.SmtpPort, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }

        public async Task SendGenericEmailAsync(string toEmail, string subject, string body)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            message.To.Add(new MailboxAddress(toEmail, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = body };
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(_emailSettings.SmtpHost, _emailSettings.SmtpPort, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }
    }
}
