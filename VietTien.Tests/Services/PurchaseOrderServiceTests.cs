using ClosedXML.Excel;
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
        private readonly Mock<IOcrService> _ocr = new();
        private readonly PurchaseOrderService _sut;
        private readonly User _ceo;
        private readonly Supplier _supplier;
        private readonly Warehouse _warehouse;

        public PurchaseOrderServiceTests()
        {
            _sut = new PurchaseOrderService(_db, _noti.Object, new Mock<ILogger<PurchaseOrderService>>().Object, _ocr.Object);
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
            // GH-10: IssueAsync chặn PO còn dòng "N/A" (ProductId lẫn MaterialId đều null), nên dòng
            // hàng mẫu phải trỏ tới một sản phẩm thật — đúng tiền điều kiện "PO Draft có >= 1 line".
            var seedProduct = TestData.SeedProduct(_db);
            po.Items.Add(new PurchaseOrderItem { ProductId = seedProduct.Id, ExpectedQuantity = 10, UnitPrice = 1000m, Unit = "Cái" });
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

        // L1-PO-06b | State-Valid | Cancel PO SentToWarehouse đang có Goods Receipt Draft dở dang ->
        // Draft đó phải bị hủy theo, không được để nguyên trạng chờ Post sau.
        // BUGFIX: trước đây chỉ đổi po.Status, Draft receipt vẫn nằm nguyên -> kho không biết PO đã
        // hủy, Post sau đó (nếu PostReceiptAsync không tự chặn) sẽ cộng tồn thật và ghi đè lại po.Status.
        [Fact]
        public async Task L1_PO_06b_Cancel_SentToWarehouseWithDraftReceipt_CancelsDraftToo()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse);
            var draftReceipt = new GoodsReceipt { PurchaseOrderId = po.Id, ReceivedByUserId = _ceo.Id, Code = "GR-TEST-01", Status = GoodsReceiptStatus.Draft };
            _db.GoodsReceipts.Add(draftReceipt);
            _db.SaveChanges();

            var dto = await _sut.CancelAsync(po.Id, _ceo.Id);

            dto.Status.Should().Be("Cancelled");
            _db.GoodsReceipts.Single(r => r.Id == draftReceipt.Id).Status.Should().Be(GoodsReceiptStatus.Cancelled);
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

        // ── Block: Import PO từ ảnh OCR (P2-1, UC-44/UC-52) ──────────────────

        private static Microsoft.AspNetCore.Http.IFormFile FakeImageFile(string name = "invoice.jpg")
        {
            var file = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            file.SetupGet(f => f.Length).Returns(128);
            file.SetupGet(f => f.FileName).Returns(name);
            file.SetupGet(f => f.ContentType).Returns("image/jpeg");
            file.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(new byte[] { 1, 2, 3 }));
            return file.Object;
        }

        [Fact]
        public async Task ImportFromImage_VendorNameFuzzyMatches_UsesMatchedSupplier()
        {
            var supplier2 = new Supplier { Name = "Công ty TNHH Bao Bì Việt Tiến", Code = "SUP-02" };
            _db.Suppliers.Add(supplier2);
            _db.SaveChanges();
            _ocr.Setup(o => o.ExtractInvoiceAsync(It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(new InvoiceOcrResult
            {
                VendorName = "Bao Bì Việt Tiến",
                Items = new List<InvoiceOcrItem> { new() { Description = "Hàng lạ không khớp", Quantity = 2, UnitPrice = 5000m } }
            });

            var dto = await _sut.ImportFromImageAsync(FakeImageFile(), _ceo.Id);

            dto.SupplierId.Should().Be(supplier2.Id);
        }

        [Fact]
        public async Task ImportFromImage_ItemNameMatchesProduct_LinksProductId()
        {
            var product = TestData.SeedProduct(_db, p => p.Name = "Băng keo trong 5cm");
            _ocr.Setup(o => o.ExtractInvoiceAsync(It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(new InvoiceOcrResult
            {
                VendorName = "NCC A",
                Items = new List<InvoiceOcrItem> { new() { Description = "Băng keo trong", Quantity = 10, UnitPrice = 8000m } }
            });

            var dto = await _sut.ImportFromImageAsync(FakeImageFile(), _ceo.Id);

            dto.Items.Should().ContainSingle();
            var savedItem = _db.PurchaseOrderItems.Single(i => i.PurchaseOrderId == dto.Id);
            savedItem.ProductId.Should().Be(product.Id);
            savedItem.Note.Should().BeNull();
        }

        [Fact]
        public async Task ImportFromImage_ItemNameUnmatched_KeepsNullIdsWithNote()
        {
            _ocr.Setup(o => o.ExtractInvoiceAsync(It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(new InvoiceOcrResult
            {
                VendorName = "NCC A",
                Items = new List<InvoiceOcrItem> { new() { Description = "Sản phẩm không có trong hệ thống", Quantity = 3, UnitPrice = 1000m } }
            });

            var dto = await _sut.ImportFromImageAsync(FakeImageFile(), _ceo.Id);

            var savedItem = _db.PurchaseOrderItems.Single(i => i.PurchaseOrderId == dto.Id);
            savedItem.ProductId.Should().BeNull();
            savedItem.MaterialId.Should().BeNull();
            savedItem.Note.Should().Contain("Sản phẩm không có trong hệ thống");
            dto.Note.Should().Contain("chưa khớp");
        }

        [Fact]
        public async Task ImportFromImage_OcrReturnsNoItems_ThrowsAndCreatesNoPo()
        {
            _ocr.Setup(o => o.ExtractInvoiceAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(new InvoiceOcrResult());
            var countBefore = _db.PurchaseOrders.Count();

            var act = () => _sut.ImportFromImageAsync(FakeImageFile(), _ceo.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.PurchaseOrders.Count().Should().Be(countBefore);
        }

        // ── Block: Import PO từ file Excel .xlsx thật (P2-2, UC-44/UC-52) ────

        private static Microsoft.AspNetCore.Http.IFormFile FakeExcelFile(byte[] bytes, string name = "import.xlsx")
        {
            var file = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            file.SetupGet(f => f.Length).Returns(bytes.Length);
            file.SetupGet(f => f.FileName).Returns(name);
            file.SetupGet(f => f.ContentType).Returns("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            file.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
            return file.Object;
        }

        private static byte[] BuildXlsx(params (string Sku, int Quantity, decimal UnitPrice)[] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Sheet1");
            ws.Cell(1, 1).Value = "ProductSku";
            ws.Cell(1, 2).Value = "Quantity";
            ws.Cell(1, 3).Value = "UnitPrice";
            for (int i = 0; i < rows.Length; i++)
            {
                var r = i + 2;
                ws.Cell(r, 1).Value = rows[i].Sku;
                ws.Cell(r, 2).Value = rows[i].Quantity;
                ws.Cell(r, 3).Value = rows[i].UnitPrice;
            }
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        [Fact]
        public async Task ImportFromExcel_ValidXlsx_CreatesDraftWithMatchedProducts()
        {
            var p1 = TestData.SeedProduct(_db, p => p.Sku = "SKU-001");
            var p2 = TestData.SeedProduct(_db, p => p.Sku = "SKU-002");
            var bytes = BuildXlsx(("SKU-001", 10, 1000), ("SKU-002", 5, 2000));

            var dto = await _sut.ImportFromExcelAsync(FakeExcelFile(bytes), _ceo.Id);

            dto.Items.Should().HaveCount(2);
            var saved = _db.PurchaseOrderItems.Where(i => i.PurchaseOrderId == dto.Id).ToList();
            saved.Should().Contain(i => i.ProductId == p1.Id && i.ExpectedQuantity == 10);
            saved.Should().Contain(i => i.ProductId == p2.Id && i.ExpectedQuantity == 5);
        }

        [Fact]
        public async Task ImportFromExcel_SkuNotFound_KeepsRowWithNullProductAndNote()
        {
            var bytes = BuildXlsx(("SKU-KHONG-TON-TAI", 3, 500));

            var dto = await _sut.ImportFromExcelAsync(FakeExcelFile(bytes), _ceo.Id);

            var saved = _db.PurchaseOrderItems.Single(i => i.PurchaseOrderId == dto.Id);
            saved.ProductId.Should().BeNull();
            saved.Note.Should().Contain("SKU-KHONG-TON-TAI");
        }

        [Fact]
        public async Task ImportFromExcel_NoValidRows_ThrowsAndCreatesNoPo()
        {
            var bytes = BuildXlsx();
            var countBefore = _db.PurchaseOrders.Count();

            var act = () => _sut.ImportFromExcelAsync(FakeExcelFile(bytes), _ceo.Id);

            await act.Should().ThrowAsync<Exception>();
            _db.PurchaseOrders.Count().Should().Be(countBefore);
        }

        [Fact]
        public async Task ImportFromExcel_NotAValidXlsxFile_ThrowsFriendlyError()
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5 };

            var act = () => _sut.ImportFromExcelAsync(FakeExcelFile(bytes, "import.xls"), _ceo.Id);

            await act.Should().ThrowAsync<Exception>().WithMessage("*.xlsx*");
        }

        // ── Block: Resolve Discrepancy theo resolutionType (P2-3) ────────────

        private PurchaseOrder SeedPoWithReceipt(Guid productId, int expectedQty, int receivedQty, int excessQty, int quarantineQty)
        {
            var po = new PurchaseOrder
            {
                Code = $"PO-{Guid.NewGuid():N}"[..12],
                CreatedById = _ceo.Id,
                SupplierId = _supplier.Id,
                WarehouseId = _warehouse.Id,
                Status = PurchaseOrderStatus.DiscrepancyReview,
            };
            var poItem = new PurchaseOrderItem { ProductId = productId, ExpectedQuantity = expectedQty, ReceivedQuantity = receivedQty, UnitPrice = 1000m, Unit = "Cái" };
            po.Items.Add(poItem);
            _db.PurchaseOrders.Add(po);
            _db.SaveChanges();

            var location = _db.WarehouseLocations.First(l => l.WarehouseId == _warehouse.Id);
            _db.Inventories.Add(new Inventory { ProductId = productId, WarehouseLocationId = location.Id, OnHandQuantity = receivedQty, QuarantineQuantity = quarantineQty });

            var receipt = new GoodsReceipt { PurchaseOrderId = po.Id, ReceivedByUserId = _ceo.Id, Code = "GR-001", Status = GoodsReceiptStatus.Posted };
            receipt.Items.Add(new GoodsReceiptItem { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = receivedQty, ExcessQuantity = excessQty });
            _db.GoodsReceipts.Add(receipt);
            _db.SaveChanges();

            return po;
        }

        [Fact]
        public async Task ResolveDiscrepancy_ReturnExcess_DeductsQuarantineAndLogsTransaction()
        {
            var product = TestData.SeedProduct(_db);
            var po = SeedPoWithReceipt(product.Id, expectedQty: 10, receivedQty: 10, excessQty: 3, quarantineQty: 3);

            var dto = await _sut.ResolveDiscrepancyAsync(po.Id, _ceo.Id, new DiscrepancyResolutionRequest { ResolutionType = "ReturnExcess", Reason = "Thừa 3, trả NCC" });

            dto.Status.Should().Be("Closed");
            _db.Inventories.Single(i => i.ProductId == product.Id).QuarantineQuantity.Should().Be(0);
            _db.StockTransactions.Should().ContainSingle(t => t.TransactionType == TransactionType.ReturnToSupplier && t.QuantityChange == -3);
        }

        [Fact]
        public async Task ResolveDiscrepancy_ReturnExcess_NoExcessRecorded_Throws()
        {
            var po = SeedPo(PurchaseOrderStatus.DiscrepancyReview);

            var act = () => _sut.ResolveDiscrepancyAsync(po.Id, _ceo.Id, new DiscrepancyResolutionRequest { ResolutionType = "ReturnExcess", Reason = "Không có hàng thừa" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.PurchaseOrders.Single(p => p.Id == po.Id).Status.Should().Be(PurchaseOrderStatus.DiscrepancyReview);
        }

        [Fact]
        public async Task ResolveDiscrepancy_RequestSupplemental_StillShort_ReopensToPartiallyReceived()
        {
            var product = TestData.SeedProduct(_db);
            var po = SeedPoWithReceipt(product.Id, expectedQty: 10, receivedQty: 6, excessQty: 0, quarantineQty: 0);

            var dto = await _sut.ResolveDiscrepancyAsync(po.Id, _ceo.Id, new DiscrepancyResolutionRequest { ResolutionType = "RequestSupplemental", Reason = "Yêu cầu giao bổ sung 4" });

            dto.Status.Should().Be("PartiallyReceived");
        }

        [Fact]
        public async Task ResolveDiscrepancy_RequestSupplemental_NoShortageLeft_Throws()
        {
            var product = TestData.SeedProduct(_db);
            var po = SeedPoWithReceipt(product.Id, expectedQty: 10, receivedQty: 10, excessQty: 0, quarantineQty: 0);

            var act = () => _sut.ResolveDiscrepancyAsync(po.Id, _ceo.Id, new DiscrepancyResolutionRequest { ResolutionType = "RequestSupplemental", Reason = "Không còn thiếu" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Theory]
        [InlineData("AcceptExcess")]
        [InlineData("CloseShort")]
        public async Task ResolveDiscrepancy_AcceptExcessOrCloseShort_ClosesPo(string resolutionType)
        {
            var po = SeedPo(PurchaseOrderStatus.DiscrepancyReview);

            var dto = await _sut.ResolveDiscrepancyAsync(po.Id, _ceo.Id, new DiscrepancyResolutionRequest { ResolutionType = resolutionType, Reason = "Xử lý" });

            dto.Status.Should().Be("Closed");
        }
    }
}
