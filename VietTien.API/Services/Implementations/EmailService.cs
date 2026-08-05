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

        // ═══════════════════════════════════════════════════════════════════════════
        // MINIMALIST & PROFESSIONAL DESIGN SYSTEM
        // ═══════════════════════════════════════════════════════════════════════════

        private const string PrimaryColor   = "#0f172a";   // Slate 900 (Thương hiệu chính)
        private const string AccentColor    = "#2563eb";   // Royal Blue (Nút bấm, điểm nhấn)
        private const string TextDark       = "#1e293b";   // Slate 800 (Văn bản chính)
        private const string TextMuted      = "#64748b";   // Slate 500 (Ghi chú / Nhãn)
        private const string BgPage         = "#f8fafc";   // Slate 50 (Nền trang)
        private const string BgCard         = "#ffffff";   // Khung nội dung
        private const string BgSubtle       = "#f1f5f9";   // Slate 100 (Khung phụ)
        private const string BorderColor    = "#e2e8f0";   // Slate 200 (Đường viền)

        /// <summary>Master layout tối giản, chuẩn doanh nghiệp</summary>
        private static string WrapLayout(string innerContent, string preheader = "")
        {
            return $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
  <meta charset='UTF-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <title>VietTien</title>
  <!--[if mso]>
  <style>table,td,div,p,span {{font-family: Arial, sans-serif !important;}}</style>
  <![endif]-->
</head>
<body style='margin:0; padding:0; background-color:{BgPage}; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; color:{TextDark}; -webkit-font-smoothing: antialiased;'>
  <!-- Preheader -->
  <div style='display:none; max-height:0; overflow:hidden; mso-hide:all;'>{preheader}</div>

  <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='background-color:{BgPage}; padding: 40px 16px;'>
    <tr>
      <td align='center'>
        <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='max-width:580px; background-color:{BgCard}; border-radius:12px; border:1px solid {BorderColor}; overflow:hidden;'>
          
          <!-- HEADER: Logo đơn giản -->
          <tr>
            <td style='padding: 32px 40px 24px 40px; border-bottom: 1px solid {BorderColor}; text-align: left;'>
              <div style='font-size: 22px; font-weight: 800; color: {PrimaryColor}; letter-spacing: 1.5px;'>
                VIETTIEN
              </div>
            </td>
          </tr>

          <!-- BODY CONTENT -->
          <tr>
            <td style='padding: 36px 40px;'>
              {innerContent}
            </td>
          </tr>

          <!-- FOOTER -->
          <tr>
            <td style='padding: 24px 40px; background-color: {BgSubtle}; border-top: 1px solid {BorderColor}; font-size: 12px; color: {TextMuted}; line-height: 1.6;'>
              <table role='presentation' cellpadding='0' cellspacing='0' width='100%'>
                <tr>
                  <td>
                    <strong>VietTien ERP System</strong><br/>
                    Email tự động từ hệ thống, vui lòng không phản hồi trực tiếp email này.
                  </td>
                  <td text-align='right' style='text-align: right; color: {TextMuted};'>
                    © {DateTime.UtcNow.Year} VietTien
                  </td>
                </tr>
              </table>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 1. OTP VERIFICATION EMAIL (Tối giản & Đầy đủ)
        // ═══════════════════════════════════════════════════════════════════════════

        public async Task SendOtpEmailAsync(string toEmail, string toName, string otpCode)
        {
            var subject = "Mã xác minh tài khoản - VietTien";

            var content = $@"
              <h1 style='margin:0 0 16px 0; font-size:20px; font-weight:700; color:{PrimaryColor};'>Xác minh địa chỉ email</h1>
              <p style='margin:0 0 24px 0; font-size:15px; color:{TextDark}; line-height:1.6;'>
                Xin chào <strong>{toName}</strong>,<br/>
                Vui lòng sử dụng mã OTP dưới đây để xác minh tài khoản của bạn tại VietTien.
              </p>

              <!-- MÃ OTP ĐƠN GIẢN -->
              <div style='background-color:{BgSubtle}; border:1px solid {BorderColor}; border-radius:8px; padding:20px; text-align:center; margin-bottom:24px;'>
                <span style='font-family:""Courier New"", Courier, monospace; font-size:36px; font-weight:700; color:{PrimaryColor}; letter-spacing:10px;'>{otpCode}</span>
              </div>

              <p style='margin:0 0 20px 0; font-size:13px; color:{TextMuted}; line-height:1.5;'>
                ⏱️ Mã xác minh có hiệu lực trong <strong>5 phút</strong>. Vui lòng không chia sẻ mã này với người khác.
              </p>

              <p style='margin:0; font-size:13px; color:{TextMuted}; line-height:1.5;'>
                Nếu bạn không thực hiện yêu cầu này, bạn có thể an tâm bỏ qua email.
              </p>";

            var body = WrapLayout(content, $"Mã xác minh VietTien của bạn là {otpCode}");
            await SendEmailAsync(toEmail, toName, subject, body);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 2. PASSWORD RESET EMAIL (Tối giản & Rõ ràng)
        // ═══════════════════════════════════════════════════════════════════════════

        public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetLink)
        {
            var subject = "Yêu cầu đặt lại mật khẩu - VietTien";

            var content = $@"
              <h1 style='margin:0 0 16px 0; font-size:20px; font-weight:700; color:{PrimaryColor};'>Đặt lại mật khẩu</h1>
              <p style='margin:0 0 24px 0; font-size:15px; color:{TextDark}; line-height:1.6;'>
                Xin chào <strong>{toName}</strong>,<br/>
                Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn. Nhấn vào nút bên dưới để tạo mật khẩu mới:
              </p>

              <!-- BUTTON -->
              <div style='margin-bottom:28px;'>
                <a href='{resetLink}' target='_blank' style='display:inline-block; background-color:{PrimaryColor}; color:#ffffff !important; text-decoration:none; padding:12px 28px; border-radius:6px; font-size:14px; font-weight:600;'>
                  Đặt lại mật khẩu
                </a>
              </div>

              <p style='margin:0 0 16px 0; font-size:13px; color:{TextMuted}; line-height:1.5;'>
                ⏱️ Liên kết này có hiệu lực trong <strong>1 giờ</strong>.
              </p>

              <!-- LINK DỰ PHÒNG -->
              <div style='background-color:{BgSubtle}; border-radius:6px; padding:12px 16px; margin-bottom:20px;'>
                <p style='margin:0 0 6px 0; font-size:12px; color:{TextMuted};'>Nếu không bấm được nút, copy đường dẫn sau:</p>
                <p style='margin:0; font-size:12px; color:{AccentColor}; word-break:break-all;'>{resetLink}</p>
              </div>

              <p style='margin:0; font-size:13px; color:{TextMuted}; line-height:1.5;'>
                Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.
              </p>";

            var body = WrapLayout(content, "Đặt lại mật khẩu tài khoản VietTien");
            await SendEmailAsync(toEmail, toName, subject, body);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 3. ORDER INVOICE EMAIL (Sạch sẽ, Chuyên nghiệp)
        // ═══════════════════════════════════════════════════════════════════════════

        public async Task SendOrderInvoiceEmailAsync(string toEmail, string toName, Order order, bool isSalesNotify = false)
        {
            var paymentMethodText = order.PaymentMethod switch
            {
                PaymentMethod.COD => "Thanh toán khi nhận hàng (COD)",
                PaymentMethod.SePay => "Chuyển khoản ngân hàng (SePay)",
                PaymentMethod.Cash => "Tiền mặt tại quầy",
                _ => order.PaymentMethod.ToString()
            };

            var subject = isSalesNotify
                ? $"[Thông báo đơn hàng] {order.OrderCode}"
                : $"Xác nhận đơn hàng {order.OrderCode} - VietTien";

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
                      <td style='padding:10px 12px; font-size:14px; border-bottom:1px solid {BorderColor}; color:{TextDark};'>{productName}</td>
                      <td style='padding:10px 12px; font-size:14px; border-bottom:1px solid {BorderColor}; color:{TextDark}; text-align:center;'>{qty}</td>
                      <td style='padding:10px 12px; font-size:14px; border-bottom:1px solid {BorderColor}; color:{TextDark}; text-align:right;'>{price.ToString("N0")}₫</td>
                      <td style='padding:10px 12px; font-size:14px; border-bottom:1px solid {BorderColor}; color:{TextDark}; text-align:right; font-weight:600;'>{lineTotal.ToString("N0")}₫</td>
                    </tr>";
                }
            }

            var titleText = isSalesNotify ? "Thông báo đơn hàng mới" : "Xác nhận đơn hàng";
            var introText = isSalesNotify
                ? $"Hệ thống vừa ghi nhận đơn hàng mới <strong>{order.OrderCode}</strong>."
                : $"Cảm ơn bạn đã đặt hàng tại <strong>VietTien</strong>. Đơn hàng của bạn đã được ghi nhận.";

            var customerName = order.CustomerProfile?.Representative ?? order.CustomerProfile?.CompanyName ?? "Khách lẻ";
            var customerAddress = order.CustomerProfile?.CompanyAddress ?? "Tại quầy";

            var discountRow = order.DiscountAmount > 0 ? $@"
            <tr>
              <td style='padding:4px 0; font-size:13px; color:{TextMuted};'>Chiết khấu:</td>
              <td style='padding:4px 0; font-size:13px; color:#16a34a; text-align:right; font-weight:600;'>-{order.DiscountAmount.ToString("N0")}₫</td>
            </tr>" : "";

            var content = $@"
              <h1 style='margin:0 0 16px 0; font-size:20px; font-weight:700; color:{PrimaryColor};'>{titleText}</h1>
              <p style='margin:0 0 24px 0; font-size:14px; color:{TextDark}; line-height:1.6;'>
                Xin chào <strong>{toName}</strong>,<br/>{introText}
              </p>

              <!-- THÔNG TIN ĐƠN HÀNG -->
              <div style='background-color:{BgSubtle}; border-radius:8px; padding:16px 20px; margin-bottom:24px; font-size:13px; line-height:1.7;'>
                <table role='presentation' cellpadding='0' cellspacing='0' width='100%'>
                  <tr>
                    <td style='color:{TextMuted}; width:35%;'>Mã đơn hàng:</td>
                    <td style='font-weight:700; color:{PrimaryColor};'>{order.OrderCode}</td>
                  </tr>
                  <tr>
                    <td style='color:{TextMuted};'>Ngày tạo:</td>
                    <td>{order.CreatedAt.AddHours(7):dd/MM/yyyy HH:mm}</td>
                  </tr>
                  <tr>
                    <td style='color:{TextMuted};'>Thanh toán:</td>
                    <td>{paymentMethodText}</td>
                  </tr>
                  <tr>
                    <td style='color:{TextMuted};'>Khách hàng:</td>
                    <td>{customerName}</td>
                  </tr>
                  <tr>
                    <td style='color:{TextMuted};'>Địa chỉ:</td>
                    <td>{customerAddress}</td>
                  </tr>
                </table>
              </div>

              <!-- BẢNG SẢN PHẨM -->
              <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='border-collapse:collapse; margin-bottom:20px;'>
                <thead>
                  <tr style='background-color:{BgSubtle};'>
                    <th style='padding:8px 12px; font-size:12px; font-weight:700; color:{TextMuted}; text-align:left; border-bottom:1px solid {BorderColor};'>Sản phẩm</th>
                    <th style='padding:8px 12px; font-size:12px; font-weight:700; color:{TextMuted}; text-align:center; border-bottom:1px solid {BorderColor};'>SL</th>
                    <th style='padding:8px 12px; font-size:12px; font-weight:700; color:{TextMuted}; text-align:right; border-bottom:1px solid {BorderColor};'>Đơn giá</th>
                    <th style='padding:8px 12px; font-size:12px; font-weight:700; color:{TextMuted}; text-align:right; border-bottom:1px solid {BorderColor};'>Thành tiền</th>
                  </tr>
                </thead>
                <tbody>
                  {itemsHtml}
                </tbody>
              </table>

              <!-- TỔNG TIỀN -->
              <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='margin-bottom:24px;'>
                <tr>
                  <td style='width:50%;'></td>
                  <td style='width:50%;'>
                    <table role='presentation' cellpadding='0' cellspacing='0' width='100%'>
                      <tr>
                        <td style='padding:4px 0; font-size:13px; color:{TextMuted};'>Tạm tính:</td>
                        <td style='padding:4px 0; font-size:13px; color:{TextDark}; text-align:right;'>{order.TotalAmount.ToString("N0")}₫</td>
                      </tr>
                      {discountRow}
                      <tr>
                        <td style='padding:4px 0; font-size:13px; color:{TextMuted};'>Thuế VAT (10%):</td>
                        <td style='padding:4px 0; font-size:13px; color:{TextDark}; text-align:right;'>{order.VatAmount.ToString("N0")}₫</td>
                      </tr>
                      <tr>
                        <td style='padding:8px 0 0 0; font-size:15px; font-weight:700; color:{PrimaryColor}; border-top:1px solid {BorderColor};'>TỔNG CỘNG:</td>
                        <td style='padding:8px 0 0 0; font-size:16px; font-weight:700; color:{PrimaryColor}; text-align:right; border-top:1px solid {BorderColor};'>{order.FinalPayment.ToString("N0")}₫</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>

              <p style='margin:0; font-size:13px; color:{TextMuted}; line-height:1.5;'>
                Nếu bạn cần hỗ trợ thêm thông tin về đơn hàng, vui lòng liên hệ với bộ phận CSKH của VietTien.
              </p>";

            var body = WrapLayout(content, $"Xác nhận đơn hàng {order.OrderCode}");

            var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var filePath = Path.Combine(wwwrootPath, "invoices", $"{order.OrderCode}.pdf");
            string? attachmentPath = File.Exists(filePath) ? filePath : null;

            await SendEmailAsync(toEmail, toName, subject, body, attachmentPath);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 4. STOCK TRANSFER NOTIFICATION (Tối giản)
        // ═══════════════════════════════════════════════════════════════════════════

        public async Task SendStockTransferNotificationAsync(string toEmail, string toName, string transferCode, string sourceWarehouse, string destinationWarehouse, string? note)
        {
            var subject = $"[Yêu cầu điều chuyển kho] {transferCode}";

            var content = $@"
              <h1 style='margin:0 0 16px 0; font-size:20px; font-weight:700; color:{PrimaryColor};'>Thông báo điều chuyển kho</h1>
              <p style='margin:0 0 20px 0; font-size:14px; color:{TextDark}; line-height:1.6;'>
                Xin chào <strong>{toName}</strong>,<br/>
                Hệ thống vừa ghi nhận một yêu cầu điều chuyển hàng hóa nội bộ.
              </p>

              <div style='background-color:{BgSubtle}; border-radius:8px; padding:18px 20px; margin-bottom:24px; font-size:14px; line-height:1.8;'>
                <div style='margin-bottom:8px;'>
                  <span style='color:{TextMuted}; display:inline-block; width:130px;'>Mã điều chuyển:</span>
                  <strong style='color:{PrimaryColor};'>{transferCode}</strong>
                </div>
                <div style='margin-bottom:8px;'>
                  <span style='color:{TextMuted}; display:inline-block; width:130px;'>Kho xuất:</span>
                  <span>{sourceWarehouse}</span>
                </div>
                <div style='margin-bottom:8px;'>
                  <span style='color:{TextMuted}; display:inline-block; width:130px;'>Kho nhận:</span>
                  <span>{destinationWarehouse}</span>
                </div>
                <div>
                  <span style='color:{TextMuted}; display:inline-block; width:130px;'>Ghi chú:</span>
                  <span>{(string.IsNullOrWhiteSpace(note) ? "Không có" : note)}</span>
                </div>
              </div>

              <p style='margin:0; font-size:13px; color:{TextMuted}; line-height:1.5;'>
                Vui lòng đăng nhập hệ thống VietTien ERP để xử lý phiếu điều chuyển.
              </p>";

            var body = WrapLayout(content, $"Thông báo điều chuyển kho {transferCode}");
            await SendEmailAsync(toEmail, toName, subject, body);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // 5. GENERIC EMAIL
        // ═══════════════════════════════════════════════════════════════════════════

        public async Task SendGenericEmailAsync(string toEmail, string subject, string body)
        {
            var content = $@"<div style='font-size:14px; color:{TextDark}; line-height:1.6;'>{body}</div>";
            var wrappedBody = WrapLayout(content, subject);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            message.To.Add(new MailboxAddress(toEmail, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = wrappedBody };
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(_emailSettings.SmtpHost, _emailSettings.SmtpPort, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        // ─── PRIVATE HELPER ────────────────────────────────────────────────────────

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
    }
}
