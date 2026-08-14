using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class FixLegacyInventoryCountSessionClosedNotificationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enum NotificationType.SYS_39_InventoryCountSessionClosed đã được đổi số thành
            // SYS_41_InventoryCountSessionClosed khi SYS_39/SYS_40 được dùng cho DeliveryTrip/MultiPick
            // (xem VietTien.API/Models/Notification.cs). Các dòng Notifications tạo trước lần đổi này
            // vẫn lưu chuỗi cũ "SYS_39_InventoryCountSessionClosed", khiến EF Core không map được
            // sang enum hiện tại và ném lỗi khi đọc danh sách thông báo. Cập nhật lại dữ liệu cũ cho khớp.
            migrationBuilder.Sql(@"
                UPDATE [Notifications]
                SET [Type] = 'SYS_41_InventoryCountSessionClosed'
                WHERE [Type] = 'SYS_39_InventoryCountSessionClosed'
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only migration: không thể phân biệt dòng đã backfill với dòng đã có giá trị mới
            // từ trước -> Down() không revert dữ liệu (tương tự các migration data khác trong repo).
        }
    }
}
