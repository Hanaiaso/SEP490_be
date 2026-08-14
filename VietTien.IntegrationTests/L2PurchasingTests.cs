using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-Purchasing — vòng đời PO → phiếu nhập → tồn kho.
    /// Chuỗi: POST /api/purchase-orders → {id}/issue → {id}/send-to-warehouse
    ///        → {id}/receipts → {id}/receipts/{rId}/post → {id}/close
    /// </summary>
    [Trait("Category", "L2")]
    public class L2PurchasingTests : SqlServerTestBase
    {
        public L2PurchasingTests(SqlServerFixture factory) : base(factory) { }

        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        private sealed record PoFixture(Guid SupplierId, Guid WarehouseId, Guid ProductId, Guid InventoryId);

        private async Task<PoFixture> SeedSupplierAndStockAsync(int onHand)
        {
            Guid supplierId = Guid.NewGuid();
            Guid warehouseId = Guid.Empty, productId = Guid.Empty, inventoryId = Guid.Empty;

            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.Include(i => i.WarehouseLocation).FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = onHand;
                inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0; inv.InTransitQuantity = 0;
                productId = inv.ProductId!.Value;
                inventoryId = inv.Id;
                warehouseId = inv.WarehouseLocation.WarehouseId;

                foreach (var o in await db.Inventories.Where(i => i.ProductId == productId && i.Id != inv.Id).ToListAsync())
                {
                    o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0;
                }

                db.Suppliers.Add(new Supplier
                {
                    Id = supplierId,
                    Name = "Nha cung cap L2",
                    Code = $"NCC{Random.Shared.Next(100000, 999999)}",
                    IsActive = true
                });
            });

            return new PoFixture(supplierId, warehouseId, productId, inventoryId);
        }

        private static async Task<Guid> ReadGuidAsync(HttpResponseMessage response, string prop)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.NameEquals(prop) && p.Value.TryGetGuid(out var g)) return g;
            return Guid.Empty;
        }

        /// <summary>Tạo PO 2 dòng và trả về (poId, purchaseOrderItemId của dòng sản phẩm).</summary>
        private async Task<(Guid PoId, Guid ItemId)> CreatePoAsync(HttpClient ceo, PoFixture f, int expectedQty)
        {
            var create = await ceo.PostAsJsonAsync("/api/purchase-orders", new CreatePurchaseOrderRequest
            {
                SupplierId = f.SupplierId,
                WarehouseId = f.WarehouseId,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Note = "L2-PUR",
                Items = new List<CreatePurchaseOrderItemRequest>
                {
                    new() { ProductId = f.ProductId, ExpectedQuantity = expectedQty, UnitPrice = 10_000m, Unit = "Cái" },
                    new() { ProductId = f.ProductId, ExpectedQuantity = 5, UnitPrice = 12_000m, Unit = "Cái" }
                }
            });
            create.StatusCode.Should().Be(HttpStatusCode.OK,
                "tạo PO phải thành công; body: {0}", await create.Content.ReadAsStringAsync());

            var poId = await ReadGuidAsync(create, "id");
            poId.Should().NotBe(Guid.Empty);

            var itemId = await QueryAsync(db => db.PurchaseOrderItems.AsNoTracking()
                .Where(i => i.PurchaseOrderId == poId && i.ExpectedQuantity == expectedQty)
                .Select(i => i.Id).FirstAsync());
            return (poId, itemId);
        }

        // ── L2-PUR-01 ──────────────────────────────────────────────────────────────────────

        // GIVEN  CEO; nhà cung cấp SUP-1; P1 tồn W1 = 10
        // WHEN   Tạo PO 2 dòng → issue → send-to-warehouse → tạo phiếu nhập → post (nhận 50)
        // THEN   PO Draft→Issued→SentToWarehouse; phiếu Posted; tồn P1 = 60; có StockTransaction
        [Fact]
        [Trait("TestID", "L2-PUR-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-06 AC-01; AC-03; BR-014")]
        public async Task L2_PUR_01_PurchaseOrderLifecycleIncrementsInventoryOnce()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var f = await SeedSupplierAndStockAsync(onHand: 10);

            var (poId, itemId) = await CreatePoAsync(ceo, f, expectedQty: 50);

            (await QueryAsync(db => db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId)))
                .Status.Should().Be(PurchaseOrderStatus.Draft);

            var issue = await ceo.PostAsync($"/api/purchase-orders/{poId}/issue", null);
            issue.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await issue.Content.ReadAsStringAsync());
            (await QueryAsync(db => db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId)))
                .Status.Should().Be(PurchaseOrderStatus.Issued);

            var send = await ceo.PostAsync($"/api/purchase-orders/{poId}/send-to-warehouse", null);
            send.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await send.Content.ReadAsStringAsync());
            (await QueryAsync(db => db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId)))
                .Status.Should().Be(PurchaseOrderStatus.SentToWarehouse);

            // Tạo phiếu nhập nhận đủ 50
            var createReceipt = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts",
                new CreateGoodsReceiptRequest
                {
                    Note = "nhan du",
                    Items = new List<CreateGoodsReceiptItemRequest>
                    {
                        new() { PurchaseOrderItemId = itemId, AcceptedQuantity = 50 }
                    }
                });
            createReceipt.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await createReceipt.Content.ReadAsStringAsync());
            var receiptId = await ReadGuidAsync(createReceipt, "id");
            receiptId.Should().NotBe(Guid.Empty);

            var invBeforePost = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == f.InventoryId));
            invBeforePost.OnHandQuantity.Should().Be(10, "tạo phiếu chưa được cộng tồn, phải đợi post");

            var post = await warehouse.PostAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", null);
            post.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await post.Content.ReadAsStringAsync());

            // (b) DB — scope mới
            var inv = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == f.InventoryId));
            inv.OnHandQuantity.Should().Be(60, "10 + 50 nhận vào");
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);

            // (c) side effect — vết biến động tồn
            (await QueryAsync(db => db.StockTransactions.CountAsync(t => t.InventoryId == f.InventoryId)))
                .Should().BeGreaterThan(0, "BR-014: nhập kho phải để lại StockTransaction");
        }

        // ── L2-PUR-02 ──────────────────────────────────────────────────────────────────────

        // GIVEN  PO-2 đang Draft, chưa gửi kho
        // WHEN   POST /api/purchase-orders/{id}/receipts
        // THEN   409; không tạo bản ghi phiếu nhập nào
        [Fact]
        [Trait("TestID", "L2-PUR-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-06 NAC-01; BR-014")]
        public async Task L2_PUR_02_CannotCreateReceiptBeforePoSentToWarehouse()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var f = await SeedSupplierAndStockAsync(onHand: 10);

            var (poId, itemId) = await CreatePoAsync(ceo, f, expectedQty: 50);
            // KHÔNG issue, KHÔNG send-to-warehouse

            var response = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts",
                new CreateGoodsReceiptRequest
                {
                    Items = new List<CreateGoodsReceiptItemRequest>
                    {
                        new() { PurchaseOrderItemId = itemId, AcceptedQuantity = 50 }
                    }
                });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "SRS FT-06 NAC-01: PO chưa gửi kho thì không được lập phiếu nhập; body: {0}",
                await response.Content.ReadAsStringAsync());

            // (b) DB
            (await QueryAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == poId)))
                .Should().Be(0, "không được tạo phiếu nhập");
            (await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == f.InventoryId)))
                .OnHandQuantity.Should().Be(10, "tồn không được đụng tới");
        }

        // ── L2-PUR-03 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Phiếu nhập R1 đã Posted, tồn P1 = 60
        // WHEN   POST .../receipts/{rId}/post cho R1 LẦN HAI
        // THEN   409; tồn giữ nguyên 60 — không cộng kép
        [Fact]
        [Trait("TestID", "L2-PUR-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-06 NAC-05; BR-029")]
        public async Task L2_PUR_03_PostingSameReceiptTwiceDoesNotDoubleIncrementStock()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var f = await SeedSupplierAndStockAsync(onHand: 10);

            var (poId, itemId) = await CreatePoAsync(ceo, f, expectedQty: 50);
            (await ceo.PostAsync($"/api/purchase-orders/{poId}/issue", null)).EnsureSuccessStatusCode();
            (await ceo.PostAsync($"/api/purchase-orders/{poId}/send-to-warehouse", null)).EnsureSuccessStatusCode();

            var createReceipt = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts",
                new CreateGoodsReceiptRequest
                {
                    Items = new List<CreateGoodsReceiptItemRequest>
                    {
                        new() { PurchaseOrderItemId = itemId, AcceptedQuantity = 50 }
                    }
                });
            var receiptId = await ReadGuidAsync(createReceipt, "id");

            (await warehouse.PostAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == f.InventoryId)))
                .OnHandQuantity.Should().Be(60);

            // Post lần hai
            var second = await warehouse.PostAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", null);

            // (a) HTTP
            second.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "SRS FT-06 NAC-05: post lại phiếu đã ghi sổ phải trả 409; body: {0}",
                await second.Content.ReadAsStringAsync());

            // (b) DB — bất biến quan trọng nhất
            var inv = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == f.InventoryId));
            inv.OnHandQuantity.Should().Be(60, "tuyệt đối không được cộng kép");
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);
        }

        // ── L2-PUR-04 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Phiếu nhập có chênh lệch số lượng so với PO, chưa xử lý
        // WHEN   CEO POST /api/purchase-orders/{id}/close
        // THEN   4xx: phải xử lý chênh lệch trước; sau khi resolve-discrepancy thì close thành công
        [Fact]
        [Trait("TestID", "L2-PUR-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-06 AC-05; NAC-03; BR-036")]
        public async Task L2_PUR_04_CannotClosePoWithUnresolvedDiscrepancy()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var f = await SeedSupplierAndStockAsync(onHand: 10);

            var (poId, itemId) = await CreatePoAsync(ceo, f, expectedQty: 50);
            (await ceo.PostAsync($"/api/purchase-orders/{poId}/issue", null)).EnsureSuccessStatusCode();
            (await ceo.PostAsync($"/api/purchase-orders/{poId}/send-to-warehouse", null)).EnsureSuccessStatusCode();

            // Nhận THIẾU 10 so với 50 đặt -> phát sinh chênh lệch
            var createReceipt = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts",
                new CreateGoodsReceiptRequest
                {
                    Note = "nhan thieu",
                    Items = new List<CreateGoodsReceiptItemRequest>
                    {
                        new() { PurchaseOrderItemId = itemId, AcceptedQuantity = 40, ShortQuantity = 10 }
                    }
                });
            createReceipt.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await createReceipt.Content.ReadAsStringAsync());
            var receiptId = await ReadGuidAsync(createReceipt, "id");
            (await warehouse.PostAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", null))
                .EnsureSuccessStatusCode();

            // Đóng PO khi chênh lệch chưa xử lý
            var closeTooEarly = await ceo.PostAsync($"/api/purchase-orders/{poId}/close", null);

            // (a) HTTP
            ((int)closeTooEarly.StatusCode).Should().BeInRange(400, 499,
                "BR-036: còn chênh lệch chưa xử lý thì không được đóng PO; body: {0}",
                await closeTooEarly.Content.ReadAsStringAsync());

            // (b) DB
            (await QueryAsync(db => db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId)))
                .Status.Should().NotBe(PurchaseOrderStatus.Closed);

            // Xử lý chênh lệch -> PurchaseOrderService.ResolveDiscrepancyAsync tự đóng PO luôn trong
            // cùng 1 bước (không cần gọi /close riêng — gọi lại sẽ 409 vì PO đã Closed, không còn
            // FullyReceived nữa).
            var resolve = await ceo.PostAsJsonAsync($"/api/purchase-orders/{poId}/resolve-discrepancy",
                new DiscrepancyResolutionRequest { ResolutionType = "CloseShort", Reason = "NCC giao thieu, chap nhan" });
            resolve.StatusCode.Should().Be(HttpStatusCode.OK,
                "sau khi xử lý chênh lệch phải đóng PO thành công; body: {0}", await resolve.Content.ReadAsStringAsync());
            (await QueryAsync(db => db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId)))
                .Status.Should().Be(PurchaseOrderStatus.Closed);
        }

        // ── L2-PUR-05 ──────────────────────────────────────────────────────────────────────

        // GIVEN  File Excel hợp lệ gồm 3 dòng hàng
        // WHEN   CEO POST /api/purchase-orders/import/excel (multipart)
        // THEN   PO Draft được tạo với ĐÚNG 3 dòng khớp giá trị trong file
        [Fact]
        [Trait("TestID", "L2-PUR-05")]
        [Trait("Priority", "P2")]
        [Trait("SRSRef", "FT-06 AC-02; NAC-02; BV-03; BR-015")]
        public async Task L2_PUR_05_ImportExcelParsesAllLinesFromFile()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            await SeedSupplierAndStockAsync(onHand: 0);

            // File .xlsx THẬT (nhị phân). Trước đây test gửi CSV kèm tên .xlsx và service cũng đọc
            // theo kiểu text nên vẫn qua; từ GH-10 service dùng ClosedXML đọc đúng định dạng Excel
            // (PurchaseOrderService.cs:117-159) nên CSV giả sẽ ném "Không đọc được file Excel".
            // Dòng 1 là header, 3 dòng dữ liệu — đúng thứ tự cột ProductSku, Quantity, UnitPrice.
            var content = new MultipartFormDataContent();
            byte[] fileBytes;
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("PO");
                sheet.Cell(1, 1).Value = "ProductSku";
                sheet.Cell(1, 2).Value = "Quantity";
                sheet.Cell(1, 3).Value = "UnitPrice";

                var data = new (string Sku, int Qty, decimal Price)[]
                {
                    ("SKU-1", 10, 1000m), ("SKU-2", 20, 2000m), ("SKU-3", 30, 3000m),
                };
                for (int i = 0; i < data.Length; i++)
                {
                    sheet.Cell(i + 2, 1).Value = data[i].Sku;
                    sheet.Cell(i + 2, 2).Value = data[i].Qty;
                    sheet.Cell(i + 2, 3).Value = data[i].Price;
                }

                using var ms = new MemoryStream();
                workbook.SaveAs(ms);
                fileBytes = ms.ToArray();
            }
            var filePart = new ByteArrayContent(fileBytes);
            filePart.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(filePart, "file", "po-import.xlsx");

            var response = await ceo.PostAsync("/api/purchase-orders/import/excel", content);

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB — BR-015: nội dung file phải được phân tích thành các dòng hàng
            var poId = await ReadGuidAsync(response, "id");
            poId.Should().NotBe(Guid.Empty);

            var po = await QueryAsync(db => db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId));
            po.Status.Should().Be(PurchaseOrderStatus.Draft);

            var lines = await QueryAsync(db => db.PurchaseOrderItems.AsNoTracking()
                .Where(i => i.PurchaseOrderId == poId).ToListAsync());
            lines.Should().HaveCount(3,
                "BR-015: PO nhập từ Excel phải có đúng 3 dòng đọc từ file, không được tạo PO rỗng");
        }
    }
}
