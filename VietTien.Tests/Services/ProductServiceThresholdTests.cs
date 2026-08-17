using FluentAssertions;
using Moq;
using VietTien.API.DTOs.Product;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Product.ReorderThreshold/ExcessThreshold (ngưỡng cảnh báo tồn thấp/tồn đọng, CEO cấu hình ở
    /// trang Quản lý sản phẩm) — trước đây không có nơi nào set (dead field trên Inventory), nay chuyển
    /// lên Product và phải được persist đúng qua Create/Update.
    /// </summary>
    public class ProductServiceThresholdTests
    {
        private static (ProductService sut, VietTien.API.Data.ApplicationDbContext db) CreateSut()
        {
            var db = TestDbFactory.Create();
            var sut = new ProductService(new UnitOfWork(db), new Mock<ICloudinaryService>().Object, db);
            return (sut, db);
        }

        [Fact]
        public async Task CreateProductAsync_PersistsReorderAndExcessThreshold()
        {
            var (sut, db) = CreateSut();
            var cat = TestData.Category();
            db.Categories.Add(cat);
            db.SaveChanges();

            var dto = new CreateProductDto
            {
                Name = "Băng dính OPP",
                Sku = "SKU-BD-001",
                StandardListedPrice = 50_000m,
                CategoryId = cat.Id,
                Unit = "Cuộn",
                ReorderThreshold = 20,
                ExcessThreshold = 500
            };

            var created = await sut.CreateProductAsync(dto);
            var managed = await sut.GetProductsForManagementAsync();
            var item = managed.Items.Single(i => i.Id == created.Id);

            item.ReorderThreshold.Should().Be(20);
            item.ExcessThreshold.Should().Be(500);
        }

        [Fact]
        public async Task CreateProductAsync_NullThresholds_MeansNotConfigured()
        {
            var (sut, db) = CreateSut();
            var cat = TestData.Category();
            db.Categories.Add(cat);
            db.SaveChanges();

            var dto = new CreateProductDto
            {
                Name = "Ống nhựa",
                Sku = "SKU-ON-001",
                StandardListedPrice = 30_000m,
                CategoryId = cat.Id,
                Unit = "Cây",
            };

            var created = await sut.CreateProductAsync(dto);
            var managed = await sut.GetProductsForManagementAsync();
            var item = managed.Items.Single(i => i.Id == created.Id);

            item.ReorderThreshold.Should().BeNull();
            item.ExcessThreshold.Should().BeNull();
        }

        [Fact]
        public async Task UpdateProductAsync_PersistsReorderAndExcessThreshold()
        {
            var (sut, db) = CreateSut();
            var cat = TestData.Category();
            db.Categories.Add(cat);
            var product = TestData.SeedProduct(db, p => { p.CategoryId = cat.Id; });

            var dto = new UpdateProductDto
            {
                Name = product.Name,
                Sku = product.Sku,
                StandardListedPrice = product.StandardListedPrice,
                CategoryId = cat.Id,
                Unit = product.Unit,
                ReorderThreshold = 15,
                ExcessThreshold = 200
            };

            await sut.UpdateProductAsync(product.Id, dto);
            var managed = await sut.GetProductsForManagementAsync();
            var item = managed.Items.Single(i => i.Id == product.Id);

            item.ReorderThreshold.Should().Be(15);
            item.ExcessThreshold.Should().Be(200);
        }
    }
}
