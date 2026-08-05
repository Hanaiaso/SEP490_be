using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.ScheduledJobs
{
    /// <summary>
    /// QuotationExpiryJob: báo giá CEO đã duyệt (Status=Approved) quá ValidUntil mà khách chưa
    /// quyết định -> tự chuyển Expired và báo Sale phụ trách + khách hàng.
    /// </summary>
    public class QuotationExpiryJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _notification = new();
        private readonly QuotationExpiryJob _sut;
        private readonly User _salesStaff;
        private readonly (User user, CustomerProfile profile) _customer;

        public QuotationExpiryJobTests()
        {
            _sut = new QuotationExpiryJob(_db, _notification.Object, NullLogger<QuotationExpiryJob>.Instance);

            _salesStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(_salesStaff);
            _db.SaveChanges();
            _customer = TestData.SeedCustomer(_db);
        }

        private Quotation SeedApprovedQuotation(DateTime validUntil)
        {
            var q = new Quotation
            {
                CustomerProfileId = _customer.profile.Id,
                SalesStaffId = _salesStaff.Id,
                Status = QuotationStatus.Approved,
                OriginalTotal = 120_000_000m,
                ValidUntil = validUntil
            };
            _db.Quotations.Add(q);

            var version = new QuotationVersion
            {
                QuotationId = q.Id,
                VersionNumber = 1,
                ProposedTotal = 110_000_000m,
                Status = QuotationVersionStatus.CeoApproved,
                CreatedByUserId = _salesStaff.Id,
                ValidUntil = validUntil
            };
            _db.Set<QuotationVersion>().Add(version);
            _db.SaveChanges();
            return q;
        }

        [Fact]
        public async Task ExpiredApprovedQuotation_MarkedExpired_AndNotifiesSaleAndCustomer()
        {
            var q = SeedApprovedQuotation(DateTime.UtcNow.AddDays(-1));

            var processed = await _sut.RunAsync(CancellationToken.None);

            processed.Should().Be(1);
            _db.Quotations.Single(x => x.Id == q.Id).Status.Should().Be(QuotationStatus.Expired);
            _db.Set<QuotationVersion>().Single(v => v.QuotationId == q.Id).Status.Should().Be(QuotationVersionStatus.Expired);

            _notification.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_28_QuotationExpired, _salesStaff.Id,
                It.IsAny<string>(), It.IsAny<string>(), q.Id, "Quotation"), Times.Once());
            _notification.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_28_QuotationExpired, _customer.user.Id,
                It.IsAny<string>(), It.IsAny<string>(), q.Id, "Quotation"), Times.Once());
        }

        [Fact]
        public async Task StillValidQuotation_NotTouched()
        {
            var q = SeedApprovedQuotation(DateTime.UtcNow.AddDays(3));

            var processed = await _sut.RunAsync(CancellationToken.None);

            processed.Should().Be(0);
            _db.Quotations.Single(x => x.Id == q.Id).Status.Should().Be(QuotationStatus.Approved);
            _notification.Verify(n => n.CreateNotificationAsync(
                It.IsAny<NotificationType>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()),
                Times.Never());
        }
    }
}
