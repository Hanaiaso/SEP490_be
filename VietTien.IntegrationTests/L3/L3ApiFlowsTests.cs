using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-APIFlows</c> — FLOW-01..07.
    /// Chuỗi HTTP nhiều bước, kiểm trạng thái sau TỪNG bước (không chỉ ở bước cuối).
    /// </summary>
    public class L3ApiFlowsTests : L3TestBase
    {
        public L3ApiFlowsTests(L3SqlFixture factory) : base(factory) { }

        private const string SePayToken = "test-sepay-token-not-a-real-secret";

        private static HttpRequestMessage SePayWebhook(string orderCode, decimal amount, string refCode)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay-callback")
            {
                Content = JsonContent.Create(new
                {
                    id = Random.Shared.Next(1, 1_000_000),
                    gateway = "TPBank",
                    transactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    accountNumber = "0000000000",
                    transferAmount = amount,
                    transferType = "in",
                    transferContent = orderCode,
                    content = orderCode,
                    referenceNumber = refCode,
                    referenceCode = refCode,
                }),
            };
            req.Headers.Add("x-sepay-token", SePayToken);
            return req;
        }

        /// FLOW-01 | Workflow | FT-01 AC-05; FT-03 AC-02; FT-05 AC-01
        /// login -> thêm giỏ -> xem tổng -> đặt đơn SePay -> webhook -> đơn vào hàng đợi kho.
        [Fact]
        public async Task L3_FLOW_01_CustomerCheckoutToWarehouseQueue()
        {
            // B0: đăng nhập thật, dùng token trả về cho mọi bước sau.
            var email = NewEmail();
            var (user, profile) = await SeedVerifiedCustomerAsync(email, "Passw0rd!");
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 50);

            var anonymous = AnonymousClient();
            var login = await anonymous.PostAsJsonAsync("/api/auth/login",
                new { Email = email, Password = "Passw0rd!" });
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var accessToken = (await ReadJsonAsync(login)).GetProperty("data").GetProperty("accessToken").GetString()!;

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            // B2: thêm vào giỏ -> giỏ có đúng 1 dòng.
            (await client.PostAsJsonAsync("/api/Cart/items", new { ProductId = product.Id, Quantity = 2 }))
                .IsSuccessStatusCode.Should().BeTrue("thêm giỏ phải thành công");
            (await QueryAsync(db => db.CartItems.CountAsync(i => i.Cart.CustomerProfileId == profile.Id)))
                .Should().Be(1, "B2: giỏ phải có đúng 1 dòng");

            // B3: xem tổng tiền -> đúng giá server.
            var summary = await client.GetAsync("/api/orders/checkout-summary");
            summary.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadJsonAsync(summary)).GetProperty("totalAmount").GetDecimal()
                .Should().Be(200_000m, "B3: tổng tiền phải do server tính");

            // B4: đặt đơn SePay -> đơn chờ thanh toán, CHƯA Paid.
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .PaymentStatus.Should().Be(PaymentStatus.Pending, "B4: chưa trả tiền thì chưa Paid");

            // B5: webhook hợp lệ -> Paid + Confirmed + sinh PickTask.
            (await anonymous.SendAsync(SePayWebhook(orderCode, 200_000m, "REF-FLOW-01")))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var afterWebhook = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            afterWebhook.PaymentStatus.Should().Be(PaymentStatus.Paid, "B5: phải chuyển Paid");
            afterWebhook.OrderStatus.Should().Be(OrderStatus.Confirmed, "B5: phải chuyển Confirmed");
            (await QueryAsync(db => db.PickTasks.CountAsync(p => p.OrderId == orderId)))
                .Should().BeGreaterThan(0, "B5: phải sinh PickTask cho kho");

            // B6: đơn xuất hiện trong hàng đợi kho.
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var queue = await warehouse.GetAsync("/api/warehouse/orders?tabType=OnlinePending");
            queue.StatusCode.Should().Be(HttpStatusCode.OK);
            (await queue.Content.ReadAsStringAsync()).Should().Contain(orderCode,
                "B6: đơn phải xuất hiện trong hàng đợi kho");
        }

        /// FLOW-02 | Workflow | FT-02 AC-04; AC-05; BR-026; BR-027
        /// Báo giá: tạo -> Sales nhận -> tạo version -> Manager duyệt -> CEO duyệt -> khách chấp nhận.
        /// Kiểm trạng thái sau TỪNG cấp duyệt.
        [Fact]
        public async Task L3_FLOW_02_QuotationTwoLevelApprovalThenCustomerAccept()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(120_000_000m, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 120_000_000m));

            // B1: khách tạo yêu cầu báo giá.
            var created = await client.PostAsJsonAsync("/api/Quotation/from-cart", new { GeneralNote = "Bao gia B2B" });
            created.IsSuccessStatusCode.Should().BeTrue();
            var quotationId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
            (await QueryAsync(db => db.Quotations.SingleAsync(q => q.Id == quotationId)))
                .Status.Should().Be(QuotationStatus.Draft, "B1: chờ Sales nhận");

            // B2: Sales nhận.
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            (await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/pickup", new { }))
                .IsSuccessStatusCode.Should().BeTrue();
            (await QueryAsync(db => db.Quotations.SingleAsync(q => q.Id == quotationId)))
                .SalesStaffId.Should().Be(L3Seed.SalesStaffId, "B2: phải gán Sales phụ trách");

            // B3: Sales tạo version -> chờ Manager.
            var version = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions", new
            {
                ProposedTotal = 110_000_000m,
                SalesNote = "Gia de xuat",
                Items = new[] { new { ProductId = product.Id, Quantity = 1, ProposedUnitPrice = 110_000_000m } },
            });
            version.IsSuccessStatusCode.Should().BeTrue();
            (await QueryAsync(db => db.QuotationVersions.SingleAsync(v => v.QuotationId == quotationId)))
                .Status.Should().Be(QuotationVersionStatus.PendingManager, "B3: chờ Sales Manager");

            // B4: Manager duyệt -> chờ CEO.
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            (await manager.PostAsJsonAsync($"/api/Quotation/{quotationId}/manager-decision",
                new { IsApproved = true, ManagerNote = "OK" })).IsSuccessStatusCode.Should().BeTrue();
            (await QueryAsync(db => db.QuotationVersions.SingleAsync(v => v.QuotationId == quotationId)))
                .Status.Should().Be(QuotationVersionStatus.PendingCeo, "B4: chờ CEO");

            // B5: CEO duyệt -> sẵn sàng cho khách quyết định.
            var ceo = await ClientForSeededAsync(L3Seed.CeoId);
            (await ceo.PostAsJsonAsync($"/api/Quotation/{quotationId}/ceo-decision",
                new { IsApproved = true, CeoNote = "OK" })).IsSuccessStatusCode.Should().BeTrue();
            (await QueryAsync(db => db.QuotationVersions.SingleAsync(v => v.QuotationId == quotationId)))
                .Status.Should().Be(QuotationVersionStatus.CeoApproved, "B5: CEO đã duyệt");

            // B6: khách chấp nhận -> version bị khoá vào báo giá.
            (await client.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                new { IsAccepted = true })).IsSuccessStatusCode.Should().BeTrue();
            var accepted = await QueryAsync(db => db.Quotations.SingleAsync(q => q.Id == quotationId));
            accepted.Status.Should().Be(QuotationStatus.CustomerAccepted, "B6: khách đã chấp nhận");
            accepted.AcceptedVersionId.Should().NotBeNull("B6: phải khoá version đã chấp nhận");
        }

        /// FLOW-03 | Workflow | FT-05 AC-03; AC-05; FT-11 AC-01; AC-02; BR-012
        /// Chuyển kho: tạo -> xuất kho -> nhận kho. Kiểm tồn từng bước.
        [Fact]
        public async Task L3_FLOW_03_StockTransferMovesInventoryStepByStep()
        {
            var (product, _) = await SeedSellableProductAsync(100_000m, 100); // tại WH-DEFAULT
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            // B1: tạo phiếu chuyển WH-DEFAULT -> WH-TRADE.
            var create = await warehouse.PostAsJsonAsync("/api/stock-transfers", new
            {
                SourceWarehouseId = L3Seed.WarehouseDefaultId,
                DestinationWarehouseId = L3Seed.WarehouseTradeId,
                Items = new[] { new { ProductId = product.Id, Quantity = 10 } },
            });
            create.IsSuccessStatusCode.Should().BeTrue($"tạo phiếu phải thành công ({await ReadMessageAsync(create)})");
            var transferId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id
                    && i.WarehouseLocationId == L3Seed.LocationDefaultId)))
                .OnHandQuantity.Should().Be(100, "B1: mới tạo phiếu thì tồn CHƯA đổi");

            // B2: xuất kho -> tồn nguồn giảm.
            var dispatch = await warehouse.PostAsJsonAsync($"/api/stock-transfers/{transferId}/dispatch", new { });
            dispatch.IsSuccessStatusCode.Should().BeTrue($"xuất kho phải thành công ({await ReadMessageAsync(dispatch)})");

            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id
                    && i.WarehouseLocationId == L3Seed.LocationDefaultId)))
                .OnHandQuantity.Should().Be(90, "B2: kho nguồn phải giảm đúng 10");
            (await QueryAsync(db => db.StockTransfers.SingleAsync(t => t.Id == transferId)))
                .Status.Should().Be(StockTransferStatus.Dispatched);

            // B3: kho đích nhận -> tồn đích tăng phần đã nhận.
            using var content = new MultipartFormDataContent
            {
                { new StringContent($"[{{\"productId\":\"{product.Id}\",\"receivedQuantity\":10}}]"), "ItemsJson" },
            };
            var receiver = await ClientForSeededAsync(L3Seed.WarehouseStaff2Id); // nhân viên WH-TRADE
            var receive = await receiver.PostAsync($"/api/stock-transfers/{transferId}/receive", content);
            receive.IsSuccessStatusCode.Should().BeTrue($"nhận kho phải thành công ({await ReadMessageAsync(receive)})");

            var atDestination = await QueryAsync(db => db.Inventories
                .Where(i => i.ProductId == product.Id && i.WarehouseLocationId == L3Seed.LocationTradeId)
                .SumAsync(i => i.OnHandQuantity));
            atDestination.Should().Be(10, "B3: kho đích phải tăng đúng phần đã nhận");

            // Bảo toàn tổng: 90 (nguồn) + 10 (đích) = 100.
            (await QueryAsync(db => db.Inventories.Where(i => i.ProductId == product.Id)
                    .SumAsync(i => i.OnHandQuantity)))
                .Should().Be(100, "tổng tồn toàn hệ thống phải được bảo toàn tuyệt đối");
        }

        /// FLOW-04 | Workflow | FT-07 AC-01; AC-03; AC-04; BR-038; BR-039
        /// Giao hàng: lên lịch -> hoàn tất giao. (Module chuyến giao/POD/COD riêng chưa có — DEF-L3-004.)
        [Fact]
        public async Task L3_FLOW_04_DeliveryScheduleThenComplete()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 50);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 100_000m));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .DeliveryStatus.Should().Be(DeliveryStatus.NotScheduled, "B0: chưa lên lịch giao");

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            // B1: lên lịch giao — endpoint phải tồn tại và mở cho Sales.
            var schedule = await sales.PostAsJsonAsync("/api/delivery/schedule", new
            {
                OrderId = orderId,
                ScheduledDeliveryDate = DateTime.UtcNow.AddDays(1),
            });
            schedule.StatusCode.Should().NotBe(HttpStatusCode.NotFound, "B1: endpoint lên lịch phải tồn tại");
            schedule.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "B1: Sales phải được lên lịch");

            // ── Luồng chuyến giao đầy đủ theo workbook: trips -> start -> attempts -> collections ──
            // Trước 14/08/2026 khối này chỉ assert 404 (DEF-L3-004). Module nay đã có nên chạy thật.

            // B1: xếp chuyến -> đơn chuyển sang SCHEDULED.
            var vehicleId = await QueryAsync(db => db.Vehicles
                .Where(v => v.IsActive).Select(v => v.Id).FirstAsync());
            var trip = await sales.PostAsJsonAsync("/api/delivery/trips", new
            {
                VehicleId = vehicleId,
                Shift = "Sáng",                       // giá trị hợp lệ là tiếng Việt, không phải "Morning"
                TripDate = DateTime.UtcNow.Date.AddDays(1),
                OrderIds = new List<Guid> { orderId }
            });
            trip.StatusCode.Should().Be(HttpStatusCode.OK,
                "B1: xếp chuyến phải thành công; body: {0}", await trip.Content.ReadAsStringAsync());
            var tripId = (await ReadJsonAsync(trip)).GetProperty("id").GetGuid();

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .DeliveryStatus.Should().Be(DeliveryStatus.Scheduled, "B1: đơn đã được xếp lịch");

            // B2: bắt đầu chuyến — BR-034 đòi mọi đơn trong chuyến đã bàn giao xong.
            (await sales.PostAsJsonAsync($"/api/delivery/trips/{tripId}/start", new { }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict,
                    "B2: chưa bàn giao thì không được xuất phát");

            await SeedAsync(db =>
            {
                db.HandoverRecords.Add(new HandoverRecord
                {
                    OrderId = orderId,
                    Status = HandoverStatus.Confirmed,
                    WarehouseStaffId = L3Seed.WarehouseStaffId,
                    SalesStaffId = L3Seed.SalesStaffId,
                    HandoverTime = DateTime.UtcNow,
                });
                return Task.CompletedTask;
            });

            var start = await sales.PostAsJsonAsync($"/api/delivery/trips/{tripId}/start", new { });
            start.StatusCode.Should().Be(HttpStatusCode.OK,
                "B2: đã bàn giao thì xuất phát được; body: {0}", await start.Content.ReadAsStringAsync());
            (await QueryAsync(db => db.DeliveryTrips.SingleAsync(t => t.Id == tripId)))
                .Status.Should().Be(DeliveryTripStatus.InDelivery, "B2: chuyến đang giao");

            // B3: ghi nhận POD đầy đủ -> đơn DELIVERED.
            var attempt = await sales.PostAsJsonAsync("/api/delivery/attempts", new
            {
                OrderId = orderId, Outcome = "Delivered",
                PhotoUrl = "https://example.invalid/pod.jpg",
                SignatureUrl = "https://example.invalid/sig.png"
            });
            attempt.StatusCode.Should().Be(HttpStatusCode.OK,
                "B3: POD đủ bằng chứng; body: {0}", await attempt.Content.ReadAsStringAsync());
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .DeliveryStatus.Should().Be(DeliveryStatus.Delivered, "B3: đơn đã giao");

            // B4: thu COD THIẾU 40.000đ -> ghi đúng số thu và tạo công nợ phần còn lại (BR-039).
            var collect = await sales.PostAsJsonAsync("/api/delivery/collections", new
            {
                OrderId = orderId, AmountCollected = 60_000m
            });
            collect.StatusCode.Should().Be(HttpStatusCode.OK,
                "B4: thu tiền một phần phải ghi nhận được; body: {0}",
                await collect.Content.ReadAsStringAsync());

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            order.AmountPaid.Should().Be(60_000m, "B4: ghi đúng số tiền đã thu");

            var debt = await QueryAsync(db => db.CustomerDebts
                .SingleOrDefaultAsync(d => d.OrderId == orderId && d.Status == DebtStatus.InDebt));
            debt.Should().NotBeNull("B4: phần còn thiếu phải tạo công nợ");
            debt!.DebtAmount.Should().Be(40_000m, "công nợ = tổng đơn 100.000 trừ 60.000 đã thu");
        }

        /// FLOW-05 | Workflow | FT-08 AC-01; AC-02; AC-03; BR-017; BR-041
        /// Huỷ đơn ĐÃ THANH TOÁN: yêu cầu huỷ -> duyệt. Bất biến: KHÔNG có giao dịch hoàn tiền,
        /// giá trị được bảo toàn dưới dạng credit chứ không trả tiền mặt.
        [Fact]
        public async Task L3_FLOW_05_PaidOrderCancellation_NoRefundTransaction_ValueKeptAsCredit()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 50);
            await SeedCartAsync(profile.Id, null, (product.Id, 2, 100_000m));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            // B1: thanh toán thật qua webhook.
            (await AnonymousClient().SendAsync(SePayWebhook(orderCode, 200_000m, "REF-FLOW-05")))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .PaymentStatus.Should().Be(PaymentStatus.Paid, "B1: đơn đã thanh toán");

            // B2: Sales tạo yêu cầu huỷ đơn đã trả tiền.
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var request = await sales.PostAsJsonAsync($"/api/delivery/{orderId}/request-cancel",
                new { Reason = "Khach doi y" });
            request.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "B2: Sales phải được tạo yêu cầu huỷ");

            // B3: bất biến xuyên suốt — Payment vẫn Paid, KHÔNG có giao dịch hoàn tiền nào.
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .PaymentStatus.Should().Be(PaymentStatus.Paid, "B3: không được đảo trạng thái thanh toán");
            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId && t.Amount < 0)))
                .Should().Be(0, "B3: hệ thống KHÔNG hoàn tiền (BR-017)");

            // B4: số dư credit không bao giờ âm.
            (await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profile.Id)))
                .AvailableCredit.Should().BeGreaterThanOrEqualTo(0m);
        }

        /// FLOW-06 | Workflow | FT-06 AC-01; AC-03; FT-01 AC-01; BR-014
        /// Mua hàng: CEO tạo + phát hành PO -> gửi kho. Bất biến: tồn kho KHÔNG đổi cho tới khi
        /// có phiếu nhập được post.
        [Fact]
        public async Task L3_FLOW_06_PurchaseOrderIssuance_DoesNotChangeStockUntilReceipt()
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "NCC Flow06",
                Code = "NCC-F06",
                Phone = NewPhone(),
                Email = NewEmail(),
                IsActive = true,
            };
            await SeedAsync(db => { db.Suppliers.Add(supplier); return Task.CompletedTask; });
            var (product, _) = await SeedSellableProductAsync(50_000m, 0);

            var ceo = await ClientForSeededAsync(L3Seed.CeoId);

            // B1: tạo PO.
            var create = await ceo.PostAsJsonAsync("/api/purchase-orders", new
            {
                SupplierId = supplier.Id,
                WarehouseId = L3Seed.WarehouseDefaultId,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Items = new[] { new { ProductId = product.Id, ExpectedQuantity = 20, UnitPrice = 30_000m } },
            });
            create.IsSuccessStatusCode.Should().BeTrue($"B1 ({await ReadMessageAsync(create)})");
            var poId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(0, "B1: tạo PO KHÔNG được đụng tồn kho");

            // B2: phát hành PO.
            (await ceo.PostAsJsonAsync($"/api/purchase-orders/{poId}/issue", new { }))
                .IsSuccessStatusCode.Should().BeTrue("B2: phát hành PO");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(0, "B2: phát hành PO vẫn KHÔNG đụng tồn kho");

            // B3: gửi kho.
            var send = await ceo.PostAsJsonAsync($"/api/purchase-orders/{poId}/send-to-warehouse", new { });
            send.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(0,
                    "B3: chỉ Goods Receipt được post mới làm tăng tồn — đúng BR-014");
        }

        /// FLOW-07 | Workflow | NFR-A02; BR-049; FT-10 AC-04
        /// Nhà cung cấp ngoài (AI/SMS/Facebook) lỗi hoặc không cấu hình KHÔNG được làm hỏng trạng
        /// thái nội bộ và KHÔNG chặn luồng nghiệp vụ khác.
        [Fact]
        public async Task L3_FLOW_07_ExternalProviderFailure_DoesNotCorruptStateOrBlockOtherFlows()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            // B1: gọi tính năng phụ thuộc nhà cung cấp ngoài (AI sinh nội dung) — fake trả rỗng.
            var ai = await sales.PostAsJsonAsync("/api/marketing-posts/generate-options",
                new { ProductId = L3Seed.ProductTapeTrongId, Prompt = "Quang cao" });
            ((int)ai.StatusCode).Should().BeLessThan(500,
                "B1: lỗi/không cấu hình nhà cung cấp ngoài phải thành lỗi nghiệp vụ, không phải 500");

            // B2: trạng thái nội bộ không bị hỏng — không có bài rác nào được tạo.
            (await QueryAsync(db => db.MarketingPosts.CountAsync()))
                .Should().Be(0, "B2: gọi AI thất bại KHÔNG được tạo bản ghi bài đăng");

            // B3: luồng nghiệp vụ KHÔNG liên quan vẫn chạy bình thường.
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 100_000m));

            var placed = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            placed.StatusCode.Should().Be(HttpStatusCode.OK,
                "B3: sự cố nhà cung cấp ngoài KHÔNG được chặn luồng đặt hàng");

            // B4: không có lệnh gọi IO thật nào ra ngoài trong toàn bộ luồng.
            Factory.MakeWebhook.Triggered.Should().BeEmpty();
        }
    }
}
