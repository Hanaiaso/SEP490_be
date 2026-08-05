using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: PurchaseOrderService — L1-PO-01..09. EF InMemory + mock INotificationService.
    /// </summary>
    public class PurchaseOrderServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _noti = new();
        private readonly PurchaseOrderService _sut;
        private readonly User _ceo;
        private readonly Supplier _supplier;
        private readonly Warehouse _warehouse;

        public PurchaseOrderServiceTests()
        {
            _sut = new PurchaseOrderService(_db, _noti.Object, new Mock<ILogger<PurchaseOrderService>>().Object);
            _ceo = TestData.User(u => u.Role = SystemRole.CEO);
            _supplier = new Supplier { Name = "NCC A", Code = "SUP-01" };
            (_warehouse, _) = TestData.Warehouse();
            _db.Users.Add(_ceo);
            _db.Suppliers.Add(_supplier);
            _db.Warehouses.Add(_warehouse);
            _db.SaveChanges();
        }

        private PurchaseOrder SeedPo(PurchaseOrderStatus status)
        {
            var po = new PurchaseOrder
            {
                Code = $"PO-{Guid.NewGuid():N}"[..12],
                CreatedById = _ceo.Id,
                SupplierId = _supplier.Id,
                WarehouseId = _warehouse.Id,
                Status = status,
            };
            po.Items.Add(new PurchaseOrderItem { ExpectedQuantity = 10, UnitPrice = 1000m, Unit = "Cái" });
            _db.PurchaseOrders.Add(po);
            _db.SaveChanges();
            return po;
        }

        // L1-PO-01 | EP-Valid | Tạo PO hợp lệ -> status Draft, đủ line item như request
        [Fact]
        public async Task L1_PO_01_Create_Valid_DraftWithLines()
        {
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);

            var dto = await _sut.CreateAsync(_ceo.Id, new CreatePurchaseOrderRequest
            {
                SupplierId = _supplier.Id,
                WarehouseId = _warehouse.Id,
                Items = new List<CreatePurchaseOrderItemRequest>
                {
                    new() { ProductId = p1.Id, ExpectedQuantity = 10, UnitPrice = 1000m, Unit = "Cái" },
                    new() { ProductId = p2.Id, ExpectedQuantity = 5, UnitPrice = 2000m, Unit = "Cái" },
                }
            });

            dto.Status.Should().Be("Draft");
            dto.Items.Should().HaveCount(2);
            dto.Items.Select(i => i.ExpectedQuantity).Should().BeEquivalentTo(new[] { 10, 5 });
        }

        // L1-PO-02 | State-Invalid | UpdateDraft trên PO đã Issued -> conflict, nội dung không đổi
        [Fact]
        public async Task L1_PO_02_UpdateDraft_OnIssuedPo_Rejected()
        {
            var po = SeedPo(PurchaseOrderStatus.Issued);

            var act = () => _sut.UpdateDraftAsync(po.Id, _ceo.Id, new CreatePurchaseOrderRequest
            {
                SupplierId = _supplier.Id,
                WarehouseId = _warehouse.Id
            });

            await act.Should().ThrowAsync<Exception>().WithMessage("Can only update Draft Purchase Orders");
            _db.PurchaseOrders.Single(p => p.Id == po.Id).Status.Should().Be(PurchaseOrderStatus.Issued);
        }

        // L1-PO-03 | State-Valid | Issue PO Draft -> Issued, ghi IssuedAt
        [Fact]
        public async Task L1_PO_03_Issue_Draft_BecomesIssued()
        {
            var po = SeedPo(PurchaseOrderStatus.Draft);

            var dto = await _sut.IssueAsync(po.Id, _ceo.Id);

            dto.Status.Should().Be("Issued");
            _db.PurchaseOrders.Single(p => p.Id == po.Id).IssuedAt.Should().NotBeNull();
        }

        // L1-PO-04 | State-Invalid | Issue PO không ở Draft (Cancelled) -> conflict, trạng thái giữ nguyên
        [Fact]
        public async Task L1_PO_04_Issue_CancelledPo_Rejected()
        {
            var po = SeedPo(PurchaseOrderStatus.Cancelled);

            var act = () => _sut.IssueAsync(po.Id, _ceo.Id);

            await act.Should().ThrowAsync<Exception>().WithMessage("Can only issue Draft Purchase Orders");
            _db.PurchaseOrders.Single(p => p.Id == po.Id).Status.Should().Be(PurchaseOrderStatus.Cancelled);
        }

        // L1-PO-05 | State-Valid | SendToWarehouse PO Issued -> SentToWarehouse + báo WarehouseStaff
        [Fact]
        public async Task L1_PO_05_SendToWarehouse_Issued_AdvancesAndNotifies()
        {
            var po = SeedPo(PurchaseOrderStatus.Issued);

            var dto = await _sut.SendToWarehouseAsync(po.Id, _ceo.Id);

            dto.Status.Should().Be("SentToWarehouse");
            _noti.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_18_POSentToWarehouse, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), po.Id, "PurchaseOrder"), Times.Once);
        }

        // L1-PO-06 | State-Valid | Cancel PO Draft -> Cancelled (terminal); Issue sau đó bị chặn
        [Fact]
        public async Task L1_PO_06_Cancel_Draft_TerminalCancelled()
        {
            var po = SeedPo(PurchaseOrderStatus.Draft);

            var dto = await _sut.CancelAsync(po.Id, _ceo.Id);

            dto.Status.Should().Be("Cancelled");
            var issueAfter = () => _sut.IssueAsync(po.Id, _ceo.Id);
            await issueAfter.Should().ThrowAsync<Exception>(); // không thể Issue PO đã hủy
        }

        // L1-PO-07 | Guard-FALSE | Close PO đang DiscrepancyReview (còn sai lệch chưa xử lý) -> reject, giữ nguyên trạng thái
        [Fact]
        public async Task L1_PO_07_Close_WithUnresolvedDiscrepancy_Blocked()
        {
            var po = SeedPo(PurchaseOrderStatus.DiscrepancyReview);

            var act = () => _sut.ClosePurchaseOrderAsync(po.Id, _ceo.Id);

            await act.Should().ThrowAsync<Exception>().WithMessage("*FullyReceived*");
            _db.PurchaseOrders.Single(p => p.Id == po.Id).Status.Should().Be(PurchaseOrderStatus.DiscrepancyReview);
        }

        // ⚠ 2 test dưới đây KHÔNG có Test ID trong doc L1 v2.2 (sheet PurchaseOrderService chỉ có
        //    PO-01..07). Chúng phủ nhánh Close hợp lệ + Close lặp — đề xuất bổ sung 2 case mới vào
        //    sheet PurchaseOrderService của doc v2.3, xem DOC_MISMATCHES.md.

        // [Chưa có trong doc] State-Valid | Close PO đã FullyReceived -> Closed (terminal)
        [Fact]
        public async Task PO_Extra_Close_FullyReceived_BecomesClosed()
        {
            var po = SeedPo(PurchaseOrderStatus.FullyReceived);

            var dto = await _sut.ClosePurchaseOrderAsync(po.Id, _ceo.Id);

            dto.Status.Should().Be("Closed");
        }

        // [Chưa có trong doc] State-Invalid | Close PO đã Closed -> reject, không đóng lặp lại
        [Fact]
        public async Task PO_Extra_Close_AlreadyClosed_Rejected()
        {
            var po = SeedPo(PurchaseOrderStatus.Closed);

            var act = () => _sut.ClosePurchaseOrderAsync(po.Id, _ceo.Id);

            await act.Should().ThrowAsync<Exception>().WithMessage("*FullyReceived*");
        }
    }
}
