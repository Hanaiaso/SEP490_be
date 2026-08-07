using FluentAssertions;
using Moq;
using VietTien.API.DTOs.Review;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: ReviewService — L1-REV-01..10. EF InMemory.</summary>
    public class ReviewServiceTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly ReviewService _sut;

        public ReviewServiceTests() => _sut = new ReviewService(_db, new Mock<INotificationService>().Object);

        private (User user, CustomerProfile profile, Product product) SeedCustomerAndProduct()
        {
            var (user, profile) = TestData.SeedCustomer(_db);
            var product = TestData.SeedProduct(_db);
            return (user, profile, product);
        }

        private Order SeedCompletedOrderWithProduct(Guid customerProfileId, Guid productId, Guid? salesStaffId = null)
        {
            var order = TestData.Order(customerProfileId, o =>
            {
                o.OrderStatus = OrderStatus.Completed;
                o.SalesStaffId = salesStaffId;
            });
            _db.Orders.Add(order);
            _db.OrderItems.Add(TestData.OrderItem(order.Id, productId));
            _db.SaveChanges();
            return order;
        }

        // L1-REV-01 | EP-Invalid | Chưa có đơn Completed chứa sản phẩm -> không được tạo đánh giá
        [Fact]
        public async Task L1_REV_01_Create_WithoutCompletedOrder_Rejected()
        {
            var (user, _, product) = SeedCustomerAndProduct();

            var act = () => _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 5, Comment = "Tốt" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ProductReviews.Count().Should().Be(0);
        }

        // L1-REV-02 | EP-Valid | Đã có đơn Completed chứa sản phẩm -> tạo đánh giá thành công, Product cập nhật rating
        [Fact]
        public async Task L1_REV_02_Create_WithCompletedOrder_PersistedAndProductRatingUpdated()
        {
            var (user, profile, product) = SeedCustomerAndProduct();
            SeedCompletedOrderWithProduct(profile.Id, product.Id);

            var dto = await _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 4, Comment = "Hàng tốt, giao nhanh" });

            dto.Rating.Should().Be(4);
            dto.CustomerName.Should().Be(user.FullName);
            _db.ProductReviews.Count().Should().Be(1);

            var updatedProduct = _db.Products.First(p => p.Id == product.Id);
            updatedProduct.ReviewCount.Should().Be(1);
            updatedProduct.AverageRating.Should().Be(4);
        }

        // L1-REV-03 | EP-Invalid | Đã đánh giá sản phẩm rồi -> chặn tạo đánh giá thứ 2
        [Fact]
        public async Task L1_REV_03_Create_Duplicate_Rejected()
        {
            var (user, profile, product) = SeedCustomerAndProduct();
            SeedCompletedOrderWithProduct(profile.Id, product.Id);
            await _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 5, Comment = "Rất tốt" });

            var act = () => _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 3, Comment = "Đánh giá lại" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ProductReviews.Count().Should().Be(1);
        }

        // L1-REV-04 | EP-Invalid | Sửa đánh giá của người khác -> UnauthorizedAccessException
        [Fact]
        public async Task L1_REV_04_Update_NotOwner_Unauthorized()
        {
            var (user, profile, product) = SeedCustomerAndProduct();
            SeedCompletedOrderWithProduct(profile.Id, product.Id);
            var review = await _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 5, Comment = "Tốt" });

            var (otherUser, _) = TestData.SeedCustomer(_db);
            var act = () => _sut.UpdateReviewAsync(review.Id, otherUser.Id, new UpdateReviewRequest { Rating = 1, Comment = "Sửa trộm" });

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // L1-REV-05 | EP-Valid | Chủ sở hữu sửa đánh giá -> cập nhật đúng, Product rating tính lại
        [Fact]
        public async Task L1_REV_05_Update_Owner_UpdatesRatingAndProduct()
        {
            var (user, profile, product) = SeedCustomerAndProduct();
            SeedCompletedOrderWithProduct(profile.Id, product.Id);
            var review = await _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 2, Comment = "Tạm được" });

            var updated = await _sut.UpdateReviewAsync(review.Id, user.Id, new UpdateReviewRequest { Rating = 5, Comment = "Dùng thêm thấy tốt hơn" });

            updated.Rating.Should().Be(5);
            updated.UpdatedAt.Should().NotBeNull();
            _db.Products.First(p => p.Id == product.Id).AverageRating.Should().Be(5);
        }

        // L1-REV-06 | EP-Valid | Xoá đánh giá -> Product rating/count tính lại đúng (về 0 khi hết đánh giá)
        [Fact]
        public async Task L1_REV_06_Delete_Owner_RecalculatesProductBackToZero()
        {
            var (user, profile, product) = SeedCustomerAndProduct();
            SeedCompletedOrderWithProduct(profile.Id, product.Id);
            var review = await _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 3, Comment = "Ổn" });

            await _sut.DeleteReviewAsync(review.Id, user.Id);

            _db.ProductReviews.Count().Should().Be(0);
            var updatedProduct = _db.Products.First(p => p.Id == product.Id);
            updatedProduct.ReviewCount.Should().Be(0);
            updatedProduct.AverageRating.Should().Be(0);
        }

        // L1-REV-07 | EP-Valid | GetEligibilityAsync phản ánh đúng 3 trạng thái: chưa mua / được đánh giá / đã đánh giá
        [Fact]
        public async Task L1_REV_07_GetEligibility_ReflectsPurchaseAndExistingReviewState()
        {
            var (user, profile, product) = SeedCustomerAndProduct();

            var beforePurchase = await _sut.GetEligibilityAsync(product.Id, user.Id);
            beforePurchase.CanReview.Should().BeFalse();
            beforePurchase.AlreadyReviewed.Should().BeFalse();

            SeedCompletedOrderWithProduct(profile.Id, product.Id);
            var afterPurchase = await _sut.GetEligibilityAsync(product.Id, user.Id);
            afterPurchase.CanReview.Should().BeTrue();

            var review = await _sut.CreateReviewAsync(product.Id, user.Id, new CreateReviewRequest { Rating = 4, Comment = "OK" });
            var afterReview = await _sut.GetEligibilityAsync(product.Id, user.Id);
            afterReview.CanReview.Should().BeFalse();
            afterReview.AlreadyReviewed.Should().BeTrue();
            afterReview.ExistingReviewId.Should().Be(review.Id);
        }

        private User SeedStaffUser(SystemRole role)
        {
            var user = TestData.User(u => u.Role = role);
            _db.Users.Add(user);
            _db.SaveChanges();
            return user;
        }

        // L1-REV-08 | EP-Valid | SalesStaff phụ trách đúng khách -> phản hồi thành công
        [Fact]
        public async Task L1_REV_08_Reply_OwnCustomer_Succeeds()
        {
            var (customer, profile, product) = SeedCustomerAndProduct();
            var salesStaff = SeedStaffUser(SystemRole.SalesStaff);
            SeedCompletedOrderWithProduct(profile.Id, product.Id, salesStaffId: salesStaff.Id);
            var review = await _sut.CreateReviewAsync(product.Id, customer.Id, new CreateReviewRequest { Rating = 5, Comment = "Tốt" });

            var replied = await _sut.ReplyToReviewAsync(review.Id, salesStaff.Id, "SalesStaff", new ReplyReviewRequest { ReplyText = "Cảm ơn bạn đã ủng hộ!" });

            replied.ReplyText.Should().Be("Cảm ơn bạn đã ủng hộ!");
            replied.RepliedByName.Should().Be(salesStaff.FullName);
            _db.ProductReviews.First(r => r.Id == review.Id).RepliedByUserId.Should().Be(salesStaff.Id);
        }

        // L1-REV-09 | EP-Invalid | SalesStaff không phụ trách khách này -> UnauthorizedAccessException
        [Fact]
        public async Task L1_REV_09_Reply_NotOwnCustomer_Unauthorized()
        {
            var (customer, profile, product) = SeedCustomerAndProduct();
            var assignedSales = SeedStaffUser(SystemRole.SalesStaff);
            var otherSales = SeedStaffUser(SystemRole.SalesStaff);
            SeedCompletedOrderWithProduct(profile.Id, product.Id, salesStaffId: assignedSales.Id);
            var review = await _sut.CreateReviewAsync(product.Id, customer.Id, new CreateReviewRequest { Rating = 5, Comment = "Tốt" });

            var act = () => _sut.ReplyToReviewAsync(review.Id, otherSales.Id, "SalesStaff", new ReplyReviewRequest { ReplyText = "Xin chào" });

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            _db.ProductReviews.First(r => r.Id == review.Id).ReplyText.Should().BeNull();
        }

        // L1-REV-10 | EP-Valid | Admin phản hồi được bất kỳ đánh giá nào (không bị scope theo Sales phụ trách)
        [Fact]
        public async Task L1_REV_10_Reply_Admin_BypassesScopeAndSucceeds()
        {
            var (customer, profile, product) = SeedCustomerAndProduct();
            var assignedSales = SeedStaffUser(SystemRole.SalesStaff);
            var admin = SeedStaffUser(SystemRole.Admin);
            SeedCompletedOrderWithProduct(profile.Id, product.Id, salesStaffId: assignedSales.Id);
            var review = await _sut.CreateReviewAsync(product.Id, customer.Id, new CreateReviewRequest { Rating = 5, Comment = "Tốt" });

            var replied = await _sut.ReplyToReviewAsync(review.Id, admin.Id, "Admin", new ReplyReviewRequest { ReplyText = "Việt Tiến xin cảm ơn!" });

            replied.ReplyText.Should().Be("Việt Tiến xin cảm ơn!");
        }

        // L1-REV-11 | EP-Valid | GetReviewsForSalesAsync: SalesStaff chỉ thấy đánh giá của khách mình phụ trách
        [Fact]
        public async Task L1_REV_11_GetReviewsForSales_ScopesToOwnCustomersForSalesStaff()
        {
            var (customerA, profileA, productA) = SeedCustomerAndProduct();
            var (customerB, profileB, productB) = SeedCustomerAndProduct();
            var salesA = SeedStaffUser(SystemRole.SalesStaff);
            var salesB = SeedStaffUser(SystemRole.SalesStaff);
            SeedCompletedOrderWithProduct(profileA.Id, productA.Id, salesStaffId: salesA.Id);
            SeedCompletedOrderWithProduct(profileB.Id, productB.Id, salesStaffId: salesB.Id);
            await _sut.CreateReviewAsync(productA.Id, customerA.Id, new CreateReviewRequest { Rating = 5, Comment = "A" });
            await _sut.CreateReviewAsync(productB.Id, customerB.Id, new CreateReviewRequest { Rating = 4, Comment = "B" });

            var forSalesA = await _sut.GetReviewsForSalesAsync(salesA.Id, "SalesStaff");
            var forAdmin = await _sut.GetReviewsForSalesAsync(Guid.NewGuid(), "Admin");

            forSalesA.Should().ContainSingle().Which.Comment.Should().Be("A");
            forAdmin.Should().HaveCount(2);
        }
    }
}
