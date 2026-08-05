using FluentAssertions;
using Moq;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: ProductService — L1-PROD-01..05.
    /// Dùng UnitOfWork thật trên EF InMemory để kiểm tra được cả logic lọc (BR-26 soft delete)
    /// nằm trong ProductRepository.GetAllAsync.
    /// </summary>
    public class ProductServiceTests
    {
        private static (ProductService sut, VietTien.API.Data.ApplicationDbContext db) CreateSut()
        {
            var db = TestDbFactory.Create();
            var sut = new ProductService(new UnitOfWork(db), new Mock<ICloudinaryService>().Object);
            return (sut, db);
        }

        // L1-PROD-01 | EP-Valid | Lọc theo categoryId -> chỉ trả sản phẩm thuộc category đó
        [Fact]
        public async Task L1_PROD_01_GetProducts_FilterByCategory()
        {
            var (sut, db) = CreateSut();
            var c1 = TestData.Category();
            var c2 = TestData.Category(c => c.Name = "Khác");
            db.Categories.AddRange(c1, c2);
            db.Products.AddRange(
                TestData.Product(c1.Id, p => p.Name = "A1"),
                TestData.Product(c1.Id, p => p.Name = "A2"),
                TestData.Product(c2.Id, p => p.Name = "B1"),
                TestData.Product(c2.Id, p => p.Name = "B2"),
                TestData.Product(c2.Id, p => p.Name = "B3"));
            db.SaveChanges();

            var result = await sut.GetProductsAsync(page: 1, pageSize: 12, categoryId: c1.Id);

            result.TotalCount.Should().Be(2);
            result.Items.Should().OnlyContain(p => p.CategoryId == c1.Id);
        }

        // L1-PROD-02 | EP-Valid | searchKeyword khớp tên không phân biệt hoa thường
        [Fact]
        public async Task L1_PROD_02_GetProducts_SearchKeyword_CaseInsensitive()
        {
            var (sut, db) = CreateSut();
            var cat = TestData.Category();
            db.Categories.Add(cat);
            db.Products.AddRange(
                TestData.Product(cat.Id, p => p.Name = "Túi BAG lớn"),
                TestData.Product(cat.Id, p => p.Name = "Ống nhựa"));
            db.SaveChanges();

            var result = await sut.GetProductsAsync(searchKeyword: "bag");

            result.TotalCount.Should().Be(1);
            result.Items.Should().OnlyContain(p => p.Name.ToLower().Contains("bag"));
        }

        // L1-PROD-03 | EP-Invalid | Sản phẩm Discontinued bị ẩn khỏi catalogue (BR-26 soft delete)
        [Fact]
        public async Task L1_PROD_03_GetProducts_DiscontinuedHidden()
        {
            var (sut, db) = CreateSut();
            var cat = TestData.Category();
            db.Categories.Add(cat);
            var active = TestData.Product(cat.Id, p => p.Name = "Đang bán");
            var discontinued = TestData.Product(cat.Id, p => { p.Name = "Ngừng bán"; p.IsDiscontinued = true; });
            db.Products.AddRange(active, discontinued);
            db.SaveChanges();

            var result = await sut.GetProductsAsync(page: 1);

            result.Items.Should().Contain(p => p.Id == active.Id);
            result.Items.Should().NotContain(p => p.Id == discontinued.Id);
        }

        // L1-PROD-04 | BVA-Min-1 | page = 0 -> chuẩn hóa về page 1, không exception
        [Fact]
        public async Task L1_PROD_04_GetProducts_PageZero_NormalizedToOne()
        {
            var (sut, db) = CreateSut();
            var cat = TestData.Category();
            db.Categories.Add(cat);
            db.Products.Add(TestData.Product(cat.Id));
            db.SaveChanges();

            var result = await sut.GetProductsAsync(page: 0);

            result.Page.Should().Be(1);
            result.Items.Should().HaveCount(1); // không lỗi offset âm
        }

        // L1-PROD-05 | EP-Invalid | Id không tồn tại -> trả null (controller map 404), không exception
        [Fact]
        public async Task L1_PROD_05_GetProductById_Unknown_ReturnsNull()
        {
            var (sut, _) = CreateSut();

            var result = await sut.GetProductByIdAsync(Guid.NewGuid());

            result.Should().BeNull();
        }
    }
}
