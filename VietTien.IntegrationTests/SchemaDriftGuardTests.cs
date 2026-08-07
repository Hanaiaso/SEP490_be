using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using VietTien.API.Data;
using Xunit;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Chốt chặn chống lặp lại sự cố schema drift.
    ///
    /// Bối cảnh: entity model + ModelSnapshot từng ghi nhận 4 bảng và 14 cột mà **không migration nào
    /// tạo ra**, nên database dựng từ migrations luôn thiếu so với code — 62/109 test L2 chết và
    /// 4 luồng nghiệp vụ (marketing, đổi/trả, xuất kho, nhập kho) không chạy được khi triển khai.
    /// `dotnet ef migrations add` KHÔNG phát hiện được vì nó diff model với snapshot, mà snapshot
    /// chính là thứ đã sai.
    ///
    /// Cách duy nhất bắt được: dựng song song hai database trên cùng container rồi so schema thật.
    ///   • `Database.MigrateAsync()`      -> đúng những gì migrations thật sự tạo ra
    ///   • `Database.EnsureCreatedAsync()` -> đúng những gì entity model cần
    /// Chênh nhau dù chỉ một cột = có người sửa entity mà quên ra migration.
    ///
    /// Test này chạy ~20 giây (phải kéo container). Nếu đỏ, thông điệp đã liệt kê sẵn thứ còn thiếu.
    /// </summary>
    [Trait("Category", "SchemaGuard")]
    public class SchemaDriftGuardTests
    {
        private static string ConnFor(MsSqlContainer c, string db) =>
            new SqlConnectionStringBuilder(c.GetConnectionString()) { InitialCatalog = db }.ConnectionString;

        private static ApplicationDbContext ContextFor(string connectionString) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);

        private static async Task<Dictionary<string, HashSet<string>>> ReadSchemaAsync(string connectionString)
        {
            var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TABLE_NAME, COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME <> '__EFMigrationsHistory';";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                if (!schema.TryGetValue(table, out var cols))
                    schema[table] = cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                cols.Add(reader.GetString(1));
            }
            return schema;
        }

        [Fact]
        public async Task MigrationsMustProduceExactlyTheSchemaTheModelNeeds()
        {
            await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await container.StartAsync();

            await using (var fromMigrations = ContextFor(ConnFor(container, "db_from_migrations")))
                await fromMigrations.Database.MigrateAsync();

            await using (var fromModel = ContextFor(ConnFor(container, "db_from_model")))
                await fromModel.Database.EnsureCreatedAsync();

            var migrated = await ReadSchemaAsync(ConnFor(container, "db_from_migrations"));
            var model = await ReadSchemaAsync(ConnFor(container, "db_from_model"));

            var problems = new List<string>();

            foreach (var table in model.Keys.Where(t => !migrated.ContainsKey(t)).OrderBy(t => t))
                problems.Add($"THIẾU BẢNG: {table} — model cần nhưng chưa migration nào CreateTable");

            foreach (var table in model.Keys.Where(migrated.ContainsKey).OrderBy(t => t))
            {
                var missing = model[table].Except(migrated[table], StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c).ToList();
                if (missing.Count > 0)
                    problems.Add($"THIẾU CỘT: {table}.{{{string.Join(", ", missing)}}}");
            }

            Assert.True(problems.Count == 0,
                "Schema dựng từ migrations KHÁC với schema mà entity model cần. Ai đó sửa entity mà " +
                "chưa ra migration — hãy chạy `dotnet ef migrations add <Tên>` và điền đủ các thao tác " +
                "dưới đây trước khi merge:\n  - " + string.Join("\n  - ", problems));
        }
    }
}
