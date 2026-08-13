using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogInsertOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L3-SEC-13: ApplicationDbContext.SaveChangesAsync đã chặn Update/Delete AuditLog ở tầng
            // EF ChangeTracker, nhưng chỉ có tác dụng với request đi qua đúng code path đó — SQL thô
            // (SSMS, ExecuteSqlRaw, hay bất kỳ công cụ nào dùng cùng SQL login của app) vẫn UPDATE/DELETE
            // được bình thường. Trigger này ép bất biến ở tầng database, không phụ thuộc caller là ai.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_AuditLogs_PreventUpdateDelete
ON dbo.AuditLogs
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR(N'AuditLog là bất biến: không được phép UPDATE hoặc DELETE bản ghi kiểm toán.', 16, 1);
    ROLLBACK TRANSACTION;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_AuditLogs_PreventUpdateDelete;");
        }
    }
}
