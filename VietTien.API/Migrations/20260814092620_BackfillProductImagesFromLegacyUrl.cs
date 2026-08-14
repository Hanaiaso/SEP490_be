using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class BackfillProductImagesFromLegacyUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sản phẩm tạo trước khi có bảng ProductImages chỉ có Products.ImageUrl, chưa có dòng
            // ProductImage tương ứng -> gallery (Images) rỗng dù ảnh đại diện vẫn hiển thị bình thường.
            // Backfill 1 dòng ProductImage cho mỗi sản phẩm như vậy để trang quản lý CEO/Admin và
            // gallery chi tiết sản phẩm phản ánh đúng ảnh đang có.
            migrationBuilder.Sql(@"
                INSERT INTO [ProductImages] ([ProductId], [ImageUrl], [SortOrder])
                SELECT p.[Id], p.[ImageUrl], 0
                FROM [Products] p
                WHERE p.[ImageUrl] IS NOT NULL AND p.[ImageUrl] <> ''
                  AND NOT EXISTS (SELECT 1 FROM [ProductImages] pi WHERE pi.[ProductId] = p.[Id])
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only migration: không thể phân biệt dòng đã backfill với dòng người dùng tự thêm
            // sau đó -> Down() không revert dữ liệu (tương tự các migration data khác trong repo).
        }
    }
}
