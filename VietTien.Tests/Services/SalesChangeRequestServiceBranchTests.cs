using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
    /// Sheet: SalesChangeRequestService — bổ sung các nhánh chưa phủ (branch 27,7% trước đợt này).
    ///
    /// `SalesChangeRequestServiceTests` cũ chỉ có 7 case đi đường thẳng của LUỒNG 7. Phần lớn ngã rẽ
    /// nằm ở `ApproveAsync` (141 dòng) — nơi Manager phải quyết định giữ/chuyển từng đơn đang chạy,
    /// và ở các guard validate của `CreateAsync`. Đây là những nhánh bảo vệ dữ liệu thật:
    /// chuyển nhầm chủ sở hữu khách hàng là mất dấu trách nhiệm doanh số.
    /// </summary>
    public class SalesChangeRequestServiceBranchTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<ICloudinaryService> _cloudinary = new();
        private readonly Mock<INotificationService> _notifications = new();
        private readonly SalesChangeRequestService _sut;

        private readonly User _customer;
        private readonly CustomerProfile _profile;
        private readonly User _s1;      // Sale hiện tại
        private readonly User _s2;      // Sale mới hợp lệ
        private readonly User _manager;

        public SalesChangeRequestServiceBranchTests()
        {
            _sut = new SalesChangeRequestService(
                _db,
                new Mock<ILogger<SalesChangeRequestService>>().Object,
                MockHubContext.Create<SalesHub>().Object,
                _cloudinary.Object,
                _notifications.Object);

            _s1 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _s2 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.AddRange(_s1, _s2, _manager);
            (_customer, _profile) = TestData.SeedCustomer(_db);
            _profile.AssignedSalesStaffId = _s1.Id;
            _db.SaveChanges();
        }

        private Task<Guid> CreateRequest(Action<CreateSalesChangeRequestDto>? mutate = null)
        {
            var dto = new CreateSalesChangeRequestDto
            {
                Reason = "Sale phản hồi chậm",
                ProblemDescription = "Không liên lạc được nhiều ngày"
            };
            mutate?.Invoke(dto);
            return _sut.CreateAsync(_customer.Id, dto);
        }

        private Order SeedOrder(OrderStatus status, DeliveryStatus delivery = DeliveryStatus.NotScheduled)
        {
            var order = TestData.Order(_profile.Id, o =>
            {
                o.OrderStatus = status;
                o.DeliveryStatus = delivery;
                o.SalesStaffId = _s1.Id;
            });
            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        private static IFormFile FakeFile()
        {
            var f = new Mock<IFormFile>();
            f.SetupGet(x => x.Length).Returns(64);
            f.SetupGet(x => x.FileName).Returns("bang-chung.jpg");
            return f.Object;
        }

        // ═══════════════════════════════════════════════════════════════════
        // CreateAsync — các guard chưa phủ
        // ═══════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("", "co mo ta")]
        [InlineData("   ", "co mo ta")]
        [InlineData("co ly do", "")]
        [InlineData("co ly do", "   ")]
        public async Task Create_WhenReasonOrDescriptionBlank_Rejected(string reason, string description)
        {
            var act = () => _sut.CreateAsync(_customer.Id, new CreateSalesChangeRequestDto
            {
                Reason = reason,
                ProblemDescription = description
            });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.SalesChangeRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_WhenCallerHasNoCustomerProfile_Rejected()
        {
            var stranger = TestData.User();
            _db.Users.Add(stranger);
            await _db.SaveChangesAsync();

            var act = () => _sut.CreateAsync(stranger.Id, new CreateSalesChangeRequestDto
            {
                Reason = "x",
                ProblemDescription = "y"
            });

            await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*hồ sơ khách hàng*");
        }

        [Fact]
        public async Task Create_WhenCustomerHasNoAssignedSale_Rejected()
        {
            _profile.AssignedSalesStaffId = null;
            await _db.SaveChangesAsync();

            var act = () => CreateRequest();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*chưa được gán Sale phụ trách*");
        }

        [Fact]
        public async Task Create_WhenDesiredSaleIsCurrentSale_Rejected()
        {
            var act = () => CreateRequest(d => d.DesiredSalesStaffId = _s1.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*phải khác Sale hiện tại*");
        }

        [Fact]
        public async Task Create_WhenDesiredSaleIsNotSalesStaff_Rejected()
        {
            var act = () => CreateRequest(d => d.DesiredSalesStaffId = _manager.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*không hợp lệ*");
        }

        [Fact]
        public async Task Create_WithValidDesiredSale_StoresPreference()
        {
            var requestId = await CreateRequest(d => d.DesiredSalesStaffId = _s2.Id);

            _db.SalesChangeRequests.Single(r => r.Id == requestId)
                .DesiredSalesStaffId.Should().Be(_s2.Id);
        }

        [Fact]
        public async Task Create_WhenTooManyEvidenceFiles_Rejected()
        {
            var act = () => CreateRequest(d => d.Files = Enumerable.Range(0, 6).Select(_ => FakeFile()).ToList());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tối đa 5*");
            _cloudinary.Verify(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), It.IsAny<string>()), Times.Never,
                "phải chặn trước khi tốn lượt upload nào");
        }

        [Fact]
        public async Task Create_WithEvidenceFiles_UploadsEachAndStoresUrlsAsJson()
        {
            _cloudinary.Setup(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), "sales-change-evidence"))
                .ReturnsAsync("https://cdn/bc.jpg");

            var requestId = await CreateRequest(d => d.Files = new List<IFormFile> { FakeFile(), FakeFile() });

            _cloudinary.Verify(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), "sales-change-evidence"),
                Times.Exactly(2));
            _db.SalesChangeRequests.Single(r => r.Id == requestId).EvidenceUrls
                .Should().Contain("https://cdn/bc.jpg").And.StartWith("[");
        }

        [Fact]
        public async Task Create_WhenNotificationServiceFails_RequestIsStillCreated()
        {
            _notifications.Setup(n => n.CreateRoleNotificationAsync(It.IsAny<NotificationType>(),
                    It.IsAny<SystemRole>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Guid?>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("mat ket noi"));

            var requestId = await CreateRequest();

            _db.SalesChangeRequests.Should().ContainSingle().Which.Id.Should().Be(requestId,
                "lỗi gửi thông báo không được làm hỏng việc tạo yêu cầu vốn đã lưu thành công");
        }

        // ═══════════════════════════════════════════════════════════════════
        // ApproveAsync — phần nhiều nhánh nhất
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Approve_WhenRequestMissing_Rejected()
        {
            var act = () => _sut.ApproveAsync(_manager.Id, Guid.NewGuid(),
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s2.Id });

            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        [Fact]
        public async Task Approve_WhenRequestAlreadyProcessed_Rejected()
        {
            var requestId = await CreateRequest();
            await _sut.RejectAsync(_manager.Id, requestId, "khong du can cu");

            var act = () => _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s2.Id });

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*đã được xử lý*");
        }

        [Fact]
        public async Task Approve_WhenNewSaleIsNotSalesStaff_Rejected()
        {
            var requestId = await CreateRequest();

            var act = () => _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _manager.Id });

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*không hợp lệ*");
        }

        [Fact]
        public async Task Approve_WhenNewSaleSameAsCurrent_Rejected()
        {
            var requestId = await CreateRequest();

            var act = () => _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s1.Id });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*phải khác Sale hiện tại*");
        }

        [Fact]
        public async Task Approve_WhenPickingDifferentSaleThanCustomerWanted_RequiresOverrideReason()
        {
            var s3 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(s3);
            await _db.SaveChangesAsync();
            var requestId = await CreateRequest(d => d.DesiredSalesStaffId = _s2.Id);

            var act = () => _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = s3.Id });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*bắt buộc phải ghi lý do*");
        }

        [Fact]
        public async Task Approve_WithOverrideReason_AllowsDifferentSaleAndRecordsReason()
        {
            var s3 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(s3);
            await _db.SaveChangesAsync();
            var requestId = await CreateRequest(d => d.DesiredSalesStaffId = _s2.Id);

            await _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = s3.Id,
                OverrideReason = "Sale khách chọn đang quá tải"
            });

            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.NewSalesStaffId.Should().Be(s3.Id);
            request.OverrideReason.Should().Be("Sale khách chọn đang quá tải");
            _db.CustomerAssignmentHistories.Single().Note
                .Should().Contain("Sale khách chọn đang quá tải",
                    "lý do chọn khác mong muốn phải vào được audit trail");
        }

        [Fact]
        public async Task Approve_WhenDecisionListHasDuplicateOrder_Rejected()
        {
            var order = SeedOrder(OrderStatus.PendingConfirmation);
            var requestId = await CreateRequest();

            var act = () => _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>
                {
                    new() { OrderId = order.Id, TransferToNewSale = true },
                    new() { OrderId = order.Id, TransferToNewSale = false }
                }
            });

            // ⚠ PHÁT HIỆN: guard `if (decisionByOrderId.Count != dto.OrderDecisions.Count)
            //   throw new InvalidOperationException("Danh sách quyết định có đơn hàng bị trùng.")`
            // là DEAD CODE — `ToDictionary` ở dòng ngay trước đã ném ArgumentException khi trùng key.
            // Khách nhận được thông điệp .NET thô thay vì câu tiếng Việt đã soạn sẵn.
            // Test khẳng định hành vi THẬT; xem GH-16 trong manifest.
            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*same key*");
        }

        [Fact]
        public async Task Approve_WhenDecisionsDoNotCoverAllRunningOrders_Rejected()
        {
            SeedOrder(OrderStatus.PendingConfirmation);
            SeedOrder(OrderStatus.Confirmed);
            var requestId = await CreateRequest();

            var act = () => _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>()   // bỏ trống trong khi có 2 đơn đang chạy
            });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*bao phủ đúng toàn bộ đơn đang chạy*");
        }

        [Fact]
        public async Task Approve_ClosedOrdersAreNotConsideredRunning()
        {
            SeedOrder(OrderStatus.Completed);
            SeedOrder(OrderStatus.Cancelled);
            SeedOrder(OrderStatus.Draft);
            var requestId = await CreateRequest();

            // Không truyền quyết định nào mà vẫn qua được => 3 đơn trên không tính là "đang chạy".
            await _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s2.Id });

            _db.CustomerProfiles.Single(p => p.Id == _profile.Id)
                .AssignedSalesStaffId.Should().Be(_s2.Id);
        }

        [Fact]
        public async Task Approve_TransferDecision_MovesOrderToNewSale()
        {
            var order = SeedOrder(OrderStatus.Confirmed);
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>
                {
                    new() { OrderId = order.Id, TransferToNewSale = true }
                }
            });

            _db.Orders.Single(o => o.Id == order.Id).SalesStaffId.Should().Be(_s2.Id);
            _db.SalesChangeRequestOrderDecisions.Single().TransferToNewSale.Should().BeTrue();
        }

        [Fact]
        public async Task Approve_KeepDecision_LeavesOrderWithOldSale()
        {
            var order = SeedOrder(OrderStatus.Confirmed);
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>
                {
                    new() { OrderId = order.Id, TransferToNewSale = false, Note = "Sale cũ hoàn tất nốt" }
                }
            });

            _db.Orders.Single(o => o.Id == order.Id).SalesStaffId.Should().Be(_s1.Id,
                "đơn giữ lại phải nguyên chủ cũ dù khách đã đổi Sale phụ trách");
            _db.CustomerProfiles.Single(p => p.Id == _profile.Id)
                .AssignedSalesStaffId.Should().Be(_s2.Id);
        }

        [Fact]
        public async Task Approve_TransferringInDeliveryOrderWithoutNote_Rejected()
        {
            var order = SeedOrder(OrderStatus.Confirmed, DeliveryStatus.InDelivery);
            var requestId = await CreateRequest();

            var act = () => _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>
                {
                    new() { OrderId = order.Id, TransferToNewSale = true }
                }
            });

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*đang giao*");
            _db.Orders.Single(o => o.Id == order.Id).SalesStaffId.Should().Be(_s1.Id);
        }

        [Fact]
        public async Task Approve_TransferringInDeliveryOrderWithNote_Allowed()
        {
            var order = SeedOrder(OrderStatus.Confirmed, DeliveryStatus.InDelivery);
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>
                {
                    new() { OrderId = order.Id, TransferToNewSale = true, Note = "Sale cũ nghỉ việc đột xuất" }
                }
            });

            _db.Orders.Single(o => o.Id == order.Id).SalesStaffId.Should().Be(_s2.Id);
            _db.SalesChangeRequestOrderDecisions.Single().Note.Should().Be("Sale cũ nghỉ việc đột xuất");
        }

        [Fact]
        public async Task Approve_KeepingInDeliveryOrderNeedsNoNote()
        {
            var order = SeedOrder(OrderStatus.Confirmed, DeliveryStatus.InDelivery);
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId, new ApproveSalesChangeRequestDto
            {
                NewSalesStaffId = _s2.Id,
                OrderDecisions = new List<OrderDecisionDto>
                {
                    new() { OrderId = order.Id, TransferToNewSale = false }
                }
            });

            _db.Orders.Single(o => o.Id == order.Id).SalesStaffId.Should().Be(_s1.Id,
                "giữ nguyên là mặc định an toàn nên không cần lý do");
        }

        [Fact]
        public async Task Approve_WritesAssignmentHistoryWithBeforeAndAfter()
        {
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s2.Id });

            var history = _db.CustomerAssignmentHistories.Should().ContainSingle().Subject;
            history.PreviousSalesStaffId.Should().Be(_s1.Id);
            history.SalesStaffId.Should().Be(_s2.Id);
            history.AssignedById.Should().Be(_manager.Id);
            history.Source.Should().Be(AssignmentSource.ManualReassignment);
        }

        [Fact]
        public async Task Approve_WhenNotificationServiceFails_ApprovalStillStands()
        {
            _notifications.Setup(n => n.CreateNotificationAsync(It.IsAny<NotificationType>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("mat ket noi"));
            var requestId = await CreateRequest();

            await _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s2.Id });

            _db.SalesChangeRequests.Single(r => r.Id == requestId)
                .Status.Should().Be(SalesChangeRequestStatus.Approved,
                    "đã commit rồi thì lỗi gửi thông báo không được đảo ngược quyết định");
        }

        // ═══════════════════════════════════════════════════════════════════
        // RejectAsync
        // ═══════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Reject_WithoutReason_Rejected(string note)
        {
            var requestId = await CreateRequest();

            var act = () => _sut.RejectAsync(_manager.Id, requestId, note);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bắt buộc phải có lý do*");
            _db.SalesChangeRequests.Single(r => r.Id == requestId)
                .Status.Should().Be(SalesChangeRequestStatus.Pending);
        }

        [Fact]
        public async Task Reject_WhenRequestMissing_Rejected()
        {
            var act = () => _sut.RejectAsync(_manager.Id, Guid.NewGuid(), "ly do");

            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        [Fact]
        public async Task Reject_WhenAlreadyApproved_Rejected()
        {
            var requestId = await CreateRequest();
            await _sut.ApproveAsync(_manager.Id, requestId,
                new ApproveSalesChangeRequestDto { NewSalesStaffId = _s2.Id });

            var act = () => _sut.RejectAsync(_manager.Id, requestId, "doi y");

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*đã được xử lý*");
        }

        [Fact]
        public async Task Reject_TrimsNoteAndKeepsAssignmentUnchanged()
        {
            var requestId = await CreateRequest();

            await _sut.RejectAsync(_manager.Id, requestId, "   khong du can cu   ");

            var request = _db.SalesChangeRequests.Single(r => r.Id == requestId);
            request.ManagerNote.Should().Be("khong du can cu");
            request.ReviewedByUserId.Should().Be(_manager.Id);
            _db.CustomerProfiles.Single(p => p.Id == _profile.Id)
                .AssignedSalesStaffId.Should().Be(_s1.Id, "từ chối thì không được đổi Sale phụ trách");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Gate bảo vệ khách hàng: Sale chỉ thấy khiếu nại sau khi Manager mở
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetAboutMe_HidesRequestUntilManagerOpensExplanationGate()
        {
            await CreateRequest();

            var beforeGate = await _sut.GetAboutMeAsync(_s1.Id);

            beforeGate.Should().BeEmpty(
                "Sale bị khiếu nại không được biết cho tới khi Manager bấm Yêu cầu giải trình");
        }

        [Fact]
        public async Task GetAboutMe_ShowsRequestAfterGateOpened()
        {
            var requestId = await CreateRequest();
            await _sut.RequestExplanationAsync(_manager.Id, requestId);

            var afterGate = await _sut.GetAboutMeAsync(_s1.Id);

            afterGate.Should().ContainSingle().Which.Id.Should().Be(requestId);
        }

        [Fact]
        public async Task SubmitExplanation_BeforeGateOpened_Forbidden()
        {
            var requestId = await CreateRequest();

            var act = () => _sut.SubmitExplanationAsync(_s1.Id, requestId,
                new SaleExplanationDto { Explanation = "toi da goi lai roi" });

            // Service ném UnauthorizedAccessException (không phải InvalidOperationException) nên qua
            // controller sẽ ra 403 chứ không phải 400 — ghi rõ để không hiểu nhầm khi đọc case L1-SCR-26.
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*chưa được quản lý mở giải trình*");
        }

        [Fact]
        public async Task SubmitExplanation_ByAnotherSale_Forbidden()
        {
            var requestId = await CreateRequest();
            await _sut.RequestExplanationAsync(_manager.Id, requestId);

            var act = () => _sut.SubmitExplanationAsync(_s2.Id, requestId,
                new SaleExplanationDto { Explanation = "khong lien quan toi toi" });

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }
}
