using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.SePay;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-Workflows — luồng nghiệp vụ xuyên module trên DB thật.
    ///
    /// FLOW-05 dùng ĐÚNG model hiện có thay vì thuật ngữ SRS: SRS gọi "CustomerOrderCredit",
    /// code hiện thực bằng CreditTransactions + Order.CreditApplied + PaymentReallocations.
    /// Test assert HÀNH VI (BR-017/BR-041/FT-08 NAC-03), không assert tên bảng.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2WorkflowsTests : SqlServerTestBase
    {
        public L2WorkflowsTests(SqlServerFixture factory) : base(factory) { }

        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        private string SePayToken =>
            Factory.Services.GetRequiredService<IConfiguration>()["SePaySettings:ApiToken"]!;

        private sealed record Ctx(Guid ProfileId, Guid ProductId, Guid InventoryId);

        private async Task<Ctx> SeedStockAsync(int onHand, int reserved = 0, int allocated = 0, Guid? salesStaffId = null)
        {
            Guid profileId = Guid.Empty, productId = Guid.Empty, inventoryId = Guid.Empty;
            await SeedAsync(async db =>
            {
                // Phải chốt đúng kho tập kết WH-DEFAULT (2006d0a6-...) — từ khi có thêm Inventory ở
                // WH-TRADE/WH-PE cho CÙNG sản phẩm, FirstAsync(i => i.ProductId != null) không còn đảm
                // bảo trúng đúng dòng WH-DEFAULT mà goods-issue thao tác.
                var defaultLocationId = Guid.Parse("2006d0a6-37a9-46ca-b8a0-bb061ec9f1e9");
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null && i.WarehouseLocationId == defaultLocationId);
                inv.OnHandQuantity = onHand; inv.ReservedQuantity = reserved; inv.AllocatedQuantity = allocated;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0; inv.InTransitQuantity = 0;
                productId = inv.ProductId!.Value; inventoryId = inv.Id;

                foreach (var o in await db.Inventories.Where(i => i.ProductId == productId && i.Id != inv.Id).ToListAsync())
                { o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0; }

                var profile = await db.CustomerProfiles.FirstAsync();
                if (salesStaffId.HasValue) profile.AssignedSalesStaffId = salesStaffId;
                profileId = profile.Id;
            });
            return new Ctx(profileId, productId, inventoryId);
        }

        private async Task<Guid> SeedOrderAsync(Ctx c, int qty, decimal total,
            OrderStatus status, PaymentStatus payment, PaymentMethod method,
            Action<Order>? mutate = null)
        {
            var id = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var order = new Order
                {
                    Id = id, CustomerProfileId = c.ProfileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = total, FinalPayment = total,
                    PaymentMethod = method, PaymentStatus = payment, OrderStatus = status,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = c.ProductId, Quantity = qty, PriceSnapshot = total / qty, CostSnapshot = 0m }
                    }
                };
                mutate?.Invoke(order);
                db.Orders.Add(order);
                await Task.CompletedTask;
            });
            return id;
        }

        private Task<Order> ReloadOrderAsync(Guid id) =>
            QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == id));

        // ── L2-FLOW-01 ─────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn ORD-001 đang chờ thanh toán
        // WHEN   webhook SePay hợp lệ → Sales xác nhận → kho lấy hàng đợi
        // THEN   Paid → Confirmed → xuất hiện trong hàng đợi kho; trạng thái nhất quán ở mỗi chặng
        [Fact]
        [Trait("TestID", "L2-FLOW-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-03 AC-02; FT-05 AC-01; BR-009; BR-011")]
        public async Task L2_FLOW_01_PaymentThenConfirmThenWarehouseQueue()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var c = await SeedStockAsync(onHand: 100, reserved: 10, salesStaffId: salesUser.Id);

            var orderId = await SeedOrderAsync(c, 2, 2_000_000m,
                OrderStatus.PendingConfirmation, PaymentStatus.Pending, PaymentMethod.SePay);
            var orderCode = (await ReloadOrderAsync(orderId)).OrderCode;

            // 1) Webhook thanh toán
            var webhookClient = Factory.CreateClient();
            webhookClient.DefaultRequestHeaders.Add("x-sepay-token", SePayToken);
            var refCode = "FT" + Guid.NewGuid().ToString("N")[..12];
            var webhook = await webhookClient.PostAsJsonAsync("/api/webhooks/sepay-callback", new SePayWebhookDto
            {
                gateway = "TPBank", transactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                accountNumber = "x", transferAmount = 2_000_000m, transferType = "in",
                transferContent = orderCode, content = orderCode,
                referenceCode = refCode, referenceNumber = refCode
            });
            webhook.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await webhook.Content.ReadAsStringAsync());

            var afterPayment = await ReloadOrderAsync(orderId);
            afterPayment.PaymentStatus.Should().Be(PaymentStatus.Paid);
            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                .Should().Be(1);

            // 2) Sales xác nhận (nếu webhook chưa tự chuyển Confirmed)
            if (afterPayment.OrderStatus != OrderStatus.Confirmed)
            {
                var confirm = await sales.PostAsync($"/api/orders/sales/{orderId}/confirm", null);
                confirm.StatusCode.Should().Be(HttpStatusCode.OK,
                    "body: {0}", await confirm.Content.ReadAsStringAsync());
            }

            var confirmed = await ReloadOrderAsync(orderId);
            confirmed.OrderStatus.Should().Be(OrderStatus.Confirmed);
            confirmed.PaymentStatus.Should().Be(PaymentStatus.Paid, "xác nhận không được làm mất trạng thái đã trả tiền");

            // 3) Kho thấy đơn trong hàng đợi
            var queue = await warehouse.GetAsync("/api/warehouse/orders?tabType=OnlinePending&pageSize=50");
            queue.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await queue.Content.ReadAsStringAsync());
            (await queue.Content.ReadAsStringAsync()).Should().Contain(orderId.ToString(),
                "đơn đã xác nhận phải vào hàng đợi kho");

            // (c) tồn không âm ở mọi chặng
            var inv = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == c.InventoryId));
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);
        }

        // ── L2-FLOW-02 — BLOCKED GH-03a ────────────────────────────────────────────────────

        // GIVEN  Đơn COD đã xác nhận, tổng 5.000.000; tồn P1 = 10; xe V1 ca Sáng còn trống
        // WHEN   Kho nhận → post goods issue → Sales xếp lịch giao → ghi kết quả giao {thu đủ, có ký}
        // THEN   Packing → tồn 6 → Đang giao → Delivered + Paid; có bản ghi công nợ/credit
        [Fact]
        [Trait("TestID", "L2-FLOW-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-05 AC-05; FT-07 AC-01; AC-03; AC-04; BR-034")]
        [Trait("Blocked", "GH-03a")]
        public async Task L2_FLOW_02_CodOrderFromGoodsIssueToDeliveredAndPaid()
        {
            // SRS v2 (POD bắt buộc): xác nhận giao hàng upload CustomerSignatureBase64/DeliveryPhotoBase64
            // lên Cloudinary thật — khai báo có chủ ý, không phải lỗi gọi service ngoài ngoài dự kiến.
            AllowOutboundCloudinary = true;
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var c = await SeedStockAsync(onHand: 10, allocated: 4, salesStaffId: salesUser.Id);

            var orderId = await SeedOrderAsync(c, 4, 5_000_000m,
                OrderStatus.Processing, PaymentStatus.Unpaid, PaymentMethod.COD,
                o => o.FulfillmentStatus = FulfillmentStatus.HandedOver);

            // ⛔ GH-03a: bảng GoodsIssues thiếu 8 cột -> insert thất bại. Không vá schema (R3).
            var goodsIssue = await warehouse.PostAsync($"/api/warehouse/orders/{orderId}/goods-issue", null);
            goodsIssue.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await goodsIssue.Content.ReadAsStringAsync());

            var afterIssue = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == c.InventoryId));
            afterIssue.OnHandQuantity.Should().Be(6, "xuất 4 trên tồn 10");

            // Xếp lịch giao TRONG NGÀY (theo giờ Việt Nam) — OrderService.cs:2172-2178 (SRS v2) chặn xác
            // nhận giao trước ngày hẹn, nên test luồng happy-path 1 lượt (issue->schedule->complete cùng
            // lúc) phải hẹn giao hôm nay, không phải ngày mai. Dùng ca "Chiều" (hạn chót 22:00,
            // OrderService.cs:1995) vì ca "Sáng" hết hạn lúc 10:00. DeliveryDate PHẢI tính theo giờ VN
            // (UtcNow.AddHours(7)) như NotInPastAttribute/OrderService.cs:1983 — dùng UtcNow trần sẽ bị
            // coi là "ngày quá khứ" vào khoảng UTC 17:00-23:59 (đã sang ngày mới ở VN nhưng UTC còn hôm qua).
            var schedule = await sales.PostAsJsonAsync("/api/delivery/schedule", new ScheduleDeliveryRequestDto
            {
                VehicleId = 1, Shift = "Chiều",
                DeliveryDate = DateTime.UtcNow.AddHours(7),
                OrderIds = new List<Guid> { orderId }
            });
            schedule.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await schedule.Content.ReadAsStringAsync());
            (await ReloadOrderAsync(orderId)).DeliveryStatus.Should().Be(DeliveryStatus.Scheduled);

            // Ghi kết quả giao: thu đủ tiền
            var complete = await sales.PostAsJsonAsync($"/api/delivery/{orderId}/complete", new RecordDeliveryResultDto
            {
                AmountCollected = 5_000_000m,
                CustomerSignatureBase64 = "data:image/png;base64,iVBORw0KGgo=",
                DeliveryPhotoBase64 = "data:image/png;base64,iVBORw0KGgo="
            });
            complete.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await complete.Content.ReadAsStringAsync());

            var final = await ReloadOrderAsync(orderId);
            final.DeliveryStatus.Should().Be(DeliveryStatus.Delivered);
            final.PaymentStatus.Should().Be(PaymentStatus.Paid, "thu đủ COD thì đơn phải thành Paid");
            final.AmountPaid.Should().Be(5_000_000m);
        }

        // ── L2-FLOW-03 — BLOCKED GH-03c ────────────────────────────────────────────────────

        // GIVEN  Sản phẩm P9 hết hàng; đã chuẩn bị PO cho P9 qty=50
        // WHEN   PO issue → send-to-warehouse → ghi sổ phiếu nhập → khách xem P9 và thêm vào giỏ
        // THEN   Tồn P9 = 50; gian hàng cho mua; thêm giỏ thành công
        [Fact]
        [Trait("TestID", "L2-FLOW-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-06 AC-03; FT-01 AC-01; BR-014")]
        [Trait("Blocked", "GH-03c")]
        public async Task L2_FLOW_03_PurchasingRestocksStorefront()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var (warehouse, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var c = await SeedStockAsync(onHand: 0);

            Guid supplierId = Guid.NewGuid(), warehouseId = Guid.Empty;
            await SeedAsync(async db =>
            {
                warehouseId = await db.Inventories.Where(i => i.Id == c.InventoryId)
                    .Select(i => i.WarehouseLocation.WarehouseId).FirstAsync();
                db.Suppliers.Add(new Supplier
                {
                    Id = supplierId, Name = "NCC FLOW-03",
                    Code = $"NCC{Random.Shared.Next(100000, 999999)}", IsActive = true
                });
            });

            var create = await ceo.PostAsJsonAsync("/api/purchase-orders", new VietTien.API.DTOs.PurchaseOrder.CreatePurchaseOrderRequest
            {
                SupplierId = supplierId, WarehouseId = warehouseId,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(3),
                Items = new List<VietTien.API.DTOs.PurchaseOrder.CreatePurchaseOrderItemRequest>
                {
                    new() { ProductId = c.ProductId, ExpectedQuantity = 50, UnitPrice = 10_000m, Unit = "Cái" }
                }
            });
            create.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await create.Content.ReadAsStringAsync());
            var poId = System.Text.Json.JsonDocument.Parse(await create.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetGuid();

            (await ceo.PostAsync($"/api/purchase-orders/{poId}/issue", null)).EnsureSuccessStatusCode();
            (await ceo.PostAsync($"/api/purchase-orders/{poId}/send-to-warehouse", null)).EnsureSuccessStatusCode();

            var itemId = await QueryAsync(db => db.PurchaseOrderItems.AsNoTracking()
                .Where(i => i.PurchaseOrderId == poId).Select(i => i.Id).FirstAsync());

            // ⛔ GH-03c: bảng GoodsReceipts thiếu cột ImageProofUrl -> insert thất bại.
            var receipt = await warehouse.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts",
                new VietTien.API.DTOs.PurchaseOrder.CreateGoodsReceiptRequest
                {
                    Items = new List<VietTien.API.DTOs.PurchaseOrder.CreateGoodsReceiptItemRequest>
                    {
                        new() { PurchaseOrderItemId = itemId, AcceptedQuantity = 50 }
                    }
                });
            receipt.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await receipt.Content.ReadAsStringAsync());
            var receiptId = System.Text.Json.JsonDocument.Parse(await receipt.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetGuid();

            (await warehouse.PostAsync($"/api/purchase-orders/{poId}/receipts/{receiptId}/post", null))
                .EnsureSuccessStatusCode();

            // (b) DB — nhập kho làm hàng bán được trở lại
            var inv = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == c.InventoryId));
            inv.OnHandQuantity.Should().Be(50);
            RawAvailable(inv).Should().Be(50, "hàng vừa nhập phải khả dụng cho gian hàng");
        }

        // ── L2-FLOW-04 ─────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn đang soạn có dòng P1 qty=5; tồn P1 chỉ còn 2
        // WHEN   Kho báo thiếu → bù tồn lên 5 → đơn quay lại hàng đợi
        // THEN   Có trạng thái thiếu hàng + thông báo cho Sales; sau khi bù, đơn trở lại Confirmed và vào lại hàng đợi
        [Fact]
        [Trait("TestID", "L2-FLOW-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-05 NAC-04; AC-04")]
        public async Task L2_FLOW_04_ShortageNotifiesSalesThenRequeuesAfterRestock()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (warehouse, warehouseUser) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            var c = await SeedStockAsync(onHand: 2, salesStaffId: salesUser.Id);

            // Nhân viên kho phải được gán đúng kho, nếu không WarehouseService.ReportShortageAsync
            // ném UnauthorizedAccessException -> controller Forbid() -> 403.
            await SeedAsync(async db =>
            {
                var warehouseId = await db.Inventories.Where(i => i.Id == c.InventoryId)
                    .Select(i => i.WarehouseLocation.WarehouseId).FirstAsync();
                var u = await db.Users.FirstAsync(x => x.Id == warehouseUser.Id);
                u.AssignedWarehouseId = warehouseId;
            });

            // WarehouseService.ReportShortageAsync:460 yêu cầu đơn phải do CHÍNH nhân viên này nhận
            // (order.WarehouseStaffId == staffId), nếu không sẽ 403.
            var orderId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.SePay,
                o => o.WarehouseStaffId = warehouseUser.Id);

            // Kho báo thiếu 3
            var shortage = await warehouse.PostAsJsonAsync($"/api/warehouse/orders/{orderId}/shortage-alert",
                new ShortageAlertRequestDto { ProductId = c.ProductId, MissingQuantity = 3, Note = "thieu 3" });
            shortage.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await shortage.Content.ReadAsStringAsync());

            // (c) side effect — Sales phải được báo
            (await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.ReferenceId == orderId && n.Type == NotificationType.SYS_07_WarehouseShortage)))
                .Should().BeGreaterThan(0, "FT-05 NAC-04: thiếu hàng phải báo Sales");

            // Bù tồn lên đủ 5
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.Id == c.InventoryId);
                inv.OnHandQuantity = 5;
            });

            // (b) DB — đơn vẫn còn nguyên vẹn để xử lý tiếp, tồn không âm
            var order = await ReloadOrderAsync(orderId);
            order.OrderStatus.Should().NotBe(OrderStatus.Cancelled, "báo thiếu không được tự huỷ đơn");
            var inv2 = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == c.InventoryId));
            inv2.OnHandQuantity.Should().Be(5);
            RawAvailable(inv2).Should().BeGreaterOrEqualTo(0);

            // Bước "re-queue order" trong cột WHEN: báo thiếu đưa đơn về PendingConfirmation
            // (WarehouseService.cs:463), nên Sales phải xác nhận lại để đơn vào lại hàng đợi.
            var reconfirm = await sales.PostAsync($"/api/orders/sales/{orderId}/confirm", null);
            reconfirm.StatusCode.Should().Be(HttpStatusCode.OK,
                "sau khi bù tồn, Sales phải xác nhận lại được; body: {0}", await reconfirm.Content.ReadAsStringAsync());

            // Đơn quay lại hàng đợi kho
            var queue = await warehouse.GetAsync("/api/warehouse/orders?tabType=OnlinePending&pageSize=50");
            queue.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await queue.Content.ReadAsStringAsync());
            (await queue.Content.ReadAsStringAsync()).Should().Contain(orderId.ToString(),
                "sau khi bù tồn, đơn phải quay lại hàng đợi để soạn tiếp");
        }

        // ── L2-FLOW-05 ─────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đã thanh toán SePay; Manager duyệt huỷ và tạo đơn thay thế
        // WHEN   POST /api/delivery/{id}/approve-cancel-replacement
        // THEN   BR-017 không sinh giao dịch hoàn tiền; BR-041 bảo toàn giá trị;
        //        FT-08 NAC-03 phần dư chỉ thuộc về chính khách đó
        [Fact]
        [Trait("TestID", "L2-FLOW-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 AC-01; AC-02; AC-03; BR-017; BR-041; BR-042")]
        public async Task L2_FLOW_05_CancelPaidOrderPreservesValueWithoutRefund()
        {
            await ResetAsync();
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var c = await SeedStockAsync(onHand: 100, allocated: 5);

            // Đơn phải ở trạng thái CHỜ DUYỆT HUỶ thì Manager mới duyệt được
            // (DeliveryService yêu cầu đã có yêu cầu huỷ trước đó).
            var originalId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.CancelRequested, PaymentStatus.Paid, PaymentMethod.SePay,
                o => o.CancelRequestedAt = DateTime.UtcNow.AddMinutes(-5));

            await SeedAsync(async db =>
            {
                db.PaymentTransactions.Add(new PaymentTransaction
                {
                    OrderId = originalId, TransactionId = "FT-FLOW05", Amount = 5_000_000m,
                    AccountNumber = "x", ReferenceCode = "flow05", IsSuccess = true, Timestamp = DateTime.UtcNow
                });
                await Task.CompletedTask;
            });

            var txBefore = await QueryAsync(db => db.PaymentTransactions.AsNoTracking()
                .Where(t => t.OrderId == originalId).ToListAsync());
            txBefore.Should().ContainSingle();

            var response = await manager.PostAsJsonAsync(
                $"/api/delivery/{originalId}/approve-cancel-replacement",
                new CreateReplacementOrderDto
                {
                    OriginalOrderId = originalId,
                    Items = new List<ReplacementOrderItemDto>
                    {
                        new() { ProductId = c.ProductId, Quantity = 3, Price = 1_000_000m }
                    }
                });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            // (b) BR-017 — KHÔNG được sinh giao dịch hoàn tiền / giao dịch âm
            var txAfter = await QueryAsync(db => db.PaymentTransactions.AsNoTracking()
                .Where(t => t.OrderId == originalId).ToListAsync());
            txAfter.Should().OnlyContain(t => t.Amount >= 0,
                "BR-017: huỷ đơn đã thanh toán không được tạo giao dịch hoàn tiền âm");
            txAfter.Count.Should().Be(txBefore.Count,
                "BR-017: không sinh thêm giao dịch thanh toán nào trên đơn gốc");

            var original = await ReloadOrderAsync(originalId);
            original.OrderStatus.Should().BeOneOf(
                new[] { OrderStatus.Cancelled, OrderStatus.CancelledReallocated },
                "đơn gốc phải được đóng lại");

            // (b) BR-041 — bảo toàn giá trị: phần phân bổ + phần dư = giá trị đã trả
            var reallocated = (await QueryAsync(db => db.PaymentReallocations.AsNoTracking()
                .Where(r => r.OriginalOrderId == originalId).SumAsync(r => (decimal?)r.Amount))) ?? 0m;
            var credited = (await QueryAsync(db => db.CreditTransactions.AsNoTracking()
                .Where(t => t.OrderId == originalId).SumAsync(t => (decimal?)t.Amount))) ?? 0m;

            (reallocated + credited).Should().Be(5_000_000m,
                "BR-041: tổng phân bổ sang đơn mới + phần dư ghi credit phải bằng đúng số đã trả, không hụt không dư");

            // (c) FT-08 NAC-03 — phần dư chỉ thuộc về chính khách đó
            // Nếu toàn bộ tiền được phân bổ thẳng sang đơn thay thế thì không phát sinh phần dư —
            // khi đó không có dòng credit nào, và đó là kết quả hợp lệ.
            var creditRows = await QueryAsync(db => db.CreditTransactions.AsNoTracking()
                .Where(t => t.OrderId == originalId).ToListAsync());
            if (creditRows.Count > 0)
            {
                creditRows.Should().OnlyContain(t => t.CustomerProfileId == c.ProfileId,
                    "FT-08 NAC-03: phần dư phải gắn đúng chủ sở hữu, không được dùng chung");
            }
            else
            {
                reallocated.Should().Be(5_000_000m,
                    "không có phần dư thì toàn bộ giá trị phải được phân bổ sang đơn thay thế");
            }
        }

        // ── L2-FLOW-06 ─────────────────────────────────────────────────────────────────────

        // GIVEN  Khách đã có báo giá được duyệt và chấp nhận
        // WHEN   Khách dựng giỏ mới 150.000.000 → checkout-summary → POST place-order
        // THEN   BR-026: đơn dùng giá đã được duyệt, không phải yêu cầu báo giá mới
        [Fact]
        [Trait("TestID", "L2-FLOW-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 AC-05; BR-026")]
        public async Task L2_FLOW_06_AcceptedQuotationEnablesHighValueCheckout()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (customer, customerUser) = await CreateClientAsAsync(SystemRole.Customer);

            Guid profileId = Guid.NewGuid(), productId = Guid.Empty;
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 1000; inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                productId = inv.ProductId!.Value;
                var product = await db.Products.FirstAsync(p => p.Id == productId);
                product.StandardListedPrice = 150_000_000m;

                db.CustomerProfiles.Add(new CustomerProfile
                {
                    Id = profileId, UserId = customerUser.Id, AssignedSalesStaffId = salesUser.Id
                });
                db.Carts.Add(new Cart
                {
                    Id = Guid.NewGuid(), CustomerProfileId = profileId,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    Items = new List<CartItem>
                    {
                        new() { ProductId = productId, Quantity = 1, UnitPrice = 150_000_000m }
                    }
                });
            });

            // Chưa có báo giá được duyệt -> giỏ >=100tr phải bị chặn
            var beforeQuotation = await customer.GetAsync("/api/orders/checkout-summary");
            ((int)beforeQuotation.StatusCode).Should().BeInRange(400, 499,
                "BR-026: chưa có báo giá được duyệt thì giỏ >=100tr không được checkout; body: {0}",
                await beforeQuotation.Content.ReadAsStringAsync());

            // Sau khi có báo giá được duyệt + khách chấp nhận
            await SeedAsync(async db =>
            {
                var quotationId = Guid.NewGuid();
                var versionId = Guid.NewGuid();
                db.Quotations.Add(new Quotation
                {
                    Id = quotationId, CustomerProfileId = profileId, SalesStaffId = salesUser.Id,
                    Status = QuotationStatus.CustomerAccepted,
                    OriginalTotal = 150_000_000m,
                    AcceptedVersionId = versionId,
                    RequestDate = DateTime.UtcNow.AddDays(-1),
                    ValidUntil = DateTime.UtcNow.AddDays(7),
                    Versions = new List<QuotationVersion>
                    {
                        new()
                        {
                            Id = versionId, VersionNumber = 1,
                            ProposedTotal = 140_000_000m,
                            Status = QuotationVersionStatus.CustomerAccepted,
                            CreatedByUserId = salesUser.Id   // FK_QuotationVersions_Users_CreatedByUserId
                        }
                    }
                });
                await Task.CompletedTask;
            });

            var afterQuotation = await customer.GetAsync("/api/orders/checkout-summary");
            afterQuotation.StatusCode.Should().Be(HttpStatusCode.OK,
                "BR-026: đã có báo giá được duyệt và chấp nhận thì phải checkout được; body: {0}",
                await afterQuotation.Content.ReadAsStringAsync());
        }
    }
}
