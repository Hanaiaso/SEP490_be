using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-InventoryTransfer — điều chuyển liên kho + điều chỉnh tồn có vết audit.
    /// L2-TRF-03 nằm ở <see cref="L2InventoryContentionTests"/> (Blocked GH-03b).
    ///
    /// R4: assert trên OnHandQuantity và RawAvailable() thô, không assert AvailableQuantity
    /// (Inventory.cs:24 bọc Math.Max nên luôn xanh kể cả khi đã oversell).
    /// </summary>
    [Trait("Category", "L2")]
    public class L2InventoryTransferTests : SqlServerTestBase
    {
        public L2InventoryTransferTests(SqlServerFixture factory) : base(factory) { }

        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        private sealed record TransferFixture(
            Guid ProductId, Guid SourceWarehouseId, Guid DestWarehouseId,
            Guid SourceInventoryId, Guid DestInventoryId, Guid TransferId);

        /// <summary>Dựng 2 kho có tồn cùng 1 sản phẩm + 1 phiếu điều chuyển Draft.</summary>
        private async Task<TransferFixture> SeedTransferAsync(int sourceQty, int destQty, int transferQty, Guid createdBy)
        {
            Guid productId = Guid.Empty, srcWh = Guid.Empty, srcInv = Guid.Empty;
            var destWh = Guid.NewGuid();
            var destInv = Guid.NewGuid();
            var transferId = Guid.NewGuid();

            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.Include(i => i.WarehouseLocation).FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = sourceQty;
                inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0; inv.InTransitQuantity = 0;
                productId = inv.ProductId!.Value;
                srcWh = inv.WarehouseLocation.WarehouseId;
                srcInv = inv.Id;

                foreach (var o in await db.Inventories.Where(i => i.ProductId == productId && i.Id != inv.Id).ToListAsync())
                {
                    o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0; o.InTransitQuantity = 0;
                }

                var destLocationId = Guid.NewGuid();
                db.Warehouses.Add(new Warehouse
                {
                    Id = destWh, Name = "Kho dich L2-TRF", Code = $"WHD{Random.Shared.Next(100000, 999999)}"
                });
                db.WarehouseLocations.Add(new WarehouseLocation
                {
                    Id = destLocationId, WarehouseId = destWh, Name = $"LOC{Random.Shared.Next(100000, 999999)}", Type = "Normal"
                });
                db.Inventories.Add(new Inventory
                {
                    Id = destInv, ProductId = productId, WarehouseLocationId = destLocationId,
                    OnHandQuantity = destQty
                });
                db.StockTransfers.Add(new StockTransfer
                {
                    Id = transferId,
                    Code = $"TR{Random.Shared.Next(100000, 999999)}",
                    SourceWarehouseId = srcWh,
                    DestinationWarehouseId = destWh,
                    CreatedByUserId = createdBy,
                    Status = StockTransferStatus.Draft,
                    CreatedAt = DateTime.UtcNow,
                    Items = new List<StockTransferItem> { new() { ProductId = productId, Quantity = transferQty } }
                });
            });

            return new TransferFixture(productId, srcWh, destWh, srcInv, destInv, transferId);
        }

        private Task<Inventory> ReloadInvAsync(Guid id) =>
            QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == id));

        private static MultipartFormDataContent ReceiveForm(Guid productId, int receivedQty, string note)
        {
            var itemsJson = JsonSerializer.Serialize(new[]
            {
                new { productId, receivedQuantity = receivedQty }
            });
            return new MultipartFormDataContent
            {
                { new StringContent(itemsJson), "ItemsJson" },
                { new StringContent(note), "Note" }
            };
        }

        // ── L2-TRF-01 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Inv(P1,W1)=10; Inv(P1,W2)=2; phiếu T1 Draft qty=5
        // WHEN   POST dispatch T1; rồi POST receive T1 {received qty=5}
        // THEN   Sau dispatch: Inv(P1,W1)=5, T1=Dispatched; sau receive: Inv(P1,W2)=7, T1=Received
        [Fact]
        [Trait("TestID", "L2-TRF-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-11 AC-01; AC-02; BV-02; BR-035")]
        public async Task L2_TRF_01_DispatchThenReceiveMovesStockBetweenWarehouses()
        {
            await ResetAsync();
            var (client, staff) = await CreateClientAsAsync(SystemRole.Admin);
            var f = await SeedTransferAsync(sourceQty: 10, destQty: 2, transferQty: 5, createdBy: staff.Id);

            // --- Dispatch ---
            var dispatch = await client.PostAsync($"/api/stock-transfers/{f.TransferId}/dispatch", null);
            dispatch.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await dispatch.Content.ReadAsStringAsync());

            var srcAfterDispatch = await ReloadInvAsync(f.SourceInventoryId);
            srcAfterDispatch.OnHandQuantity.Should().Be(5, "kho xuất bị trừ đúng 5");
            srcAfterDispatch.InTransitQuantity.Should().Be(5, "phần đã xuất phải nằm ở hàng đi đường");
            RawAvailable(srcAfterDispatch).Should().BeGreaterOrEqualTo(0);

            (await QueryAsync(db => db.StockTransfers.AsNoTracking().FirstAsync(t => t.Id == f.TransferId)))
                .Status.Should().Be(StockTransferStatus.Dispatched);

            // --- Receive ---
            var receive = await client.PostAsync($"/api/stock-transfers/{f.TransferId}/receive",
                ReceiveForm(f.ProductId, 5, "nhan du"));
            receive.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await receive.Content.ReadAsStringAsync());

            // (b) DB — scope mới
            var dest = await ReloadInvAsync(f.DestInventoryId);
            dest.OnHandQuantity.Should().Be(7, "kho nhận từ 2 lên 7");
            RawAvailable(dest).Should().BeGreaterOrEqualTo(0);

            var src = await ReloadInvAsync(f.SourceInventoryId);
            src.InTransitQuantity.Should().Be(0, "nhận xong thì hàng đi đường phải về 0");
            src.OnHandQuantity.Should().BeGreaterOrEqualTo(0);

            var transfer = await QueryAsync(db => db.StockTransfers.AsNoTracking()
                .FirstAsync(t => t.Id == f.TransferId));
            transfer.Status.Should().Be(StockTransferStatus.Received);

            // (c) side effect — có vết biến động tồn kho
            (await QueryAsync(db => db.StockTransactions.CountAsync()))
                .Should().BeGreaterThan(0, "điều chuyển phải để lại StockTransaction");
        }

        // ── L2-TRF-02 ──────────────────────────────────────────────────────────────────────

        // GIVEN  T1 đã Dispatched qty=5
        // WHEN   POST receive T1 {received qty=6}
        // THEN   4xx hoặc ghi nhận chênh lệch; Inv(P1,W2) KHÔNG +6; kho nguồn giữ nguyên nhất quán
        [Fact]
        [Trait("TestID", "L2-TRF-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-11 NAC-03; BV-02")]
        public async Task L2_TRF_02_ReceivingMoreThanDispatchedIsRejected()
        {
            await ResetAsync();
            var (client, staff) = await CreateClientAsAsync(SystemRole.Admin);
            var f = await SeedTransferAsync(sourceQty: 10, destQty: 2, transferQty: 5, createdBy: staff.Id);

            (await client.PostAsync($"/api/stock-transfers/{f.TransferId}/dispatch", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var destBefore = await ReloadInvAsync(f.DestInventoryId);
            var srcBefore = await ReloadInvAsync(f.SourceInventoryId);

            // Nhận 6 > 5 đã xuất
            var receive = await client.PostAsync($"/api/stock-transfers/{f.TransferId}/receive",
                ReceiveForm(f.ProductId, 6, "nhan vuot"));

            // (a) HTTP
            ((int)receive.StatusCode).Should().BeInRange(400, 499,
                "nhận nhiều hơn đã xuất phải bị từ chối; body: {0}", await receive.Content.ReadAsStringAsync());

            // (b) DB — scope mới, không tin response
            var destAfter = await ReloadInvAsync(f.DestInventoryId);
            destAfter.OnHandQuantity.Should().Be(destBefore.OnHandQuantity,
                "kho nhận không được cộng số lượng chưa từng được xuất");
            destAfter.OnHandQuantity.Should().NotBe(destBefore.OnHandQuantity + 6);

            var srcAfter = await ReloadInvAsync(f.SourceInventoryId);
            srcAfter.OnHandQuantity.Should().Be(srcBefore.OnHandQuantity, "kho nguồn phải nhất quán");
            srcAfter.InTransitQuantity.Should().Be(srcBefore.InTransitQuantity);
            RawAvailable(srcAfter).Should().BeGreaterOrEqualTo(0);
            RawAvailable(destAfter).Should().BeGreaterOrEqualTo(0);

            (await QueryAsync(db => db.StockTransfers.AsNoTracking().FirstAsync(t => t.Id == f.TransferId)))
                .Status.Should().Be(StockTransferStatus.Dispatched, "phiếu chưa được chốt nhận");
        }

        // ── L2-TRF-04 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Inventory I1 (P1,W1) qty=10; JWT nhân viên kho
        // WHEN   PUT /api/inventory/{inventoryId}/adjust {newQuantity:7, note:'stocktake'}
        // THEN   qty=7; có bản ghi biến động delta -3 kèm người thực hiện và ghi chú; bản ghi cũ không bị sửa
        [Fact]
        [Trait("TestID", "L2-TRF-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-12 AC-03; BR-044; BR-022")]
        public async Task L2_TRF_04_AdjustInventoryWritesAppendOnlyAuditTrail()
        {
            await ResetAsync();
            var (client, staff) = await CreateClientAsAsync(SystemRole.WarehouseStaff);

            Guid inventoryId = Guid.Empty;
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.Include(i => i.WarehouseLocation)
                    .FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 10;
                inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0;
                inventoryId = inv.Id;

                // InventoryService.AdjustInventoryAsync yêu cầu nhân viên phải được gán ĐÚNG kho
                // (chỉ CEO được miễn) — nếu thiếu sẽ ném UnauthorizedAccessException -> Forbid() -> 403.
                var staffUser = await db.Users.FirstAsync(u => u.Id == staff.Id);
                staffUser.AssignedWarehouseId = inv.WarehouseLocation.WarehouseId;
            });

            var txBefore = await QueryAsync(db => db.StockTransactions.AsNoTracking().CountAsync());

            var response = await client.PutAsJsonAsync($"/api/inventory/{inventoryId}/adjust",
                new AdjustInventoryRequest { NewQuantity = 7, Note = "stocktake" });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB
            var inv = await ReloadInvAsync(inventoryId);
            inv.OnHandQuantity.Should().Be(7);
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);
            inv.LastUpdatedByUserId.Should().Be(staff.Id, "phải ghi ai là người điều chỉnh");

            // (c) side effect — vết biến động chỉ được THÊM, không sửa bản ghi cũ
            var txAfter = await QueryAsync(db => db.StockTransactions.AsNoTracking()
                .Where(t => t.InventoryId == inventoryId).ToListAsync());
            txAfter.Should().NotBeEmpty("điều chỉnh tồn phải để lại vết");
            txAfter.Should().Contain(t => t.QuantityChange == -3, "delta phải là -3 (10 -> 7)");
            txAfter.Should().Contain(t => t.CreatedByUserId == staff.Id, "vết phải gắn người thực hiện");
            (await QueryAsync(db => db.StockTransactions.AsNoTracking().CountAsync()))
                .Should().BeGreaterThan(txBefore, "audit chỉ được THÊM, không sửa/xoá bản ghi cũ");
        }

        // ── L2-TRF-05 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Inventory I1 qty=10
        // WHEN   PUT /api/inventory/{inventoryId}/adjust {newQuantity:-1}
        // THEN   4xx; qty không đổi; không sinh bản ghi audit
        [Fact]
        [Trait("TestID", "L2-TRF-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-12 NAC-03; BV-01; BR-032")]
        public async Task L2_TRF_05_AdjustToNegativeQuantityIsRejected()
        {
            await ResetAsync();
            var (client, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);

            Guid inventoryId = Guid.Empty;
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 10;
                inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0;
                inventoryId = inv.Id;
            });

            var txBefore = await QueryAsync(db => db.StockTransactions.CountAsync(t => t.InventoryId == inventoryId));

            var response = await client.PutAsJsonAsync($"/api/inventory/{inventoryId}/adjust",
                new AdjustInventoryRequest { NewQuantity = -1, Note = "so am" });

            // (a) HTTP
            ((int)response.StatusCode).Should().BeInRange(400, 499,
                "số lượng âm phải bị từ chối; body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB
            var inv = await ReloadInvAsync(inventoryId);
            inv.OnHandQuantity.Should().Be(10, "tồn không được đổi khi yêu cầu bị từ chối");
            inv.OnHandQuantity.Should().BeGreaterOrEqualTo(0);
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);

            // (c) side effect
            (await QueryAsync(db => db.StockTransactions.CountAsync(t => t.InventoryId == inventoryId)))
                .Should().Be(txBefore, "yêu cầu bị từ chối không được sinh vết audit");
        }
    }
}
