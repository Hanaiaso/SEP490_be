using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <summary>
    /// Bù migration còn thiếu cho commit e5a4477 ("update for admin integration key, delivery schedule
    /// conflict"). Commit đó thêm <c>SystemConfig.IsSecret</c> + 11 dòng seed cấu hình tích hợp vào
    /// model VÀ cập nhật ApplicationDbContextModelSnapshot.cs, nhưng KHÔNG sinh migration tương ứng.
    ///
    /// Hậu quả trước khi có migration này: mọi DB dựng bằng <c>Migrate()</c> (Program.cs:285 chạy lúc
    /// khởi động) đều thiếu cột <c>SystemConfigs.IsSecret</c>, trong khi model runtime lại đòi cột đó
    /// -> mọi truy vấn/ghi trên SystemConfigs ném "Invalid column name 'IsSecret'". Kéo theo
    /// SystemConfigService chết, mà OrderService.CalculateDiscountAsync đọc QUOTATION_MIN_VALUE qua
    /// service này -> hỏng cả luồng đặt hàng.
    ///
    /// Vì sao viết tay thay vì `dotnet ef migrations add`: EF diff MODEL với SNAPSHOT, mà snapshot đã
    /// chứa sẵn IsSecret + 11 dòng seed rồi, nên lệnh đó chỉ sinh ra một migration RỖNG.
    /// `dotnet ef migrations has-pending-model-changes` cũng báo "no changes" vì cùng lý do — nó không
    /// kiểm được lệch giữa snapshot và các migration.
    /// </summary>
    public partial class AddSystemConfigIsSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSecret",
                table: "SystemConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // 11 tham số tích hợp: chỉ tạo DÒNG cấu hình (không tạo SystemConfigVersion), nên
            // GetEffectiveValueAsync trả null và code vẫn fallback về appsettings như trước —
            // hành vi không đổi ngay sau khi migrate, đúng ý commit gốc.
            migrationBuilder.InsertData(
                table: "SystemConfigs",
                columns: new[] { "Key", "Description", "IsActive", "IsSecret", "OwnerLevel", "Unit", "ValueType" },
                values: new object[,]
                {
                    { "SEPAY_API_TOKEN", "API Token xác thực webhook SePay", true, true, "Admin", null, "String" },
                    { "SEPAY_BANK_ACCOUNT", "Số tài khoản ngân hàng nhận thanh toán SePay", true, false, "Admin", null, "String" },
                    { "SEPAY_BANK_ID", "Mã ngân hàng (bankId) dùng sinh QR SePay", true, false, "Admin", null, "String" },
                    { "GOOGLE_OAUTH_CLIENT_ID", "Google OAuth Client ID dùng xác thực đăng nhập Google", true, false, "Admin", null, "String" },
                    { "ESMS_API_KEY", "API Key dịch vụ SMS eSMS", true, true, "Admin", null, "String" },
                    { "ESMS_SECRET_KEY", "Secret Key dịch vụ SMS eSMS", true, true, "Admin", null, "String" },
                    { "EMAIL_SMTP_HOST", "SMTP host gửi email hệ thống", true, false, "Admin", null, "String" },
                    { "EMAIL_SMTP_PORT", "SMTP port gửi email hệ thống", true, false, "Admin", null, "Int" },
                    { "EMAIL_SENDER_EMAIL", "Địa chỉ email gửi đi", true, false, "Admin", null, "String" },
                    { "EMAIL_SENDER_PASSWORD", "Mật khẩu ứng dụng (App Password) của hộp thư gửi", true, true, "Admin", null, "String" },
                    { "MAKE_WEBHOOK_URL", "Webhook URL kịch bản Make.com đăng bài Facebook", true, false, "Admin", null, "String" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var key in new[]
            {
                "SEPAY_API_TOKEN", "SEPAY_BANK_ACCOUNT", "SEPAY_BANK_ID", "GOOGLE_OAUTH_CLIENT_ID",
                "ESMS_API_KEY", "ESMS_SECRET_KEY", "EMAIL_SMTP_HOST", "EMAIL_SMTP_PORT",
                "EMAIL_SENDER_EMAIL", "EMAIL_SENDER_PASSWORD", "MAKE_WEBHOOK_URL",
            })
            {
                migrationBuilder.DeleteData(table: "SystemConfigs", keyColumn: "Key", keyValue: key);
            }

            migrationBuilder.DropColumn(name: "IsSecret", table: "SystemConfigs");
        }
    }
}
