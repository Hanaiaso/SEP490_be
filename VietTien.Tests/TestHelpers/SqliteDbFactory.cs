using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VietTien.API.Data;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// ApplicationDbContext chạy trên SQLite in-memory — dùng cho các service gọi raw SQL
    /// (<see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlInterpolatedAsync"/>),
    /// thứ mà provider InMemory không hỗ trợ. Ví dụ: InventoryReservationService.
    /// Mọi test khác vẫn dùng <see cref="TestDbFactory"/> (InMemory) cho nhanh.
    ///
    /// Connection phải được GIỮ MỞ suốt vòng đời test: SQLite xoá sạch DB ":memory:" ngay khi
    /// connection cuối cùng đóng lại. Vì vậy factory trả về cả connection để test dispose.
    /// </summary>
    public static class SqliteDbFactory
    {
        public static (ApplicationDbContext db, SqliteConnection connection) Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, SqliteCompatibleModelCustomizer>()
                .EnableSensitiveDataLogging()
                .Options;

            var db = new ApplicationDbContext(options);
            db.Database.EnsureCreated();
            return (db, connection);
        }
    }

    /// <summary>
    /// Gỡ các cấu hình chỉ có trên SQL Server để EnsureCreated() sinh được DDL hợp lệ cho SQLite.
    /// Hiện tại:
    /// - default value NEWSEQUENTIALID() mà ApplicationDbContext gán cho mọi khoá chính Guid
    ///   (SQLite không biết hàm này). Id vẫn có giá trị vì model tự khởi tạo Guid.NewGuid().
    /// - Cột [Timestamp]/rowversion (concurrency token): trên SQL Server được engine tự sinh giá trị
    ///   khi INSERT nên HasData() không cần set — nhưng SQLite không có cơ chế tương đương, nên các
    ///   dòng seed qua HasData() (vd Products) sẽ INSERT giá trị NULL và vi phạm NOT NULL. Gán default
    ///   value SQL randomblob(8) để SQLite tự sinh 8 byte ngẫu nhiên, tương đương rowversion cho mục
    ///   đích test (không cần đúng semantics tăng dần của SQL Server thật).
    /// </summary>
    public class SqliteCompatibleModelCustomizer : RelationalModelCustomizer
    {
        public SqliteCompatibleModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (string.Equals(property.GetDefaultValueSql(), "NEWSEQUENTIALID()", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetDefaultValueSql(null);
                        property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                    }

                    if (property.IsConcurrencyToken && property.ClrType == typeof(byte[]))
                    {
                        property.SetDefaultValueSql("randomblob(8)");
                    }
                }
            }
        }
    }
}
