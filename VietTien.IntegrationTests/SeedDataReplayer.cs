using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure; // AccessorExtensions.GetService<T>()
using Microsoft.EntityFrameworkCore.Metadata;       // IDesignTimeModel
using VietTien.API.Data;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Nạp lại seed data khai báo bằng <c>HasData</c> trong <c>ApplicationDbContext.OnModelCreating</c>
    /// (User GUID cố định, CustomerProfile, Warehouse/Location/Shift, Category, Product, Inventory,
    /// SystemConfig + SystemConfigVersion, Vehicle, DiscountTier, Material).
    ///
    /// Respawn xoá SẠCH mọi bảng, mà SePayReservationExpiryJob đọc SystemConfig và
    /// OrderService.CalculateDiscount đọc DiscountTier — thiếu bước này là test hỏng.
    ///
    /// Đọc thẳng từ model nên tự đồng bộ khi team sửa HasData, không phải chép tay lần hai.
    /// Dùng chung bởi <see cref="SqlServerFixture"/> (L2, Testcontainers) và
    /// <see cref="L3.L3SqlFixture"/> (L3, SQL Server local).
    ///
    /// ⚠ Chỉ đúng khi entity có seed dùng PK gán tường minh (hiện tại toàn bộ là Guid). Nếu về sau
    /// team seed entity có PK int IDENTITY thì phải chuyển sang raw INSERT + SET IDENTITY_INSERT.
    /// </summary>
    internal static class SeedDataReplayer
    {
        public static async Task ReseedAsync(ApplicationDbContext db)
        {
            // KHÔNG dùng db.Model: lúc runtime đó là read-optimized model, đã lược bỏ seed data
            // ("The requested configuration is not stored in the read-optimized model"). Seed chỉ còn
            // trong design-time model.
            var designTimeModel = db.GetService<IDesignTimeModel>().Model;

            foreach (var entityType in designTimeModel.GetEntityTypes())
            {
                var seedRows = entityType.GetSeedData().ToList();
                if (seedRows.Count == 0) continue;

                // GetSeedData() trả về CẢ key của navigation (vd "Category.Products") chứ không chỉ
                // scalar — entry.Property() sẽ ném nếu gặp. Lọc theo danh sách property thật của model.
                var scalarProperties = entityType.GetProperties().Select(p => p.Name).ToHashSet();

                foreach (var row in seedRows)
                {
                    var instance = Activator.CreateInstance(entityType.ClrType, nonPublic: true)!;
                    var entry = db.Entry(instance);

                    foreach (var kv in row)
                    {
                        if (!scalarProperties.Contains(kv.Key)) continue;

                        // Đi qua ChangeTracker (không dùng reflection trực tiếp) để cover cả shadow property.
                        entry.Property(kv.Key).CurrentValue = kv.Value;
                    }

                    entry.State = EntityState.Added;
                }
            }

            // EF tự topological-sort các INSERT theo phụ thuộc FK.
            await db.SaveChangesAsync();
        }
    }
}
