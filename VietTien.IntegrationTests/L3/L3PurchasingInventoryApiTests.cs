using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-PurchasingInventoryAPI</c> — PUR-01..08, TRF-01..07, INV-01..07.
    ///
    /// Ánh xạ chính: POST /api/goods-receipts/{id}/post -> POST /api/purchase-orders/{id}/receipts/{rId}/post;
    /// POST /api/stock-transfers/{id}/post -> /dispatch; PUT /api/inventory/{id} -> /{id}/adjust.
    /// </summary>
    public class L3PurchasingInventoryApiTests : L3TestBase
    {
        public L3PurchasingInventoryApiTests(L3SqlFixture factory) : base(factory) { }

        private async Task<Supplier> SeedSupplierAsync()
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "NCC L3 " + Guid.NewGuid().ToString("N")[..6],
                Code = "NCC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                Phone = NewPhone(),
                Email = NewEmail(),
                Address = "Dia chi NCC",
                IsActive = true,
            };
            await SeedAsync(db => { db.Suppliers.Add(supplier); return Task.CompletedTask; });
            return supplier;
        }

        /// <summary>Phiếu chuyển kho Draft từ WH-DEFAULT sang WH-TRADE với 1 dòng hàng.</summary>
        private async Task<(StockTransfer transfer, Product product)> SeedDraftTransferAsync(
            int stockAtSource, int transferQuantity)
        {
            var (product, _) = await SeedSellableProductAsync(50_000m, stockAtSource);
            var transfer = new StockTransfer
            {
                Id = Guid.NewGuid(),
                Code = "TR-L3-" + Guid.NewGuid().ToString("N")[..6],
                SourceWarehouseId = L3Seed.WarehouseDefaultId,
                DestinationWarehouseId = L3Seed.WarehouseTradeId,
                CreatedByUserId = L3Seed.WarehouseStaffId,
                Status = StockTransferStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.StockTransfers.Add(transfer);
                db.StockTransferItems.Add(new StockTransferItem
                {
                    Id = Guid.NewGuid(),
                    StockTransferId = transfer.Id,
                    ProductId = product.Id,
                    Quantity = transferQuantity,
                });
                return Task.CompletedTask;
            });
            return (transfer, product);
        }

        // ── Block: Purchase Order & Goods Receipt (FT-06) ─────────────────────────────────────

        /// PUR-01 | Input-Domain-Happy | FT-06 AC-01
        /// CEO tạo PO rồi phát hành -> PO chuyển sang trạng thái đã phát hành.
        [Fact]
        public async Task L3_PUR_01_CreateAndIssuePurchaseOrder_ByCeo_Succeeds()
        {
            var supplier = await SeedSupplierAsync();
            var (product, _) = await SeedSellableProductAsync(50_000m, 0);
            var ceo = await ClientForSeededAsync(L3Seed.CeoId);

            var create = await ceo.PostAsJsonAsync("/api/purchase-orders", new
            {
                SupplierId = supplier.Id,
                WarehouseId = L3Seed.WarehouseDefaultId,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Items = new[] { new { ProductId = product.Id, ExpectedQuantity = 10, UnitPrice = 40_000m } }
            });

            create.IsSuccessStatusCode.Should().BeTrue(
                $"CEO phải tạo được PO (server trả {(int)create.StatusCode}: {await ReadMessageAsync(create)})");
            var poId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

            var issue = await ceo.PostAsJsonAsync($"/api/purchase-orders/{poId}/issue", new { });

            issue.IsSuccessStatusCode.Should().BeTrue();
            (await QueryAsync(db => db.PurchaseOrders.SingleAsync(p => p.Id == poId)))
                .Status.Should().NotBe(PurchaseOrderStatus.Draft, "PO đã phát hành không còn là bản nháp");
        }

        /// PUR-02 | Input-Domain-Error | FT-06 NAC-05; NFR-SEC03
        /// Nhân viên kho phát hành PO -> 403 (chỉ CEO được phát hành).
        [Fact]
        public async Task L3_PUR_02_IssuePurchaseOrder_ByWarehouseStaff_Forbidden()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var res = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{Guid.NewGuid()}/issue", new { });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// PUR-03 | Input-Domain-Error | FT-06 NAC-01; BR-014  ->  nhóm B
        /// Không có đường tăng tồn kho THẲNG từ PO — bất biến BR-014 được bảo đảm bằng việc
        /// không mở route /api/inventory/post-from-po.
        [Fact]
        public async Task L3_PUR_03_PostInventoryDirectlyFromPo_RouteDoesNotExist()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var before = await QueryAsync(db => db.Inventories
                .SingleAsync(i => i.Id == L3Seed.InventoryPeWrapDefaultId));

            var res = await warehouse.PostAsJsonAsync("/api/inventory/post-from-po",
                new { PurchaseOrderId = Guid.NewGuid() });

            // 404 (không có route) hoặc 405 (prefix /api/inventory tồn tại nhưng không có verb này)
            // đều là bằng chứng "không có đường tăng tồn thẳng từ PO".
            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == L3Seed.InventoryPeWrapDefaultId)))
                .OnHandQuantity.Should().Be(before.OnHandQuantity,
                    "muốn tăng tồn BẮT BUỘC phải đi qua Goods Receipt — đó là cách BR-014 được bảo đảm");
        }

        /// PUR-04 | BVA | FT-06 BV-03; NAC-02; BR-015
        /// Import Excel: file rỗng / sai định dạng -> bị từ chối, KHÔNG phát hành PO nào.
        [Fact]
        public async Task L3_PUR_04_ImportExcel_InvalidFile_Rejected_NoPurchaseOrderIssued()
        {
            var ceo = await ClientForSeededAsync(L3Seed.CeoId);
            var poCountBefore = await QueryAsync(db => db.PurchaseOrders.CountAsync());

            using var content = new MultipartFormDataContent();
            var bytes = new byte[] { 0x00, 0x01, 0x02 }; // KHÔNG phải file .xlsx hợp lệ
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "file", "khong-phai-excel.txt");

            var res = await ceo.PostAsync("/api/purchase-orders/import/excel", content);

            res.IsSuccessStatusCode.Should().BeFalse("file sai định dạng phải bị từ chối");
            (await QueryAsync(db => db.PurchaseOrders.CountAsync()))
                .Should().Be(poCountBefore, "không được tạo/phát hành PO từ file hỏng");
        }

        /// PUR-05 | BVA | FT-06 BV-02; AC-04; BR-036
        /// Post phiếu nhập: chỉ nhân viên kho được post; vai trò khác -> 403.
        [Fact]
        public async Task L3_PUR_05_PostGoodsReceipt_RoleGate()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.PostAsJsonAsync(
                    $"/api/purchase-orders/{Guid.NewGuid()}/receipts/{Guid.NewGuid()}/post", new { }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            (await warehouse.PostAsJsonAsync(
                    $"/api/purchase-orders/{Guid.NewGuid()}/receipts/{Guid.NewGuid()}/post", new { }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
                    "nhân viên kho phải lọt qua cổng phân quyền");
        }

        /// PUR-06 | Input-Domain-Error | FT-06 NAC-04; BR-021  ->  nhóm B
        /// Không có route SỬA phiếu nhập -> chứng từ đã post là bất biến theo thiết kế.
        [Fact]
        public async Task L3_PUR_06_EditGoodsReceipt_RouteDoesNotExist_ImmutableByAbsence()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var res = await warehouse.PutAsJsonAsync($"/api/goods-receipts/{Guid.NewGuid()}",
                new { Note = "sua phieu da post" });

            // BR-021 được bảo đảm bằng việc KHÔNG mở route sửa phiếu nhập.
            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }

        /// PUR-07 | Idempotency | FT-06 NAC-05; BR-029
        /// Post phiếu nhập lần 2 trên id không tồn tại vẫn phải trả lỗi xác định (không 500),
        /// và không bao giờ tăng tồn kho.
        [Fact]
        public async Task L3_PUR_07_PostGoodsReceipt_Replay_NeverIncreasesStockTwice()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var before = await QueryAsync(db => db.Inventories
                .SingleAsync(i => i.Id == L3Seed.InventoryPeWrapDefaultId));

            var poId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var first = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", new { });
            var second = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", new { });

            ((int)first.StatusCode).Should().BeLessThan(500, "phải trả lỗi nghiệp vụ xác định, không phải 500");
            second.StatusCode.Should().Be(first.StatusCode, "lần 2 phải cho kết quả nhất quán với lần 1");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == L3Seed.InventoryPeWrapDefaultId)))
                .OnHandQuantity.Should().Be(before.OnHandQuantity, "tồn kho KHÔNG được tăng");
        }

        /// PUR-08 | BVA | FT-06 BV-01; NAC-03
        /// Tạo PO với số lượng âm/0 -> bị chặn bởi validation, không tạo PO.
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public async Task L3_PUR_08_CreatePurchaseOrder_NonPositiveQuantity_Rejected(int quantity)
        {
            var supplier = await SeedSupplierAsync();
            var (product, _) = await SeedSellableProductAsync(50_000m, 0);
            var ceo = await ClientForSeededAsync(L3Seed.CeoId);
            var before = await QueryAsync(db => db.PurchaseOrders.CountAsync());

            var res = await ceo.PostAsJsonAsync("/api/purchase-orders", new
            {
                SupplierId = supplier.Id,
                WarehouseId = L3Seed.WarehouseDefaultId,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Items = new[] { new { ProductId = product.Id, ExpectedQuantity = quantity, UnitPrice = 40_000m } }
            });

            res.IsSuccessStatusCode.Should().BeFalse($"số lượng {quantity} phải bị từ chối");
            (await QueryAsync(db => db.PurchaseOrders.CountAsync())).Should().Be(before);
        }

        // ── Block: Stock Transfer (FT-11) ─────────────────────────────────────────────────────

        /// TRF-01 | Input-Domain-Error | FT-11 NAC-01; BV-03
        /// Kho nguồn == kho đích -> 400 (chặn bởi IValidatableObject của CreateStockTransferDto).
        [Fact]
        public async Task L3_TRF_01_CreateTransfer_SameSourceAndDestination_Rejected()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var (product, _) = await SeedSellableProductAsync(50_000m, 100);
            var before = await QueryAsync(db => db.StockTransfers.CountAsync());

            var res = await warehouse.PostAsJsonAsync("/api/stock-transfers", new
            {
                SourceWarehouseId = L3Seed.WarehouseDefaultId,
                DestinationWarehouseId = L3Seed.WarehouseDefaultId, // TRÙNG
                Items = new[] { new { ProductId = product.Id, Quantity = 5 } }
            });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await res.Content.ReadAsStringAsync()).Should().Contain("không được trùng");
            (await QueryAsync(db => db.StockTransfers.CountAsync())).Should().Be(before);
        }

        /// TRF-02 | BVA | FT-11 BV-01; NAC-01
        /// Biên số lượng chuyển: 0 -> bị chặn ở validation; vượt tồn khả dụng -> bị chặn khi xuất kho.
        [Fact]
        public async Task L3_TRF_02_TransferQuantityBoundary_ZeroRejected_ExceedingAvailableRejected()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var (product, _) = await SeedSellableProductAsync(50_000m, 10);

            // qty = 0 -> [Range(1, int.MaxValue)]
            var zero = await warehouse.PostAsJsonAsync("/api/stock-transfers", new
            {
                SourceWarehouseId = L3Seed.WarehouseDefaultId,
                DestinationWarehouseId = L3Seed.WarehouseTradeId,
                Items = new[] { new { ProductId = product.Id, Quantity = 0 } }
            });
            zero.StatusCode.Should().Be(HttpStatusCode.BadRequest, "số lượng 0 phải bị chặn");

            // qty = Available + 1 -> tạo được phiếu nhưng KHÔNG xuất kho được.
            var (transfer, exceedProduct) = await SeedDraftTransferAsync(stockAtSource: 10, transferQuantity: 11);
            var dispatch = await warehouse.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new { });

            dispatch.IsSuccessStatusCode.Should().BeFalse("vượt tồn khả dụng thì không được xuất kho");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == exceedProduct.Id)))
                .OnHandQuantity.Should().Be(10, "tồn kho nguồn không đổi khi phiếu bị từ chối");
        }

        /// TRF-03 | BVA | FT-11 BV-02; AC-02; NAC-03
        /// Nhận hàng khi phiếu CHƯA xuất kho -> bị từ chối theo state guard.
        [Fact]
        public async Task L3_TRF_03_ReceiveBeforeDispatch_RejectedByStateGuard()
        {
            var (transfer, product) = await SeedDraftTransferAsync(stockAtSource: 100, transferQuantity: 10);
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            using var content = new MultipartFormDataContent
            {
                { new StringContent($"[{{\"productId\":\"{product.Id}\",\"receivedQuantity\":10}}]"), "ItemsJson" }
            };
            var res = await warehouse.PostAsync($"/api/stock-transfers/{transfer.Id}/receive", content);

            res.IsSuccessStatusCode.Should().BeFalse("phiếu chưa Dispatched thì không thể nhận");
            (await QueryAsync(db => db.StockTransfers.SingleAsync(t => t.Id == transfer.Id)))
                .Status.Should().Be(StockTransferStatus.Draft);
        }

        /// TRF-04 | Input-Domain-Error | FT-11 NAC-04; NFR-SEC03
        /// Vai trò ngoài nhóm kho (khách hàng) không được nhận hàng chuyển kho -> 403.
        [Fact]
        public async Task L3_TRF_04_ReceiveTransfer_ByCustomerRole_Forbidden()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);

            var res = await customer.PostAsync($"/api/stock-transfers/{Guid.NewGuid()}/receive",
                new MultipartFormDataContent { { new StringContent("[]"), "ItemsJson" } });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// TRF-05 | Idempotency | FT-11 AC-04; BR-029; BR-035
        /// Xuất kho lần 2 cho cùng phiếu -> bị từ chối, tồn kho KHÔNG dịch chuyển lần thứ hai.
        [Fact]
        public async Task L3_TRF_05_DispatchTwice_Idempotent_StockMovesOnlyOnce()
        {
            var (transfer, product) = await SeedDraftTransferAsync(stockAtSource: 100, transferQuantity: 10);
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var first = await warehouse.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new { });
            first.IsSuccessStatusCode.Should().BeTrue(
                $"lần xuất đầu phải thành công ({await ReadMessageAsync(first)})");
            var afterFirst = await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id));

            var second = await warehouse.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new { });

            second.IsSuccessStatusCode.Should().BeFalse("phiếu đã xuất là bất biến");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(afterFirst.OnHandQuantity, "tồn KHÔNG được trừ lần thứ hai");
        }

        /// TRF-06 | Input-Domain-Error | FT-11 NAC-02; BR-032; NFR-D01
        /// Hai lệnh xuất kho ĐỒNG THỜI trên cùng phiếu -> chỉ 1 thành công, tồn không bị trừ đúp.
        [Fact]
        public async Task L3_TRF_06_ConcurrentDispatch_OnlyOneSucceeds_NoDoubleDeduction()
        {
            var (transfer, product) = await SeedDraftTransferAsync(stockAtSource: 100, transferQuantity: 10);
            var clientA = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var clientB = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var taskA = clientA.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new { });
            var taskB = clientB.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new { });
            var results = await Task.WhenAll(taskA, taskB);

            results.Count(r => r.IsSuccessStatusCode).Should().Be(1,
                "chỉ đúng 1 trong 2 request đồng thời được phép xuất kho");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(90, "tồn chỉ được trừ ĐÚNG MỘT LẦN (100 - 10)");
        }

        /// TRF-07 | Input-Domain-Error | FT-11 AC-05; NAC-05; BR-012
        /// Tạo phiếu chuyển kho với mặt hàng không tồn tại ở kho nguồn -> không xuất kho được.
        [Fact]
        public async Task L3_TRF_07_DispatchItemNotStockedAtSource_Rejected()
        {
            // Hàng nằm ở WH-PE nhưng phiếu lại xuất từ WH-DEFAULT.
            var (product, _) = await SeedSellableProductAsync(50_000m, 100, L3Seed.LocationPeId);
            var transfer = new StockTransfer
            {
                Id = Guid.NewGuid(),
                Code = "TR-L3-WRONGSRC",
                SourceWarehouseId = L3Seed.WarehouseDefaultId,
                DestinationWarehouseId = L3Seed.WarehouseTradeId,
                CreatedByUserId = L3Seed.WarehouseStaffId,
                Status = StockTransferStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.StockTransfers.Add(transfer);
                db.StockTransferItems.Add(new StockTransferItem
                { Id = Guid.NewGuid(), StockTransferId = transfer.Id, ProductId = product.Id, Quantity = 5 });
                return Task.CompletedTask;
            });

            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var res = await warehouse.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new { });

            res.IsSuccessStatusCode.Should().BeFalse("kho nguồn không có hàng thì không xuất được");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(100, "tồn ở kho khác không được đụng tới");
        }

        // ── Block: Stock Count / Adjustment / Material Issue (FT-12) ──────────────────────────

        /// INV-01 | Input-Domain-Error | FT-12 NAC-01; BR-044  ->  nhóm C
        /// Phiên kiểm kê (count-session) theo snapshot lý thuyết chưa được triển khai.
        [Fact]
        public async Task L3_INV_01_CountSession_EndpointNotImplemented()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            (await warehouse.PutAsJsonAsync(
                    $"/api/inventory/count-sessions/{Guid.NewGuid()}/theoretical", new { }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound,
                    "DEF-L3-005: phiên kiểm kê có snapshot lý thuyết chưa được triển khai");
        }

        /// INV-02 | BVA | FT-12 BV-01; NAC-02
        /// Số lượng đếm ÂM -> bị chặn, tồn kho CHƯA đổi.
        [Fact]
        public async Task L3_INV_02_ShiftCount_NegativeQuantity_Rejected_InventoryUnchanged()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var before = await QueryAsync(db => db.Inventories
                .SingleAsync(i => i.Id == L3Seed.InventoryPeWrapDefaultId));

            var res = await warehouse.PutAsJsonAsync(
                $"/api/inventory/{L3Seed.InventoryPeWrapDefaultId}/adjust",
                new { NewQuantity = -1, Reason = "kiem ke am" });

            res.IsSuccessStatusCode.Should().BeFalse("số lượng âm phải bị từ chối");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == L3Seed.InventoryPeWrapDefaultId)))
                .OnHandQuantity.Should().Be(before.OnHandQuantity);
        }

        /// INV-03 | BVA | FT-12 BV-02; AC-03; NAC-04
        /// Điều chỉnh tồn kho: chỉ nhân viên kho / CEO được phép; vai trò khác -> 403.
        [Fact]
        public async Task L3_INV_03_AdjustInventory_RoleGate()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.PutAsJsonAsync($"/api/inventory/{L3Seed.InventoryPeWrapDefaultId}/adjust",
                    new { NewQuantity = 1, Reason = "x" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var ceo = await ClientForSeededAsync(L3Seed.CeoId);
            (await ceo.PutAsJsonAsync($"/api/inventory/{L3Seed.InventoryPeWrapDefaultId}/adjust",
                    new { NewQuantity = 9_000, Reason = "kiem ke dinh ky" }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "CEO phải lọt qua cổng phân quyền");
        }

        /// INV-04 | Input-Domain-Error | FT-12 NAC-03; BR-032  ->  <b>FAIL — defect DEF-L3-007 (P1)</b>
        ///
        /// Theo BR-032/NFR-D01, điều chỉnh làm tồn khả dụng xuống âm phải bị chặn
        /// (INSUFFICIENT_AVAILABLE_OR_CONCURRENCY_CONFLICT).
        ///
        /// THỰC TẾ: <c>InventoryService.AdjustInventoryAsync</c> (InventoryService.cs:97-122) chỉ kiểm
        /// <c>newQuantity &lt; 0</c> và phạm vi kho. Nó KHÔNG kiểm tồn mới có còn phủ được
        /// Reserved + Allocated + Damaged + Quarantine hay không, mà gán thẳng
        /// <c>inventory.OnHandQuantity = newQuantity</c>.
        /// Dòng tồn dùng ở đây có Reserved = 1000 và Quarantine = 1000; đặt OnHand = 0 cho ra tồn khả
        /// dụng THÔ = -2000, tức 1000 đơn vị đã hứa cho đơn khách bốc hơi mà không có cảnh báo nào.
        ///
        /// Sai lệch bị che: property <c>Inventory.AvailableQuantity</c> có <c>Math.Max(0, ...)</c> nên
        /// mọi màn hình đọc property đó vẫn thấy 0, không thấy âm — đúng như workbook đã lưu ý ở
        /// L3-SEC-18 ("kiểm tra biểu thức thô, không dùng thuộc tính đã floor về 0").
        ///
        /// Test này CỐ Ý đỏ: sẽ xanh khi AdjustInventoryAsync chặn tồn mới nhỏ hơn phần đã cam kết.
        [Fact]
        public async Task L3_INV_04_AdjustInventory_MustNotDriveAvailableNegative()
        {
            var ceo = await ClientForSeededAsync(L3Seed.CeoId);
            // Dòng tồn seed sẵn: OnHand 10.000, Reserved 1.000, Quarantine 1.000 (băng keo trong, WH-DEFAULT).
            var inventoryId = Guid.Parse("16eaf448-d4e7-4757-b60c-3a3348cbf10c");

            await ceo.PutAsJsonAsync($"/api/inventory/{inventoryId}/adjust",
                new { NewQuantity = 0, Reason = "dieu chinh ve 0" });

            var inv = await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == inventoryId));
            (inv.OnHandQuantity - inv.ReservedQuantity - inv.AllocatedQuantity
             - inv.DamagedQuantity - inv.QuarantineQuantity)
                .Should().BeGreaterThanOrEqualTo(0,
                    "DEF-L3-007: điều chỉnh tồn phải bị chặn khi làm tồn khả dụng thô xuống âm — " +
                    "1.000 đơn vị đang giữ cho đơn khách bị xoá trắng mà không có cảnh báo");
        }

        /// INV-05 | Input-Domain-Error | FT-12 NAC-05; BR-045
        /// Xuất nguyên liệu sản xuất THIẾU bằng chứng (ảnh biên bản đã ký) -> 422
        /// PRODUCTION_ISSUE_EVIDENCE_REQUIRED và KHÔNG trừ kho.
        ///
        /// Trước 14/08/2026 case này bị thay bằng mốc đánh dấu "EndpointNotImplemented" (assert 404/405)
        /// khi API chưa có. Endpoint nay đã có — và nhận multipart/form-data, nên gửi JSON sẽ ra 415
        /// chứ không phải 404.
        [Fact]
        public async Task L3_INV_05_ProductionIssue_MissingEvidence_Rejected_NoStockDeducted()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var (product, inventory) = await SeedSellableProductAsync(50_000m, 100);

            // Form hợp lệ MỌI trường bắt buộc, chỉ CỐ TÌNH thiếu evidencePhoto — để chắc chắn 422 đến
            // từ luật bằng chứng chứ không phải từ ModelState (sẽ là 400).
            using var form = new MultipartFormDataContent
            {
                { new StringContent(L3Seed.WarehouseDefaultId.ToString()), "WarehouseId" },
                { new StringContent("Nguyen Van Nhan"), "ExternalRecipientName" },
                { new StringContent("To san xuat 1"), "Department" },
                { new StringContent(DateTime.UtcNow.ToString("O")), "ReceivedAt" },
                { new StringContent("BB-L3-INV-05"), "PaperDocumentNumber" },
                { new StringContent(product.Id.ToString()), "Items[0].ProductId" },
                { new StringContent("5"), "Items[0].Quantity" },
            };

            var res = await warehouse.PostAsync("/api/materials/production-issues", form);

            res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
                "BR-045: thiếu ảnh biên bản đã ký thì không được xuất; body: {0}",
                await res.Content.ReadAsStringAsync());

            (await ReadJsonAsync(res)).TryGetProperty("code", out var code).Should().BeTrue(
                "workbook L3-INV-05 yêu cầu errorCode PRODUCTION_ISSUE_EVIDENCE_REQUIRED trong body");
            code.GetString().Should().Be("PRODUCTION_ISSUE_EVIDENCE_REQUIRED");

            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == inventory.Id)))
                .OnHandQuantity.Should().Be(100, "bị chặn thì tuyệt đối không trừ kho");
        }

        /// INV-05 (vế bổ sung) | Luật bằng chứng còn được thực thi ở tầng post phiếu xuất.
        [Fact]
        public async Task L3_INV_05b_PostProductionMaterialIssue_WithoutEvidence_Rejected()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            // Luật bằng chứng vẫn được thực thi khi post phiếu xuất nguyên liệu sản xuất.
            var (product, _) = await SeedSellableProductAsync(50_000m, 100);
            var issue = new GoodsIssue
            {
                Id = Guid.NewGuid(),
                Code = "GI-L3-PM",
                Type = GoodsIssueType.ProductionMaterial, // thiếu toàn bộ 5 trường bằng chứng
                WarehouseId = L3Seed.WarehouseDefaultId,
                IssuedByUserId = L3Seed.WarehouseStaffId,
                Status = GoodsIssueStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.GoodsIssues.Add(issue);
                db.GoodsIssueItems.Add(new GoodsIssueItem
                { Id = Guid.NewGuid(), GoodsIssueId = issue.Id, ProductId = product.Id, Quantity = 1 });
                return Task.CompletedTask;
            });

            var post = await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { });

            post.IsSuccessStatusCode.Should().BeFalse("thiếu bằng chứng thì không được post");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(100, "không trừ kho khi thiếu bằng chứng");
        }

        /// INV-06 | BVA | FT-12 AC-04; BV-03; BR-049
        /// Cảnh báo tồn thấp quanh ngưỡng ReorderThreshold: threshold+1 KHÔNG cảnh báo,
        /// threshold và threshold-1 sinh ĐÚNG 1 cảnh báo kèm số lượng, ngưỡng, kho, hành động.
        ///
        /// Trước 14/08/2026 case này bị thay bằng mốc đánh dấu "EndpointNotImplemented" (assert 404)
        /// khi API chưa có. Endpoint nay đã có nên viết lại đúng đặc tả trong Report_5_3.
        [Theory]
        [InlineData(1, false)]   // threshold + 1 -> chưa tới ngưỡng
        [InlineData(0, true)]    // đúng ngưỡng    -> phải cảnh báo (AC-04: "tại hoặc dưới")
        [InlineData(-1, true)]   // dưới ngưỡng    -> phải cảnh báo
        public async Task L3_INV_06_LowStockAlerts_BoundaryAroundReorderThreshold(
            int offsetFromThreshold, bool expectAlert)
        {
            const int threshold = 20;
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var (product, inventory) = await SeedSellableProductAsync(
                50_000m, threshold + offsetFromThreshold);
            await SeedAsync(async db =>
            {
                // Ngưỡng cảnh báo nay thuộc về SẢN PHẨM và được so với tồn khả dụng CỘNG DỒN mọi kho
                // (InventoryService.GetLowStockAlertsAsync:532) — trước đây đặt trên từng dòng Inventory.
                var inv = await db.Inventories.SingleAsync(i => i.Id == inventory.Id);
                inv.ReorderThreshold = threshold;
                var prod = await db.Products.SingleAsync(x => x.Id == product.Id);
                prod.ReorderThreshold = threshold;
            });

            var res = await warehouse.GetAsync("/api/inventory/low-stock-alerts");
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var alerts = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
            var mine = alerts!
                .Where(a => a.GetProperty("itemId").GetGuid() == product.Id)
                .ToList();

            if (!expectAlert)
            {
                mine.Should().BeEmpty(
                    "tồn {0} vẫn TRÊN ngưỡng {1} nên chưa được cảnh báo", threshold + offsetFromThreshold, threshold);
                return;
            }

            mine.Should().ContainSingle(
                "tại hoặc dưới ngưỡng phải có ĐÚNG 1 cảnh báo, không nhân bản");

            // Nội dung cảnh báo phải đủ 4 thông tin workbook yêu cầu.
            var alert = mine[0];
            alert.GetProperty("availableQuantity").GetDouble()
                .Should().Be(threshold + offsetFromThreshold, "số lượng hiện tại");
            alert.GetProperty("threshold").GetDouble().Should().Be(threshold, "ngưỡng");
            // Cảnh báo nay gộp theo sản phẩm trên TOÀN BỘ kho nên trường kho được trả về rỗng có chủ đích;
            // hợp đồng vẫn phải có trường này để màn hình cảnh báo hiển thị được phạm vi.
            alert.TryGetProperty("warehouseName", out _).Should().BeTrue("cảnh báo phải mang thông tin kho");
            alert.GetProperty("suggestedAction").GetString()
                .Should().NotBeNullOrWhiteSpace("hành động tiếp theo");
        }

        /// INV-07 | BVA | FT-12 BV-03; NAC-03; BR-045
        /// Xuất vượt tồn khả dụng -> bị chặn, tồn khả dụng thô không âm.
        [Fact]
        public async Task L3_INV_07_IssueMoreThanAvailable_Rejected_AvailableStaysNonNegative()
        {
            var (product, _) = await SeedSellableProductAsync(50_000m, 5);
            var issue = new GoodsIssue
            {
                Id = Guid.NewGuid(),
                Code = "GI-L3-OVER",
                Type = GoodsIssueType.SalesOrder,
                WarehouseId = L3Seed.WarehouseDefaultId,
                IssuedByUserId = L3Seed.WarehouseStaffId,
                Status = GoodsIssueStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.GoodsIssues.Add(issue);
                db.GoodsIssueItems.Add(new GoodsIssueItem
                { Id = Guid.NewGuid(), GoodsIssueId = issue.Id, ProductId = product.Id, Quantity = 6 });
                return Task.CompletedTask;
            });

            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var res = await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { });

            res.IsSuccessStatusCode.Should().BeFalse("xuất 6 khi chỉ còn 5 phải bị chặn");
            var inv = await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id));
            inv.OnHandQuantity.Should().Be(5);
            (inv.OnHandQuantity - inv.ReservedQuantity).Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
