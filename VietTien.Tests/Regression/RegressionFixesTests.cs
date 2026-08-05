using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Regression
{
    /// <summary>
    /// Sheet: RegressionFixes — L1-REG-01..12. Test hồi quy cho các commit vá lỗi 24–28/07
    /// (f5b6b5e · 510915c · 81434ce · ae6fed6). MỖI CASE PHẢI FAIL NẾU BUG TÁI XUẤT.
    ///
    /// ⚠ L1-REG-03 (IDOR StockTransferService: nhận hàng ở kho không phải kho đích) BỊ BLOCKED ở tầng L1:
    ///    ReceiveAsync(Guid id, ReceiveStockTransferDto dto) KHÔNG có tham số caller/kho — không có chỗ
    ///    để kiểm tra phạm vi. Thuộc phạm vi L3. Xem DOC_MISMATCHES.md.
    /// </summary>
    public class RegressionFixesTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();

        // ── Block: 81434ce — Vá IDOR / auth / state-guard ────────────────────

        // L1-REG-01 | EP-Invalid | IDOR InventoryService: sửa tồn kho của kho KHÔNG được gán -> forbidden
        // 🔴 SPEC GAP v2.2: AdjustInventoryAsync(inventoryId, newQuantity, note, staffId) chỉ GHI LẠI
        // staffId vào LastUpdatedByUserId, không hề đối chiếu nhân viên với kho của bản ghi tồn.
        // Test ĐỎ cho tới khi bổ sung kiểm tra phạm vi kho (FT-05 NAC-05; NFR-SEC03).
        [Fact]
        public async Task L1_REG_01_AdjustInventory_OutsideAssignedWarehouse_IsForbidden()
        {
            var staffOfW1 = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            _db.Users.Add(staffOfW1);
            var (w1, _) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            _db.SaveChanges();

            var product = TestData.SeedProduct(_db);
            var inventoryInW2 = TestData.Inventory(product.Id, loc2.Id, 100);
            _db.Inventories.Add(inventoryInW2);
            _db.SaveChanges();

            var sut = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object,
                new Mock<ILogger<InventoryService>>().Object);
            var act = () => sut.AdjustInventoryAsync(inventoryInW2.Id, 5, "Kiểm kê", staffOfW1.Id);

            await act.Should().ThrowAsync<Exception>("nhân viên kho W1 không được sửa tồn của kho W2");
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == inventoryInW2.Id).OnHandQuantity.Should().Be(100);
        }

        // L1-REG-02 | EP-Invalid | IDOR QuotationService: khách đọc báo giá không thuộc mình -> forbidden
        [Fact]
        public async Task L1_REG_02_GetQuotation_CrossCustomer_IsForbidden()
        {
            var owner = TestData.User();
            var ownerProfile = TestData.Profile(owner.Id, p => p.User = owner);
            var intruder = TestData.User();
            var quotation = new Quotation
            {
                CustomerProfileId = ownerProfile.Id,
                CustomerProfile = ownerProfile,
                Status = QuotationStatus.Draft,
                OriginalTotal = 120_000_000m,
            };

            var quotationRepo = new Mock<IQuotationRepository>();
            quotationRepo.Setup(r => r.GetByIdAsync(quotation.Id)).ReturnsAsync(quotation);

            var sut = new QuotationService(
                quotationRepo.Object, new Mock<ICartRepository>().Object, new Mock<IUserRepository>().Object,
                new Mock<IUnitOfWork>().Object, new Mock<INotificationService>().Object);

            var act = () => sut.GetQuotationByIdAsync(quotation.Id, intruder.Id, "Customer");

            await act.Should().ThrowAsync<UnauthorizedAccessException>("không được lộ giá đề xuất cho khách khác");
        }

        // L1-REG-04 | State-Invalid | PurchaseOrderService: state-guard chặn chuyển trạng thái sai thứ tự
        [Theory]
        [InlineData(PurchaseOrderStatus.Cancelled, "Issue")]          // Cancelled không được Issue
        [InlineData(PurchaseOrderStatus.Draft, "SendToWarehouse")]    // Draft chưa Issue thì không gửi kho
        [InlineData(PurchaseOrderStatus.Issued, "Close")]             // Issued chưa nhận đủ hàng thì không đóng
        public async Task L1_REG_04_PurchaseOrder_InvalidTransition_IsConflict(PurchaseOrderStatus from, string action)
        {
            var ceo = TestData.User(u => u.Role = SystemRole.CEO);
            var supplier = new Supplier { Name = "NCC A", Code = "SUP-01" };
            var (warehouse, _) = TestData.Warehouse();
            _db.Users.Add(ceo);
            _db.Suppliers.Add(supplier);
            _db.Warehouses.Add(warehouse);
            _db.SaveChanges();

            var po = new PurchaseOrder
            {
                Code = $"PO-{Guid.NewGuid():N}"[..12],
                CreatedById = ceo.Id,
                SupplierId = supplier.Id,
                WarehouseId = warehouse.Id,
                Status = from,
            };
            _db.PurchaseOrders.Add(po);
            _db.SaveChanges();

            var sut = new PurchaseOrderService(_db,
                new Mock<INotificationService>().Object,
                new Mock<ILogger<PurchaseOrderService>>().Object);

            Func<Task> act = action switch
            {
                "Issue" => () => sut.IssueAsync(po.Id, ceo.Id),
                "SendToWarehouse" => () => sut.SendToWarehouseAsync(po.Id, ceo.Id),
                _ => () => sut.ClosePurchaseOrderAsync(po.Id, ceo.Id),
            };

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ChangeTracker.Clear();
            _db.PurchaseOrders.Single(p => p.Id == po.Id).Status.Should().Be(from, "trạng thái không được đổi");
        }

        // ── Block: 510915c — Race condition & guard huỷ/thanh toán ───────────

        // L1-REG-05 | Concurrency | Hai lần đặt cùng lượng tồn cuối -> lần thứ 2 phải thất bại, tồn không âm
        // ⚠ EF InMemory không mô phỏng được row-lock; kiểm chứng atomic thật nằm ở
        //   L1-RES-06 (SQLite) và L2 trên DB thật. Ở đây khẳng định GUARD "không bán quá tồn" còn nguyên.
        [Fact]
        public async Task L1_REG_05_OverSelling_LastUnitsIsBlocked()
        {
            var product = TestData.SeedProduct(_db);
            TestData.SeedInventory(_db, product.Id, 10);
            var reservation = new FakeInventoryReservationService(_db);

            await reservation.ReserveAsync(new[] { (product.Id, 10) });

            var act = () => reservation.ReserveAsync(new[] { (product.Id, 10) });

            await act.Should().ThrowAsync<Exception>("10 sản phẩm cuối chỉ được bán 1 lần");
            _db.ChangeTracker.Clear();
            var inv = _db.Inventories.Single(i => i.ProductId == product.Id);
            inv.ReservedQuantity.Should().Be(10);
            inv.AvailableQuantity.Should().Be(0).And.BeGreaterThanOrEqualTo(0, "tồn khả dụng không được âm");
        }

        // L1-REG-08 | EP-Invalid | NotificationService gửi ĐÚNG 1 người nhận, không phát tán cho người ngoài phạm vi
        [Fact]
        public async Task L1_REG_08_Notification_GoesOnlyToIntendedRecipient()
        {
            var (targetSales, _) = TestData.SeedCustomer(_db);
            var (outsider, _) = TestData.SeedCustomer(_db);
            var sut = new NotificationService(_db, MockHubContext.Create<NotificationHub>().Object);

            await sut.CreateNotificationAsync(NotificationType.SYS_24_CustomerRequestedCancel, targetSales.Id,
                "Khách yêu cầu huỷ đơn", "Đơn VT123456 — khách yêu cầu huỷ.");

            _db.Notifications.Should().ContainSingle()
                .Which.RecipientUserId.Should().Be(targetSales.Id);
            _db.Notifications.Any(n => n.RecipientUserId == outsider.Id)
                .Should().BeFalse("nội dung nhạy cảm không được gửi cho người không liên quan");
        }

        // ── Block: f5b6b5e + ae6fed6 — Validation DTO & bug logic ────────────

        // L1-REG-09 | EP-Invalid | Validation DTO: số lượng <= 0 và ngày giao trong quá khứ bị chặn ở tầng nhập
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void L1_REG_09_InvalidQuantity_IsRejectedByDtoValidation(int quantity)
        {
            var dto = new ReturnExchangeItemDto { ProductId = Guid.NewGuid(), Quantity = quantity };

            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                dto, new System.ComponentModel.DataAnnotations.ValidationContext(dto), results, validateAllProperties: true);

            isValid.Should().BeFalse("số lượng phải >= 1");
        }

        [Fact]
        public void L1_REG_09_PastDeliveryDate_IsRejectedByDtoValidation()
        {
            var dto = new API.DTOs.Delivery.SchedulePickupRequestDto
            {
                VehicleId = 1,
                Shift = "Sáng",
                PickupDate = DateTime.UtcNow.AddDays(-1) // quá khứ
            };

            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                dto, new System.ComponentModel.DataAnnotations.ValidationContext(dto), results, validateAllProperties: true);

            isValid.Should().BeFalse("không được lên lịch cho ngày trong quá khứ");
        }

        // L1-REG-10 | EP-Valid | AddressService: luôn còn ĐÚNG 1 địa chỉ mặc định sau mỗi lần đổi
        [Fact]
        public async Task L1_REG_10_SetDefaultAddress_KeepsExactlyOneDefault()
        {
            var (user, profile) = TestData.SeedCustomer(_db);
            var uow = new UnitOfWork(_db);
            var addresses = new[]
            {
                new Address { CustomerProfileId = profile.Id, ReceiverName = "A1", ReceiverPhone = "0900000001", SpecificAddress = "1 Lê Lợi", Ward = "P1", District = "Q1", City = "HCM", IsDefault = true },
                new Address { CustomerProfileId = profile.Id, ReceiverName = "A2", ReceiverPhone = "0900000002", SpecificAddress = "2 Lê Lợi", Ward = "P1", District = "Q1", City = "HCM" },
                new Address { CustomerProfileId = profile.Id, ReceiverName = "A3", ReceiverPhone = "0900000003", SpecificAddress = "3 Lê Lợi", Ward = "P1", District = "Q1", City = "HCM" },
            };
            _db.Addresses.AddRange(addresses);
            _db.SaveChanges();

            var sut = new AddressService(uow);

            foreach (var target in new[] { addresses[1], addresses[2] })
            {
                await sut.SetDefaultAddressAsync(user.Id, target.Id);

                _db.ChangeTracker.Clear();
                var all = _db.Addresses.Where(a => a.CustomerProfileId == profile.Id).ToList();
                all.Count(a => a.IsDefault).Should().Be(1, "luôn phải còn đúng 1 địa chỉ mặc định");
                all.Single(a => a.IsDefault).Id.Should().Be(target.Id);
            }
        }

        // L1-REG-11 | EP-Valid | MaterialService: tìm kiếm không phân biệt HOA/thường
        [Fact]
        public async Task L1_REG_11_MaterialSearch_IsCaseInsensitive()
        {
            _db.Materials.Add(new Material { Name = "Nhựa PP", Unit = "Cây", CurrentStock = 100, SafetyThreshold = 10 });
            _db.SaveChanges();

            var sut = new MaterialService(_db);

            (await sut.GetAllAsync("NHỰA")).Should().ContainSingle().Which.Name.Should().Be("Nhựa PP");
            (await sut.GetAllAsync("nhựa")).Should().ContainSingle();
            (await sut.GetAllAsync("Nhựa PP")).Should().ContainSingle();
        }

        // L1-REG-12 | Atomicity | GoodsIssue: post phiếu nhiều dòng thất bại giữa chừng -> KHÔNG ghi một phần
        [Fact]
        public async Task L1_REG_12_PostGoodsIssue_PartialFailure_LeavesStockUntouched()
        {
            var staff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            var (warehouse, location) = TestData.Warehouse();
            _db.Users.Add(staff);
            _db.Warehouses.Add(warehouse);
            _db.SaveChanges();

            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            _db.Inventories.AddRange(
                TestData.Inventory(p1.Id, location.Id, 10),
                TestData.Inventory(p2.Id, location.Id, 1)); // p2 KHÔNG đủ cho 3 -> phiếu phải hỏng ở dòng 2
            _db.SaveChanges();

            var sut = new GoodsIssueService(_db, new Mock<ICloudinaryService>().Object);
            var issue = await sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = warehouse.Id,
                Items = new List<CreateGoodsIssueItemRequestDto>
                {
                    new() { ProductId = p1.Id, Quantity = 3 },
                    new() { ProductId = p2.Id, Quantity = 3 },
                }
            }, staff.Id);

            var act = () => sut.PostGoodsIssueAsync(issue.Id, staff.Id);

            await act.Should().ThrowAsync<Exception>();
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.ProductId == p1.Id).OnHandQuantity
                .Should().Be(10, "dòng 1 KHÔNG được trừ khi dòng 2 thất bại");
            _db.Inventories.Single(i => i.ProductId == p2.Id).OnHandQuantity.Should().Be(1);
            _db.StockTransactions.Should().BeEmpty("không được ghi StockTransaction một phần");
        }
    }
}
