using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.SalesChange;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: SalesChangeRequestService — L1-SCR-01..07. EF InMemory + mock logger/hub/cloudinary.
    /// Ghi chú map trạng thái thực tế: 'AwaitingExplanation' trong spec = ExplanationRequestedAt được set
    /// (status vẫn Pending); Sale giải trình xong status vẫn Pending trong hàng chờ Manager.
    /// </summary>
    public class SalesChangeRequestServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly SalesChangeRequestService _sut;
        private readonly User _customer;
        private readonly CustomerProfile _profile;
        private readonly User _s1; // Sale hiện tại
        private readonly User _s2; // Sale mới
        private readonly User _manager;

        public SalesChangeRequestServiceTests()
        {
            _sut = new SalesChangeRequestService(
                _db,
                new Mock<ILogger<SalesChangeRequestService>>().Object,
                MockHubContext.Create<SalesHub>().Object,
                new Mock<ICloudinaryService>().Object,
                new Mock<INotificationService>().Object);

            _s1 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _s2 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.AddRange(_s1, _s2, _manager);
            (_customer, _profile) = TestData.SeedCustomer(_db);
            _profile.AssignedSalesStaffId = _s1.Id;
            _db.SaveChanges();
        }

        private async Task<Guid> CreateRequest() =>
            await _sut.CreateAsync(_customer.Id, new CreateSalesChangeRequestDto
            {
                Reason = "Sale phản hồi chậm",
                ProblemDescription = "Không liên lạc được nhiều ngày"
            });

        // L1-SCR-01 | EP-Valid | Khách tạo yêu cầu đổi Sale -> Pending, tham chiếu đúng khách + Sale hiện tại
        [Fact]
        public async Task L1_SCR_01_Create_Valid_PendingPersisted()
        {
            var requestId = await CreateRequest();

            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.Status.Should().Be(SalesChangeRequestStatus.Pending);
            request.CustomerProfileId.Should().Be(_profile.Id);
            request.CurrentSalesStaffId.Should().Be(_s1.Id);
            request.Reason.Should().Be("Sale phản hồi chậm");
        }

        // L1-SCR-02 | EP-Invalid | Khách đang có yêu cầu mở tạo thêm yêu cầu -> từ chối, không lưu gì
        [Fact]
        public async Task L1_SCR_02_Create_WhileOpenRequestExists_Rejected()
        {
            await CreateRequest();

            var act = () => CreateRequest();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Bạn đang có một yêu cầu đổi Sale chưa được xử lý.*");
            _db.SalesChangeRequests.Count().Should().Be(1);
        }

        // L1-SCR-03 | EP-Invalid | Hủy yêu cầu của khách khác -> forbidden, yêu cầu không đổi
        [Fact]
        public async Task L1_SCR_03_Cancel_RequestOfAnotherCustomer_Forbidden()
        {
            var requestId = await CreateRequest();
            var (stranger, _) = TestData.SeedCustomer(_db); // khách khác có profile riêng

            var act = () => _sut.CancelAsync(stranger.Id, requestId);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Bạn không có quyền hủy yêu cầu này.");
            _db.SalesChangeRequests.Single(r => r.Id == requestId).Status.Should().Be(SalesChangeRequestStatus.Pending);
        }

        // L1-SCR-04 | State-Valid | Manager yêu cầu giải trình -> ExplanationRequestedAt/By được ghi nhận
        [Fact]
        public async Task L1_SCR_04_RequestExplanation_GateOpenedForSale()
        {
            var requestId = await CreateRequest();

            await _sut.RequestExplanationAsync(_manager.Id, requestId);

            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.ExplanationRequestedAt.Should().NotBeNull();
            request.ExplanationRequestedByUserId.Should().Be(_manager.Id);
            // Gọi lần 2 bị chặn
            var again = () => _sut.RequestExplanationAsync(_manager.Id, requestId);
            await again.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Đã yêu cầu Sale giải trình cho yêu cầu này rồi.");
        }

        // L1-SCR-05 | State-Valid | Sale phụ trách gửi giải trình (sau khi Manager mở gate) -> nội dung được lưu
        [Fact]
        public async Task L1_SCR_05_SubmitExplanation_StoredForManagerReview()
        {
            var requestId = await CreateRequest();
            await _sut.RequestExplanationAsync(_manager.Id, requestId);

            await _sut.SubmitExplanationAsync(_s1.Id, requestId, new SaleExplanationDto
            {
                Explanation = "Tôi đã liên hệ nhưng khách không nghe máy."
            });

            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.SaleExplanation.Should().Be("Tôi đã liên hệ nhưng khách không nghe máy.");
            request.SaleExplainedAt.Should().NotBeNull();
            request.Status.Should().Be(SalesChangeRequestStatus.Pending); // vẫn trong hàng chờ Manager
        }

        // L1-SCR-06 | State-Valid | Manager phê duyệt -> khách chuyển sang Sale mới, request Approved (terminal)
        [Fact]
        public async Task L1_SCR_06_Approve_CustomerReallocatedToNewSale()
        {
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OverrideReason = "Khách không chỉ định Sale mong muốn",
                OrderDecisions = new List<OrderDecisionDto>() // khách không có đơn đang chạy
            });

            _db.CustomerProfiles.Single(cp => cp.Id == _profile.Id).AssignedSalesStaffId.Should().Be(_s2.Id);
            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.Status.Should().Be(SalesChangeRequestStatus.Approved);
            request.NewSalesStaffId.Should().Be(_s2.Id);
            _db.CustomerAssignmentHistories.Should()
                .Contain(h => h.CustomerProfileId == _profile.Id && h.SalesStaffId == _s2.Id && h.PreviousSalesStaffId == _s1.Id);
        }

        // L1-SCR-07 | State-Valid | Manager từ chối kèm lý do -> Rejected, khách vẫn thuộc Sale cũ
        [Fact]
        public async Task L1_SCR_07_Reject_WithNote_AssignmentUnchanged()
        {
            var requestId = await CreateRequest();

            await _sut.RejectAsync(_manager.Id, requestId, "insufficient grounds");

            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.Status.Should().Be(SalesChangeRequestStatus.Rejected);
            request.ManagerNote.Should().Be("insufficient grounds");
            _db.CustomerProfiles.Single(cp => cp.Id == _profile.Id).AssignedSalesStaffId.Should().Be(_s1.Id);
        }
    }
}
