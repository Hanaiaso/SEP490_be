using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Batch 2 — tranh chấp tồn kho trên SQL Server THẬT.
    ///
    /// Vì sao phải là L2: L1-RES-06 chạy trên EF InMemory/SQLite nên hai lệnh ghi bị serialise,
    /// test xanh kể cả khi guarded UPDATE bị gỡ. Chỉ SQL Server thật mới cho hai transaction
    /// chạm nhau và lộ ra row-lock có thật hay không.
    ///
    /// ⚠ Lưu ý khi đọc assertion: <c>Inventory.AvailableQuantity</c> (Inventory.cs:24) là computed
    /// property C# bọc <c>Math.Max(0, ...)</c> — nó KHÔNG BAO GIỜ âm được, kể cả khi đã oversell.
    /// Vì vậy assert "không âm" phải làm trên số THÔ (OnHandQuantity và biểu thức chưa kẹp Math.Max),
    /// nếu không sẽ xanh giả.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2InventoryContentionTests : SqlServerTestBase
    {
        public L2InventoryContentionTests(SqlServerFixture factory) : base(factory) { }

        private const int Iterations = 5;
        private const int LastUnits = 10;

        /// <summary>Số khả dụng THÔ — không kẹp Math.Max, để phát hiện oversell mà AvailableQuantity che mất.</summary>
        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        /// <summary>Đưa 1 dòng Inventory về đúng <paramref name="units"/> sản phẩm cuối cùng.</summary>
        private async Task<(Guid ProductId, Guid InventoryId, Guid WarehouseId)> SetUpLastUnitsAsync(int units)
        {
            Guid productId = Guid.Empty, inventoryId = Guid.Empty, warehouseId = Guid.Empty;

            await SeedAsync(async db =>
            {
                var inv = await db.Inventories
                    .Include(i => i.WarehouseLocation)
                    .FirstAsync(i => i.ProductId != null);

                inv.OnHandQuantity = units;
                inv.ReservedQuantity = 0;
                inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0;
                inv.QuarantineQuantity = 0;
                inv.InTransitQuantity = 0;

                productId = inv.ProductId!.Value;
                inventoryId = inv.Id;
                warehouseId = inv.WarehouseLocation.WarehouseId;

                // Các dòng tồn khác của cùng sản phẩm phải về 0, nếu không bên thua vẫn tìm được
                // hàng ở vị trí khác và case mất ý nghĩa.
                var others = await db.Inventories
                    .Where(i => i.ProductId == productId && i.Id != inv.Id)
                    .ToListAsync();
                foreach (var o in others)
                {
                    o.OnHandQuantity = 0;
                    o.ReservedQuantity = 0;
                    o.AllocatedQuantity = 0;
                    o.InTransitQuantity = 0;
                }
            });

            return (productId, inventoryId, warehouseId);
        }

        private Task<Inventory> ReloadInventoryAsync(Guid inventoryId) =>
            QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == inventoryId));

        // ── L2-FUL-08 ──────────────────────────────────────────────────────────────────────

        // GIVEN  kho chỉ còn đúng 10 sản phẩm cuối cùng (AvailableQuantity = 10)
        // WHEN   hai nhân viên bán hàng cùng lập đơn tại quầy cho trọn 10 sản phẩm đó, đồng thời
        // THEN   BR-032: đúng 1 đơn thành công, bên thua báo thiếu tồn; tồn về 0 và KHÔNG âm; đúng 1 hoá đơn
        [Fact]
        [Trait("TestID", "L2-FUL-08")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BR-032")]
        public async Task L2_FUL_08_TwoDirectOrdersForLastUnits_OnlyOneSucceeds()
        {
            for (var iteration = 1; iteration <= Iterations; iteration++)
            {
                await ResetAsync();
                var (productId, inventoryId, _) = await SetUpLastUnitsAsync(LastUnits);

                // Client phải tạo SAU ResetAsync vì reset xoá sạch Users.
                var (staffA, _) = await CreateClientAsAsync(SystemRole.SalesStaff);
                var (staffB, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

                PlaceDirectOrderRequestDto Request(string phone) => new()
                {
                    CustomerName = $"Khach quay {phone}",
                    PhoneNumber = phone,
                    Address = "Tai quay",
                    TotalAmount = LastUnits * 100_000m,
                    DiscountAmount = 0m,
                    VatAmount = 0m,
                    FinalPayment = LastUnits * 100_000m,
                    PaymentMethod = PaymentMethod.Cash,
                    Items = new List<DirectOrderItemDto>
                    {
                        new() { ProductId = productId, Quantity = LastUnits, Price = 100_000m }
                    }
                };

                // Barrier: hai request cùng chờ một TaskCompletionSource rồi mới bắn.
                var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var taskA = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await staffA.PostAsJsonAsync("/api/orders/place-direct-order", Request("0900000001"));
                });
                var taskB = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await staffB.PostAsJsonAsync("/api/orders/place-direct-order", Request("0900000002"));
                });

                barrier.SetResult();
                var responses = await Task.WhenAll(taskA, taskB);

                // (a) HTTP — đúng 1 bên thắng
                var succeeded = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
                var failed = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();

                var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));

                succeeded.Should().HaveCount(1,
                    "vòng {0}: chỉ có 10 sản phẩm cuối nên đúng 1 đơn được thành công. Các phản hồi: {1}",
                    iteration, string.Join(" || ", responses.Select((r, i) => $"{(int)r.StatusCode}:{bodies[i]}")));

                failed.Should().HaveCount(1);
                (await failed[0].Content.ReadAsStringAsync()).Should().MatchRegex(
                    "(?i)(depleted|không đủ|khong du|thiếu|refresh)",
                    "vòng {0}: bên thua phải nhận lỗi thiếu tồn rõ ràng", iteration);

                // (b) DB — scope MỚI, không tin response
                var inventory = await ReloadInventoryAsync(inventoryId);

                inventory.OnHandQuantity.Should().Be(0,
                    "vòng {0}: 10 sản phẩm cuối đã bán hết cho đúng 1 đơn", iteration);
                inventory.OnHandQuantity.Should().BeGreaterOrEqualTo(0,
                    "vòng {0}: tồn vật lý không bao giờ được âm", iteration);
                RawAvailable(inventory).Should().BeGreaterOrEqualTo(0,
                    "vòng {0}: khả dụng THÔ âm nghĩa là đã oversell (AvailableQuantity che mất bằng Math.Max)",
                    iteration);
                inventory.AvailableQuantity.Should().Be(0, "vòng {0}", iteration);

                // (c) side effect — đúng 1 hoá đơn / 1 đơn bán trực tiếp được ghi nhận
                var directOrders = await QueryAsync(db => db.Orders.AsNoTracking()
                    .Where(o => o.IsExternalOrder).ToListAsync());
                directOrders.Should().ContainSingle("vòng {0}: chỉ được phát sinh đúng 1 hoá đơn", iteration);

                var orderItems = await QueryAsync(db => db.OrderItems.AsNoTracking()
                    .Where(oi => oi.ProductId == productId).ToListAsync());
                orderItems.Sum(oi => oi.Quantity).Should().Be(LastUnits,
                    "vòng {0}: tổng số lượng đã bán không được vượt quá tồn ban đầu", iteration);
            }
        }

        // ── L2-TRF-03 ──────────────────────────────────────────────────────────────────────

        // GIVEN  1 lô tồn chỉ còn 10 đơn vị, đồng thời có 1 phiếu điều chuyển Draft và 1 phiếu xuất kho
        //        Draft cùng đòi trọn 10 đơn vị đó
        // WHEN   dispatch phiếu điều chuyển và post phiếu xuất kho chạy song song
        // THEN   BR-032: đúng 1 bên thành công; tồn không âm; bên thua nhận lỗi tranh chấp/thiếu tồn
        [Fact]
        [Trait("TestID", "L2-TRF-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BR-032")]
        public async Task L2_TRF_03_TransferDispatchAndGoodsIssueOnSameStock_OnlyOneSucceeds()
        {
            for (var iteration = 1; iteration <= Iterations; iteration++)
            {
                await ResetAsync();
                var (productId, inventoryId, sourceWarehouseId) = await SetUpLastUnitsAsync(LastUnits);

                var (warehouseClient, warehouseStaff) = await CreateClientAsAsync(SystemRole.WarehouseStaff);

                var transferId = Guid.NewGuid();
                var goodsIssueId = Guid.NewGuid();

                await SeedAsync(async db =>
                {
                    var destinationWarehouse = new Warehouse
                    {
                        Id = Guid.NewGuid(),
                        Name = $"Kho dich {iteration}",
                        Code = $"WH-DEST-{Guid.NewGuid():N}"[..20]
                    };
                    db.Warehouses.Add(destinationWarehouse);

                    db.StockTransfers.Add(new StockTransfer
                    {
                        Id = transferId,
                        Code = $"TR-{Guid.NewGuid():N}"[..20],
                        SourceWarehouseId = sourceWarehouseId,
                        DestinationWarehouseId = destinationWarehouse.Id,
                        CreatedByUserId = warehouseStaff.Id,
                        Status = StockTransferStatus.Draft,
                        CreatedAt = DateTime.UtcNow,
                        Items = new List<StockTransferItem>
                        {
                            new() { ProductId = productId, Quantity = LastUnits }
                        }
                    });

                    db.GoodsIssues.Add(new GoodsIssue
                    {
                        Id = goodsIssueId,
                        Code = $"GI-{Guid.NewGuid():N}"[..20],
                        // Type = Other để tránh bộ kiểm bằng chứng A1 của ProductionMaterial —
                        // case này kiểm tranh chấp tồn kho, không kiểm chứng từ.
                        Type = GoodsIssueType.Other,
                        WarehouseId = sourceWarehouseId,
                        IssuedByUserId = warehouseStaff.Id,
                        Status = GoodsIssueStatus.Draft,
                        CreatedAt = DateTime.UtcNow,
                        Items = new List<GoodsIssueItem>
                        {
                            new() { ProductId = productId, Quantity = LastUnits }
                        }
                    });

                    await Task.CompletedTask;
                });

                // Barrier
                var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var dispatchTask = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await warehouseClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null);
                });
                var issueTask = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await warehouseClient.PostAsync($"/api/goods-issues/{goodsIssueId}/post", null);
                });

                barrier.SetResult();
                var responses = await Task.WhenAll(dispatchTask, issueTask);
                var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));

                // (a) HTTP — đúng 1 bên thắng
                var succeeded = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();

                succeeded.Should().HaveCount(1,
                    "vòng {0}: chỉ có 10 đơn vị nên chỉ 1 trong 2 nghiệp vụ được xuất. Các phản hồi: {1}",
                    iteration, string.Join(" || ", responses.Select((r, i) => $"{(int)r.StatusCode}:{bodies[i]}")));

                var loserIndex = Array.FindIndex(responses, r => r.StatusCode != HttpStatusCode.OK);
                responses[loserIndex].StatusCode.Should().BeOneOf(
                    new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest },
                    "vòng {0}: bên thua phải nhận xung đột tranh chấp hoặc thiếu tồn", iteration);
                bodies[loserIndex].Should().MatchRegex(
                    "(?i)(không đủ|khong du|thay đổi bởi tác vụ khác|concurrency|insufficient)",
                    "vòng {0}: bên thua phải nhận thông báo tranh chấp tồn kho rõ ràng", iteration);

                // (b) DB — scope MỚI
                var inventory = await ReloadInventoryAsync(inventoryId);

                inventory.OnHandQuantity.Should().BeGreaterOrEqualTo(0,
                    "vòng {0}: tồn vật lý không bao giờ được âm", iteration);
                RawAvailable(inventory).Should().BeGreaterOrEqualTo(0,
                    "vòng {0}: khả dụng THÔ âm nghĩa là cả hai nghiệp vụ đều đã trừ tồn", iteration);
                inventory.OnHandQuantity.Should().Be(0,
                    "vòng {0}: đúng một bên xuất trọn 10 đơn vị", iteration);

                // (c) side effect — đúng 1 chứng từ chuyển trạng thái
                var transferStatus = await QueryAsync(db => db.StockTransfers.AsNoTracking()
                    .Where(t => t.Id == transferId).Select(t => t.Status).FirstAsync());
                var issueStatus = await QueryAsync(db => db.GoodsIssues.AsNoTracking()
                    .Where(g => g.Id == goodsIssueId).Select(g => g.Status).FirstAsync());

                var dispatched = transferStatus == StockTransferStatus.Dispatched;
                var posted = issueStatus == GoodsIssueStatus.Posted;

                (dispatched ^ posted).Should().BeTrue(
                    "vòng {0}: đúng một chứng từ được hoàn tất (transfer={1}, goodsIssue={2})",
                    iteration, transferStatus, issueStatus);
            }
        }
    }
}
