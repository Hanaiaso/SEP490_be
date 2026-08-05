using FluentAssertions;
using Moq;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: QuotationService — L1-QUO-01..17.
    /// Trạng thái thực tế của code (khác nhãn trong spec): Draft ~ 'Requested', Negotiating ~ 'Under Sales Review',
    /// PendingManager/PendingCeo ~ 'Waiting Admin Approval', Approved ~ 'Proposed to Customer'.
    /// SKIP: L1-QUO-09 (role gate nằm ở controller [Authorize]), L1-QUO-12 (giới hạn 5 vòng chưa được cài đặt).
    /// </summary>
    public class QuotationServiceTests
    {
        private readonly Mock<IQuotationRepository> _quoRepo = new();
        private readonly Mock<ICartRepository> _cartRepo = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<INotificationService> _noti = new();
        private readonly QuotationService _sut;

        private readonly User _customer;
        private readonly CustomerProfile _profile;

        public QuotationServiceTests()
        {
            _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
            _customer = TestData.User();
            _profile = TestData.Profile(_customer.Id, p => p.User = _customer);
            _userRepo.Setup(r => r.GetCustomerProfileByUserIdAsync(_customer.Id)).ReturnsAsync(_profile);

            _sut = new QuotationService(_quoRepo.Object, _cartRepo.Object, _userRepo.Object, _uow.Object, _noti.Object);
        }

        /// <summary>Quotation gắn sẵn CustomerProfile của _customer, đăng ký GetByIdAsync.</summary>
        private Quotation SeedQuotation(Action<Quotation>? mutate = null)
        {
            var q = new Quotation
            {
                CustomerProfileId = _profile.Id,
                CustomerProfile = _profile,
                Status = QuotationStatus.Draft,
                OriginalTotal = 120_000_000m,
            };
            mutate?.Invoke(q);
            _quoRepo.Setup(r => r.GetByIdAsync(q.Id)).ReturnsAsync(q);
            return q;
        }

        private Cart CartWithTotal(decimal total)
        {
            var product = TestData.Product(Guid.NewGuid(), p => p.StandardListedPrice = total);
            var cart = TestData.Cart(_profile.Id);
            cart.Items.Add(new CartItem { CartId = cart.Id, ProductId = product.Id, Product = product, Quantity = 1, UnitPrice = total });
            return cart;
        }

        //  ▶ Block: CreateQuotationFromCartAsync()

        // L1-QUO-01 | Guard-TRUE | Cart >= 100M, chưa có giá thỏa thuận -> tạo quotation (Draft) + báo Sale phụ trách
        [Fact]
        public async Task L1_QUO_01_CreateFromCart_Above100M_CreatedAndSalesNotified()
        {
            var salesStaffId = Guid.NewGuid();
            _profile.AssignedSalesStaffId = salesStaffId;
            var cart = CartWithTotal(120_000_000m);
            _cartRepo.Setup(r => r.GetCartByCustomerIdAsync(_profile.Id)).ReturnsAsync(cart);

            Quotation? created = null;
            _quoRepo.Setup(r => r.CreateAsync(It.IsAny<Quotation>()))
                .Callback<Quotation>(q => { created = q; q.CustomerProfile = _profile; _quoRepo.Setup(x => x.GetByIdAsync(q.Id)).ReturnsAsync(q); })
                .ReturnsAsync((Quotation q) => q);

            var dto = await _sut.CreateQuotationFromCartAsync(_customer.Id, new CreateQuotationRequest());

            _quoRepo.Verify(r => r.CreateAsync(It.IsAny<Quotation>()), Times.Once);
            created!.Status.Should().Be(QuotationStatus.Draft); // 'Requested' trong spec = Draft trong code
            created.OriginalTotal.Should().Be(120_000_000m);
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_16_NewQuotationRequest, salesStaffId,
                It.IsAny<string>(), It.IsAny<string>(), created.Id, "Quotation"), Times.Once);
        }

        // L1-QUO-02 | Guard-FALSE | Cart < 100M -> từ chối, không tạo quotation
        [Fact]
        public async Task L1_QUO_02_CreateFromCart_Below100M_Rejected()
        {
            var cart = CartWithTotal(80_000_000m);
            _cartRepo.Setup(r => r.GetCartByCustomerIdAsync(_profile.Id)).ReturnsAsync(cart);

            var act = () => _sut.CreateQuotationFromCartAsync(_customer.Id, new CreateQuotationRequest());

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Đơn hàng phải từ 100 triệu trở lên để đàm phán giá.");
            _quoRepo.Verify(r => r.CreateAsync(It.IsAny<Quotation>()), Times.Never);
        }

        //  ▶ Block: PickUpQuotationAsync()

        // L1-QUO-03 | State-Valid | Draft -> Negotiating khi Sales nhận xử lý, gán SalesStaffId
        [Fact]
        public async Task L1_QUO_03_PickUp_AssignsStaffAndMovesToNegotiating()
        {
            var q = SeedQuotation();
            var s1 = Guid.NewGuid();

            var dto = await _sut.PickUpQuotationAsync(q.Id, s1);

            q.SalesStaffId.Should().Be(s1);
            q.Status.Should().Be(QuotationStatus.Negotiating);
            _quoRepo.Verify(r => r.UpdateAsync(q), Times.Once);
        }

        // L1-QUO-04 | EP-Invalid | Quotation đã có Sales khác nhận -> từ chối, giữ nguyên người phụ trách
        [Fact]
        public async Task L1_QUO_04_PickUp_AlreadyAssigned_Rejected()
        {
            var s2 = Guid.NewGuid();
            var q = SeedQuotation(x => { x.SalesStaffId = s2; x.Status = QuotationStatus.Negotiating; });

            var act = () => _sut.PickUpQuotationAsync(q.Id, Guid.NewGuid());

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Already picked up by another sales staff");
            q.SalesStaffId.Should().Be(s2);
        }

        //  ▶ Block: CreateVersionAsync()

        // L1-QUO-05 | State-Valid | Sales phụ trách gửi giá đề xuất -> version PendingManager, quotation PendingManager
        [Fact]
        public async Task L1_QUO_05_CreateVersion_MovesToPendingManager()
        {
            var s1 = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var q = SeedQuotation(x =>
            {
                x.SalesStaffId = s1;
                x.Status = QuotationStatus.Negotiating;
                x.Items.Add(new QuotationItem { ProductId = productId, Quantity = 3, OriginalUnitPrice = 40_000_000m });
            });
            QuotationVersion? version = null;
            _quoRepo.Setup(r => r.CreateVersionAsync(It.IsAny<QuotationVersion>()))
                .Callback<QuotationVersion>(v => version = v)
                .ReturnsAsync((QuotationVersion v) => v);

            await _sut.CreateVersionAsync(q.Id, s1, new CreateQuotationVersionRequest
            {
                ProposedTotal = 110_000_000m,
                Items = new List<QuotationVersionItemRequest>
                {
                    new() { ProductId = productId, ProposedUnitPrice = 36_666_667m }
                }
            });

            version.Should().NotBeNull();
            version!.Status.Should().Be(QuotationVersionStatus.PendingManager);
            version.VersionNumber.Should().Be(1);
            q.Status.Should().Be(QuotationStatus.PendingManager);
            _noti.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_17_QuotationPendingApproval, SystemRole.SalesManager,
                It.IsAny<string>(), It.IsAny<string>(), q.Id, "Quotation"), Times.Once);
        }

        // L1-QUO-06 | EP-Invalid | Staff không phụ trách gửi version -> Unauthorized, không tạo version
        [Fact]
        public async Task L1_QUO_06_CreateVersion_NotAssignedStaff_Forbidden()
        {
            var q = SeedQuotation(x => { x.SalesStaffId = Guid.NewGuid(); x.Status = QuotationStatus.Negotiating; });

            var act = () => _sut.CreateVersionAsync(q.Id, Guid.NewGuid(), new CreateQuotationVersionRequest());

            await act.Should().ThrowAsync<Exception>().WithMessage("Unauthorized");
            _quoRepo.Verify(r => r.CreateVersionAsync(It.IsAny<QuotationVersion>()), Times.Never);
        }

        //  ▶ Block: ManagerReviewVersionAsync() / CeoReviewVersionAsync()

        // L1-QUO-07 | State-Valid | Manager duyệt -> version PendingCeo (duyệt 2 cấp Manager -> CEO)
        [Fact]
        public async Task L1_QUO_07_ManagerReview_Approve_AdvancesToPendingCeo()
        {
            var mgr = Guid.NewGuid();
            var q = SeedQuotation(x => x.Status = QuotationStatus.PendingManager);
            var version = new QuotationVersion { QuotationId = q.Id, VersionNumber = 1, Status = QuotationVersionStatus.PendingManager };
            q.Versions.Add(version);

            await _sut.ManagerReviewVersionAsync(q.Id, mgr, new ManagerReviewRequest { IsApproved = true, ManagerNote = "OK" });

            version.Status.Should().Be(QuotationVersionStatus.PendingCeo);
            q.Status.Should().Be(QuotationStatus.PendingCeo);
            version.ManagerApprovedByUserId.Should().Be(mgr);
        }

        // L1-QUO-08 | State-Valid | Manager từ chối -> quay về Negotiating, lưu lý do cho Sales xem
        [Fact]
        public async Task L1_QUO_08_ManagerReview_Reject_BackToNegotiatingWithReason()
        {
            var q = SeedQuotation(x => x.Status = QuotationStatus.PendingManager);
            var version = new QuotationVersion { QuotationId = q.Id, VersionNumber = 1, Status = QuotationVersionStatus.PendingManager };
            q.Versions.Add(version);

            await _sut.ManagerReviewVersionAsync(q.Id, Guid.NewGuid(), new ManagerReviewRequest { IsApproved = false, ManagerNote = "Giá thấp quá" });

            version.Status.Should().Be(QuotationVersionStatus.ManagerRejected);
            version.ManagerNote.Should().Be("Giá thấp quá");
            q.Status.Should().Be(QuotationStatus.Negotiating);
        }

        // L1-QUO-09 | Chặn role không có quyền duyệt nằm ở tầng controller ([Authorize(Roles=...)]) —
        // CeoReviewVersionAsync không nhận thông tin role để kiểm tra ở unit level -> đã chuyển sang L3:
        // VietTien.IntegrationTests/RoleGateTests.cs (L1_QUO_09_CeoDecision_NonCeoRole_Forbidden).

        //  ▶ Block: CustomerDecisionAsync()

        // L1-QUO-10 | State-Valid | Khách chấp nhận version đã duyệt -> CustomerAccepted, AcceptedVersionId lưu lại
        [Fact]
        public async Task L1_QUO_10_CustomerDecision_Accept_TerminalAccepted()
        {
            var q = SeedQuotation(x => x.Status = QuotationStatus.Approved);
            var version = new QuotationVersion
            {
                QuotationId = q.Id,
                VersionNumber = 2,
                Status = QuotationVersionStatus.CeoApproved,
                ValidUntil = DateTime.UtcNow.AddDays(3)
            };
            q.Versions.Add(version);

            await _sut.CustomerDecisionAsync(q.Id, _customer.Id, new CustomerDecisionRequest { IsAccepted = true });

            q.Status.Should().Be(QuotationStatus.CustomerAccepted);
            version.Status.Should().Be(QuotationVersionStatus.CustomerAccepted);
            q.AcceptedVersionId.Should().Be(version.Id); // giá thỏa thuận gắn với account qua AcceptedVersionId
        }

        // L1-QUO-11 | State-Valid | Khách từ chối -> quay về Negotiating (vòng đàm phán tiếp theo)
        [Fact]
        public async Task L1_QUO_11_CustomerDecision_Reject_BackToNegotiating()
        {
            var q = SeedQuotation(x => x.Status = QuotationStatus.Approved);
            var version = new QuotationVersion
            {
                QuotationId = q.Id,
                Status = QuotationVersionStatus.CeoApproved,
                ValidUntil = DateTime.UtcNow.AddDays(3)
            };
            q.Versions.Add(version);

            await _sut.CustomerDecisionAsync(q.Id, _customer.Id, new CustomerDecisionRequest { IsAccepted = false });

            version.Status.Should().Be(QuotationVersionStatus.CustomerRejected);
            q.Status.Should().Be(QuotationStatus.Negotiating); // đàm phán tiếp tục
        }

        // L1-QUO-12 | BVA-Max+1 | Từ chối ở vòng thứ 5 -> Cancelled, không cho vòng 6, có escalation.
        // ĐÃ SỬA: trước đây không có bộ đếm vòng đàm phán — mọi lần từ chối đều quay về Negotiating.
        [Fact]
        public async Task L1_QUO_12_CustomerDecision_FifthRejection_Cancelled()
        {
            var salesStaffId = Guid.NewGuid();
            var q = SeedQuotation(x => { x.Status = QuotationStatus.Approved; x.SalesStaffId = salesStaffId; });
            var version = new QuotationVersion
            {
                QuotationId = q.Id,
                VersionNumber = 5,
                Status = QuotationVersionStatus.CeoApproved,
                ValidUntil = DateTime.UtcNow.AddDays(3)
            };
            q.Versions.Add(version);

            await _sut.CustomerDecisionAsync(q.Id, _customer.Id, new CustomerDecisionRequest { IsAccepted = false });

            version.Status.Should().Be(QuotationVersionStatus.CustomerRejected);
            q.Status.Should().Be(QuotationStatus.Cancelled, "vượt giới hạn 5 vòng đàm phán");

            // Không cho tạo vòng thứ 6
            var act = () => _sut.CreateVersionAsync(q.Id, salesStaffId, new CreateQuotationVersionRequest
            {
                ProposedTotal = 1_000_000m,
                Items = new List<QuotationVersionItemRequest>()
            });
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Cannot create version for this quotation state");

            _noti.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_27_QuotationNegotiationLimitReached, SystemRole.SalesManager,
                It.IsAny<string>(), It.IsAny<string>(), q.Id, "Quotation"), Times.Once);
        }

        // L1-QUO-13 | EP-Invalid | Khách không sở hữu quotation ra quyết định -> Unauthorized, trạng thái giữ nguyên
        [Fact]
        public async Task L1_QUO_13_CustomerDecision_NotOwner_Forbidden()
        {
            var q = SeedQuotation(x => x.Status = QuotationStatus.Approved);
            var strangerId = Guid.NewGuid();

            var act = () => _sut.CustomerDecisionAsync(q.Id, strangerId, new CustomerDecisionRequest { IsAccepted = true });

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Unauthorized or Quotation not found");
            q.Status.Should().Be(QuotationStatus.Approved);
        }

        //  ▶ Block: CancelQuotationAsync()

        // L1-QUO-14 | State-Valid | Khách hủy quotation đang mở -> Cancelled
        [Fact]
        public async Task L1_QUO_14_Cancel_OpenQuotation_Cancelled()
        {
            var q = SeedQuotation(x => x.Status = QuotationStatus.Draft);

            await _sut.CancelQuotationAsync(q.Id, _customer.Id);

            q.Status.Should().Be(QuotationStatus.Cancelled);
            _quoRepo.Verify(r => r.UpdateAsync(q), Times.Once);
        }

        // L1-QUO-15 | State-Invalid | Hủy quotation đã Accepted (terminal) -> conflict, trạng thái giữ nguyên
        [Fact]
        public async Task L1_QUO_15_Cancel_AcceptedQuotation_Conflict()
        {
            var q = SeedQuotation(x => x.Status = QuotationStatus.CustomerAccepted);

            var act = () => _sut.CancelQuotationAsync(q.Id, _customer.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Cannot cancel an accepted quotation");
            q.Status.Should().Be(QuotationStatus.CustomerAccepted);
        }

        //  ▶ Block: SendMessageAsync() / GetMessagesAsync()

        // L1-QUO-16 | EP-Valid | Người tham gia gửi tin nhắn đàm phán -> lưu 1 lần, trả DTO kèm thông tin người gửi
        [Fact]
        public async Task L1_QUO_16_SendMessage_PersistedOnceWithSenderInfo()
        {
            var sender = TestData.User(u => u.Role = SystemRole.SalesStaff);
            var q = SeedQuotation(x => x.SalesStaffId = sender.Id);
            ChatMessage? saved = null;
            _quoRepo.Setup(r => r.AddMessageAsync(It.IsAny<ChatMessage>()))
                .Callback<ChatMessage>(m => { saved = m; m.Sender = sender; })
                .ReturnsAsync((ChatMessage m) => m);
            _quoRepo.Setup(r => r.GetMessagesByQuotationIdAsync(q.Id))
                .ReturnsAsync(() => new[] { saved! });

            var dto = await _sut.SendMessageAsync(q.Id, sender.Id, new SendChatMessageRequest { MessageText = "Chào anh" });

            _quoRepo.Verify(r => r.AddMessageAsync(It.IsAny<ChatMessage>()), Times.Once);
            dto.MessageText.Should().Be("Chào anh");
            dto.SenderName.Should().Be(sender.FullName);
            dto.SenderRole.Should().Be("SalesStaff");
        }

        // L1-QUO-17 | Guard-FALSE | Người ngoài (không phải khách hàng/sales phụ trách/quản lý) đọc tin nhắn -> Forbidden (NFR-SEC04)
        [Fact]
        public async Task L1_QUO_17_GetMessages_NonParticipant_Forbidden()
        {
            var q = SeedQuotation(x => x.SalesStaffId = TestData.User(u => u.Role = SystemRole.SalesStaff).Id);
            var outsider = TestData.User(u => u.Role = SystemRole.SalesStaff);

            var act = () => _sut.GetMessagesAsync(q.Id, outsider.Id, "SalesStaff");

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }
}
