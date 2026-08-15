using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.Data;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// SRS NAC-05 ("operate outside the assigned warehouse/role -> 403 WAREHOUSE_ACTION_FORBIDDEN")
    /// và FT-09 NAC-02 ("count line outside the authorised session/warehouse scope -> reject").
    ///
    /// Mỗi case dựng 2 kho + 2 nhân viên và cho nhân viên kho A thao tác lên tài nguyên của kho B.
    /// MỖI CASE PHẢI FAIL NẾU HÀNG RÀO PHÂN QUYỀN THEO KHO BỊ GỠ.
    /// </summary>
    public class WarehouseScopeGuardTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly IWarehouseAccessGuard _guard;

        private readonly Warehouse _whA;
        private readonly WarehouseLocation _locA;
        private readonly Warehouse _whB;
        private readonly WarehouseLocation _locB;
        private readonly User _staffA;
        private readonly User _staffB;

        public WarehouseScopeGuardTests()
        {
            _guard = TestWarehouseAccessGuard.Create(_db);

            (_whA, _locA) = TestData.Warehouse();
            (_whB, _locB) = TestData.Warehouse();
            _db.Warehouses.AddRange(_whA, _whB);

            _staffA = TestData.User(u => { u.Role = SystemRole.WarehouseStaff; u.AssignedWarehouseId = _whA.Id; });
            _staffB = TestData.User(u => { u.Role = SystemRole.WarehouseStaff; u.AssignedWarehouseId = _whB.Id; });
            _db.Users.AddRange(_staffA, _staffB);
            _db.SaveChanges();
        }

        private Inventory SeedInventoryIn(WarehouseLocation location, int onHand = 100)
        {
            var product = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(product.Id, location.Id, onHand);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            return inv;
        }

        // ── #7 GoodsIssueService ─────────────────────────────────────────────

        private GoodsIssueService NewGoodsIssueService()
            => new(_db, new Mock<ICloudinaryService>().Object, _guard);

        [Fact]
        public async Task GoodsIssue_CreateForAnotherWarehouse_IsForbidden()
        {
            var sut = NewGoodsIssueService();
            var product = TestData.SeedProduct(_db);

            var act = () => sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _whB.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = product.Id, Quantity = 1 } }
            }, _staffA.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>(
                "nhân viên kho A không được lập phiếu xuất cho kho B");
            _db.GoodsIssues.Should().BeEmpty("phiếu không được tạo khi bị chặn");
        }

        [Fact]
        public async Task GoodsIssue_PostIssueOfAnotherWarehouse_IsForbiddenAndStockUntouched()
        {
            var sut = NewGoodsIssueService();
            var inv = SeedInventoryIn(_locB, 100);

            // Nhân viên kho B lập phiếu hợp lệ cho kho B.
            var issue = await sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _whB.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = inv.ProductId, Quantity = 10 } }
            }, _staffB.Id);

            // Nhân viên kho A cố Post phiếu đó -> phải bị chặn, tồn kho B không được trừ.
            var act = () => sut.PostGoodsIssueAsync(issue.Id, _staffA.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity
                .Should().Be(100, "Post bị chặn thì tồn kho vật lý phải nguyên vẹn");
            _db.StockTransactions.Should().BeEmpty();
        }

        [Fact]
        public async Task GoodsIssue_List_OnlyShowsAssignedWarehouse()
        {
            var sut = NewGoodsIssueService();
            var productA = TestData.SeedProduct(_db);
            var productB = TestData.SeedProduct(_db);

            await sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _whA.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = productA.Id, Quantity = 1 } }
            }, _staffA.Id);
            await sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _whB.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = productB.Id, Quantity = 1 } }
            }, _staffB.Id);

            var seenByA = await sut.GetGoodsIssuesAsync(null, _staffA.Id);

            seenByA.Should().ContainSingle().Which.WarehouseId.Should().Be(_whA.Id,
                "danh sách phiếu xuất phải lọc theo kho được phân công, không lộ phiếu của kho khác");
        }

        [Fact]
        public async Task GoodsIssue_GetByIdOfAnotherWarehouse_IsForbidden()
        {
            var sut = NewGoodsIssueService();
            var product = TestData.SeedProduct(_db);
            var issue = await sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _whB.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = product.Id, Quantity = 1 } }
            }, _staffB.Id);

            var act = () => sut.GetGoodsIssueByIdAsync(issue.Id, _staffA.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // ── #8 InventoryCountSessionService ──────────────────────────────────

        private InventoryCountSessionService NewCountSessionService()
        {
            var sysConfig = new Mock<ISystemConfigService>();
            sysConfig.Setup(s => s.GetEffectiveValueAsync("INVENTORY_COUNT_VARIANCE_THRESHOLD", It.IsAny<DateTime?>()))
                     .ReturnsAsync("5");
            return new InventoryCountSessionService(_db, sysConfig.Object, new Mock<INotificationService>().Object, _guard);
        }

        [Fact]
        public async Task CountSession_RecordItemCountOnAnotherWarehouse_IsForbidden()
        {
            var sut = NewCountSessionService();
            SeedInventoryIn(_locB, 100);
            var session = await sut.OpenAsync(_staffB.Id, new OpenInventoryCountSessionRequest { WarehouseId = _whB.Id });
            var itemId = session.Items.Single().Id;

            var act = () => sut.RecordItemCountAsync(session.Id, itemId, _staffA.Id,
                new RecordCountItemRequest { PhysicalQuantity = 1 });

            await act.Should().ThrowAsync<UnauthorizedAccessException>(
                "ghi số đếm mới là thao tác quyết định tồn kho lúc đóng phiên, phải chặn ngoài phạm vi kho");
            _db.ChangeTracker.Clear();
            _db.InventoryCountingSessionItems.Single().PhysicalQuantity
                .Should().BeNull("số đếm không được ghi khi bị chặn");
        }

        [Fact]
        public async Task CountSession_CloseSessionOfAnotherWarehouse_IsForbiddenAndStockUntouched()
        {
            var sut = NewCountSessionService();
            var inv = SeedInventoryIn(_locB, 100);
            var session = await sut.OpenAsync(_staffB.Id, new OpenInventoryCountSessionRequest { WarehouseId = _whB.Id });
            await sut.RecordItemCountAsync(session.Id, session.Items.Single().Id, _staffB.Id,
                new RecordCountItemRequest { PhysicalQuantity = 98 }); // -2, trong ngưỡng -> sẽ áp thẳng nếu lọt

            var act = () => sut.CloseAsync(session.Id, _staffA.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity
                .Should().Be(100, "đóng phiên bị chặn thì không được áp chênh lệch vào tồn kho");
            _db.InventoryCountingSessions.Single().Status
                .Should().Be(InventoryCountSessionStatus.Open, "phiên phải còn mở");
        }

        [Fact]
        public async Task CountSession_GetByIdOfAnotherWarehouse_IsForbidden()
        {
            var sut = NewCountSessionService();
            SeedInventoryIn(_locB, 100);
            var session = await sut.OpenAsync(_staffB.Id, new OpenInventoryCountSessionRequest { WarehouseId = _whB.Id });

            var act = () => sut.GetByIdAsync(session.Id, _staffA.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // ── #9 StockAdjustmentService ────────────────────────────────────────

        [Fact]
        public async Task StockAdjustment_GetByIdOfAnotherStaffProposal_IsForbidden()
        {
            var sut = new StockAdjustmentService(_db, new Mock<INotificationService>().Object, _guard);
            var inv = SeedInventoryIn(_locB, 100);

            var created = await sut.CreateAsync(_staffB.Id, new CreateStockAdjustmentRequest
            {
                InventoryId = inv.Id,
                PhysicalQuantity = 90,
                Reason = "Kiểm kê kho B"
            });

            var act = () => sut.GetByIdAsync(created.Id, _staffA.Id, SystemRole.WarehouseStaff);

            await act.Should().ThrowAsync<UnauthorizedAccessException>(
                "danh sách đã lọc theo người đề xuất thì chi tiết cũng phải lọc, nếu không là IDOR đọc");
        }

        [Fact]
        public async Task StockAdjustment_GetByIdAsCeo_IsAllowed()
        {
            var sut = new StockAdjustmentService(_db, new Mock<INotificationService>().Object, _guard);
            var ceo = TestData.User(u => u.Role = SystemRole.CEO);
            _db.Users.Add(ceo);
            _db.SaveChanges();
            var inv = SeedInventoryIn(_locB, 100);

            var created = await sut.CreateAsync(_staffB.Id, new CreateStockAdjustmentRequest
            {
                InventoryId = inv.Id,
                PhysicalQuantity = 90,
                Reason = "Kiểm kê kho B"
            });

            var dto = await sut.GetByIdAsync(created.Id, ceo.Id, SystemRole.CEO);

            dto.Id.Should().Be(created.Id, "CEO là người duyệt nên phải xem được mọi đề xuất");
        }

        // ── #10 Quarantine (WarehouseManagementController) ───────────────────

        private WarehouseManagementController NewQuarantineController(Guid actingUserId)
            => new WarehouseManagementController(
                    new Mock<IWarehouseManagementService>().Object, _db, _guard)
                .WithUser(actingUserId, "WarehouseStaff");

        [Fact]
        public async Task Quarantine_DispatchLogOfAnotherWarehouse_Returns403AndStockUntouched()
        {
            var inv = SeedInventoryIn(_locB, 100);
            inv.QuarantineQuantity = 5;
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-CROSS",
                ProductId = inv.ProductId,
                InventoryId = inv.Id,
                Quantity = 5,
                Reason = "hàng lỗi",
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = _staffB.Id
            };
            _db.QuarantineLogs.Add(log);
            _db.SaveChanges();

            var sut = NewQuarantineController(_staffA.Id);

            var result = await sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "available" });

            result.StatusOf().Should().Be(403);
            _db.ChangeTracker.Clear();
            _db.QuarantineLogs.Single().Status.Should().Be(QuarantineStatus.Waiting, "lô phải giữ nguyên trạng thái");
            _db.Inventories.Single(i => i.Id == inv.Id).QuarantineQuantity.Should().Be(5);
        }

        [Fact]
        public async Task Quarantine_List_OnlyShowsAssignedWarehouse()
        {
            var invA = SeedInventoryIn(_locA, 50);
            var invB = SeedInventoryIn(_locB, 50);
            _db.QuarantineLogs.AddRange(
                new QuarantineLog
                {
                    QuarantineCode = "QZ-A", ProductId = invA.ProductId, InventoryId = invA.Id,
                    Quantity = 1, Reason = "a", Status = QuarantineStatus.Waiting, ReceivedByUserId = _staffA.Id
                },
                new QuarantineLog
                {
                    QuarantineCode = "QZ-B", ProductId = invB.ProductId, InventoryId = invB.Id,
                    Quantity = 1, Reason = "b", Status = QuarantineStatus.Waiting, ReceivedByUserId = _staffB.Id
                });
            _db.SaveChanges();

            var sut = NewQuarantineController(_staffA.Id);

            var result = await sut.GetQuarantineList();

            var items = result.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>()
                .Which.Value.Should().BeAssignableTo<List<QuarantineListItemDto>>().Subject;
            items.Should().ContainSingle().Which.QuarantineCode.Should().Be("QZ-A");
        }

        /// <summary>
        /// Truy vấn cũ là <c>Inventories.FirstOrDefault(i =&gt; i.ProductId == dto.ProductId)</c> — bỏ qua
        /// warehouse hoàn toàn. Ở đây sản phẩm CHỈ tồn ở kho A, còn người tiếp nhận thuộc kho B: nếu
        /// không lọc theo kho thì truy vấn sẽ vớ đúng dòng của kho A và cộng nhầm hàng cách ly vào đó.
        /// Không phụ thuộc thứ tự trả về của EF nên kết quả tất định.
        /// </summary>
        [Fact]
        public async Task Quarantine_Receive_WhenProductOnlyExistsInAnotherWarehouse_IsRejected()
        {
            var product = TestData.SeedProduct(_db);
            var invA = TestData.Inventory(product.Id, _locA.Id, 50);
            _db.Inventories.Add(invA);
            var order = new Order { OrderCode = "ORD-QZ" };
            _db.Orders.Add(order);
            _db.SaveChanges();

            var sut = NewQuarantineController(_staffB.Id);

            var result = await sut.ReceiveToQuarantine(new QuarantineReceiveDto
            {
                ProductId = product.Id,
                OrderId = order.Id,
                Quantity = 4,
                Reason = "khách trả hàng lỗi"
            });

            result.StatusOf().Should().Be(404);
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == invA.Id).QuarantineQuantity
                .Should().Be(0, "tuyệt đối không được cộng hàng cách ly vào tồn kho của kho khác");
            _db.QuarantineLogs.Should().BeEmpty();
        }

        [Fact]
        public async Task Quarantine_Receive_IntoOwnWarehouse_Succeeds()
        {
            var product = TestData.SeedProduct(_db);
            var invB = TestData.Inventory(product.Id, _locB.Id, 50);
            _db.Inventories.Add(invB);
            var order = new Order { OrderCode = "ORD-QZ-OK" };
            _db.Orders.Add(order);
            _db.SaveChanges();

            var sut = NewQuarantineController(_staffB.Id);

            var result = await sut.ReceiveToQuarantine(new QuarantineReceiveDto
            {
                ProductId = product.Id,
                OrderId = order.Id,
                Quantity = 4,
                Reason = "khách trả hàng lỗi"
            });

            result.StatusOf().Should().Be(200);
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == invB.Id).QuarantineQuantity.Should().Be(4);
            _db.QuarantineLogs.Single().InventoryId.Should().Be(invB.Id);
        }
    }
}
