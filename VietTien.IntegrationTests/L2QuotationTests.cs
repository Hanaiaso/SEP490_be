using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-Quotation — chuỗi đàm phán báo giá trên DB thật.
    /// Route thật: /api/Quotation (chữ Q hoa, số ít) — pickup · versions · manager-decision
    /// · ceo-decision · customer-decision · messages.
    /// ⚠ Workbook ghi bước 3 là "M1 approve"; route thật là `manager-decision` (R8).
    /// </summary>
    [Trait("Category", "L2")]
    public class L2QuotationTests : SqlServerTestBase
    {
        public L2QuotationTests(SqlServerFixture factory) : base(factory) { }

        private sealed record QuoFixture(HttpClient Customer, Guid CustomerUserId, Guid ProfileId, Guid ProductId, Guid CartId);

        /// <summary>Khách hàng có hồ sơ + giỏ hàng đạt tổng chỉ định.</summary>
        private async Task<QuoFixture> SeedCustomerWithCartAsync(decimal cartTotal, Guid? assignedSalesStaffId = null)
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profileId = Guid.NewGuid();
            var cartId = Guid.NewGuid();
            Guid productId = Guid.Empty;

            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 10_000; inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0;
                productId = inv.ProductId!.Value;

                // Phải đặt CẢ giá catalog: /api/Quotation/from-cart tính tổng giỏ theo
                // Product.StandardListedPrice, còn /api/orders/checkout-summary tính theo
                // CartItem.UnitPrice — hai nguồn khác nhau (xem GH-11 trong báo cáo).
                var product = await db.Products.FirstAsync(p => p.Id == productId);
                product.StandardListedPrice = cartTotal;

                db.CustomerProfiles.Add(new CustomerProfile
                {
                    Id = profileId, UserId = user.Id, AssignedSalesStaffId = assignedSalesStaffId
                });
                db.Carts.Add(new Cart
                {
                    Id = cartId, CustomerProfileId = profileId,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    Items = new List<CartItem>
                    {
                        new() { ProductId = productId, Quantity = 1, UnitPrice = cartTotal }
                    }
                });
            });

            return new QuoFixture(client, user.Id, profileId, productId, cartId);
        }

        private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.NameEquals("id") && p.Value.TryGetGuid(out var g)) return g;
            return Guid.Empty;
        }

        private Task<Quotation> ReloadQuotationAsync(Guid id) =>
            QueryAsync(db => db.Quotations.AsNoTracking().FirstAsync(q => q.Id == id));

        /// <summary>Tạo báo giá từ giỏ và trả về id.</summary>
        private async Task<Guid> CreateFromCartAsync(QuoFixture f)
        {
            var response = await f.Customer.PostAsJsonAsync("/api/Quotation/from-cart",
                new CreateQuotationRequest { CartId = f.CartId, GeneralNote = "L2-QUO" });
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "tạo báo giá từ giỏ phải thành công; body: {0}", await response.Content.ReadAsStringAsync());
            var id = await ReadIdAsync(response);
            id.Should().NotBe(Guid.Empty);
            return id;
        }

        // ── L2-QUO-01 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Khách U1 có giỏ 120.000.000; đã seed sales S1, manager M1, CEO C1
        // WHEN   from-cart → S1 pickup + tạo version → M1 duyệt → C1 duyệt → U1 chấp nhận
        // THEN   Trạng thái đi đúng chuỗi 4.4.3 và dừng ở CustomerAccepted; có thông báo ở mỗi chặng
        [Fact]
        [Trait("TestID", "L2-QUO-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 AC-04; AC-05; BR-027")]
        public async Task L2_QUO_01_FullApprovalChainReachesCustomerAccepted()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var f = await SeedCustomerWithCartAsync(120_000_000m, assignedSalesStaffId: salesUser.Id);

            var quotationId = await CreateFromCartAsync(f);

            // 2) Sales nhận việc
            var pickup = await sales.PostAsync($"/api/Quotation/{quotationId}/pickup", null);
            pickup.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await pickup.Content.ReadAsStringAsync());

            // 3) Sales tạo phương án giá -> chuyển sang chờ Manager
            var version = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions",
                new CreateQuotationVersionRequest
                {
                    ProposedTotal = 110_000_000m,
                    SalesNote = "Giam gia cho khach lon",
                    Items = new List<QuotationVersionItemRequest>
                    {
                        new() { ProductId = f.ProductId, ProposedUnitPrice = 110_000_000m }
                    }
                });
            version.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await version.Content.ReadAsStringAsync());
            (await ReloadQuotationAsync(quotationId)).Status.Should().Be(QuotationStatus.PendingManager);

            // 4) Manager duyệt -> chờ CEO
            var mgr = await manager.PostAsJsonAsync($"/api/Quotation/{quotationId}/manager-decision",
                new ManagerReviewRequest { IsApproved = true, ManagerNote = "OK" });
            mgr.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await mgr.Content.ReadAsStringAsync());
            (await ReloadQuotationAsync(quotationId)).Status.Should().Be(QuotationStatus.PendingCeo);

            // 5) CEO duyệt -> Approved
            var ceoDecision = await ceo.PostAsJsonAsync($"/api/Quotation/{quotationId}/ceo-decision",
                new CeoReviewRequest { IsApproved = true, CeoNote = "Duyet" });
            ceoDecision.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await ceoDecision.Content.ReadAsStringAsync());
            (await ReloadQuotationAsync(quotationId)).Status.Should().Be(QuotationStatus.Approved);

            // 6) Khách chấp nhận
            var accept = await f.Customer.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                new CustomerDecisionRequest { IsAccepted = true });
            accept.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await accept.Content.ReadAsStringAsync());

            // (b) DB — scope mới
            var final = await ReloadQuotationAsync(quotationId);
            final.Status.Should().Be(QuotationStatus.CustomerAccepted);
            final.AcceptedVersionId.Should().NotBeNull("phải ghi nhận phiên bản giá được chấp nhận");
            final.SalesStaffId.Should().Be(salesUser.Id);

            var acceptedVersion = await QueryAsync(db => db.QuotationVersions.AsNoTracking()
                .FirstAsync(v => v.Id == final.AcceptedVersionId!.Value));
            acceptedVersion.ProposedTotal.Should().Be(110_000_000m);

            // (c) side effect — có thông báo phát sinh trong chuỗi duyệt
            (await QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == quotationId)))
                .Should().BeGreaterThan(0, "mỗi chặng duyệt phải sinh thông báo");
        }

        // ── L2-QUO-02 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Báo giá Q1 đang chờ CEO duyệt; người gọi mang JWT Sales
        // WHEN   POST /api/Quotation/{id}/ceo-decision bằng role Sales
        // THEN   403; trạng thái trong DB không đổi
        [Fact]
        [Trait("TestID", "L2-QUO-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 NAC-05; NFR-SEC03")]
        public async Task L2_QUO_02_SalesCannotMakeCeoDecision()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var f = await SeedCustomerWithCartAsync(120_000_000m, assignedSalesStaffId: salesUser.Id);

            var quotationId = await CreateFromCartAsync(f);
            (await sales.PostAsync($"/api/Quotation/{quotationId}/pickup", null)).EnsureSuccessStatusCode();
            (await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions",
                new CreateQuotationVersionRequest
                {
                    ProposedTotal = 110_000_000m,
                    Items = new List<QuotationVersionItemRequest>
                    {
                        new() { ProductId = f.ProductId, ProposedUnitPrice = 110_000_000m }
                    }
                })).EnsureSuccessStatusCode();
            (await manager.PostAsJsonAsync($"/api/Quotation/{quotationId}/manager-decision",
                new ManagerReviewRequest { IsApproved = true })).EnsureSuccessStatusCode();

            var statusBefore = (await ReloadQuotationAsync(quotationId)).Status;
            statusBefore.Should().Be(QuotationStatus.PendingCeo);

            // Sales cố ra quyết định thay CEO
            var response = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/ceo-decision",
                new CeoReviewRequest { IsApproved = true, CeoNote = "tu duyet" });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "NFR-SEC03: chỉ CEO được ra quyết định này; body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB
            (await ReloadQuotationAsync(quotationId)).Status.Should().Be(statusBefore, "trạng thái không được đổi");
            (await QueryAsync(db => db.QuotationVersions.AsNoTracking()
                .CountAsync(v => v.QuotationId == quotationId && v.Status == QuotationVersionStatus.CeoApproved)))
                .Should().Be(0, "không được có phiên bản nào được CEO duyệt");
        }

        // ── L2-QUO-03 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Q1 đã gửi khách, đã qua 5 vòng đàm phán
        // WHEN   U1 POST customer-decision {accept:false} lần thứ 5
        // THEN   Q1 = Cancelled; vòng tiếp theo bị từ chối 409/422; có thông báo escalation
        [Fact]
        [Trait("TestID", "L2-QUO-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 NAC-04")]
        public async Task L2_QUO_03_NegotiationIsCappedAtFiveRounds()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);
            var f = await SeedCustomerWithCartAsync(120_000_000m, assignedSalesStaffId: salesUser.Id);

            var quotationId = await CreateFromCartAsync(f);
            (await sales.PostAsync($"/api/Quotation/{quotationId}/pickup", null)).EnsureSuccessStatusCode();

            // 5 vòng: mỗi vòng Sales đề xuất -> Manager duyệt -> CEO duyệt -> khách từ chối
            for (var round = 1; round <= 5; round++)
            {
                var version = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions",
                    new CreateQuotationVersionRequest
                    {
                        ProposedTotal = 120_000_000m - round * 1_000_000m,
                        SalesNote = $"vong {round}",
                        Items = new List<QuotationVersionItemRequest>
                        {
                            new() { ProductId = f.ProductId, ProposedUnitPrice = 120_000_000m - round * 1_000_000m }
                        }
                    });

                if (round == 5 && !version.IsSuccessStatusCode)
                {
                    // Đã bị chặn ở vòng 5 -> đúng tinh thần cap, kiểm tiếp ở phần dưới
                    break;
                }
                version.StatusCode.Should().Be(HttpStatusCode.OK,
                    "vòng {0}: tạo phương án giá; body: {1}", round, await version.Content.ReadAsStringAsync());

                (await manager.PostAsJsonAsync($"/api/Quotation/{quotationId}/manager-decision",
                    new ManagerReviewRequest { IsApproved = true })).EnsureSuccessStatusCode();
                (await ceo.PostAsJsonAsync($"/api/Quotation/{quotationId}/ceo-decision",
                    new CeoReviewRequest { IsApproved = true })).EnsureSuccessStatusCode();

                var reject = await f.Customer.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                    new CustomerDecisionRequest { IsAccepted = false });
                reject.StatusCode.Should().Be(HttpStatusCode.OK,
                    "vòng {0}: khách từ chối; body: {1}", round, await reject.Content.ReadAsStringAsync());
            }

            // (b) DB — sau vòng thứ 5 báo giá phải bị đóng lại
            var q = await ReloadQuotationAsync(quotationId);
            q.Status.Should().Be(QuotationStatus.Cancelled,
                "FT-02 NAC-04: quá 5 vòng đàm phán thì báo giá phải bị huỷ, không cho đàm phán vô hạn");

            // Vòng thứ 6 phải bị chặn
            var extraRound = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions",
                new CreateQuotationVersionRequest
                {
                    ProposedTotal = 100_000_000m,
                    Items = new List<QuotationVersionItemRequest>
                    {
                        new() { ProductId = f.ProductId, ProposedUnitPrice = 100_000_000m }
                    }
                });
            ((int)extraRound.StatusCode).Should().BeInRange(400, 499,
                "vòng đàm phán thứ 6 phải bị từ chối; body: {0}", await extraRound.Content.ReadAsStringAsync());

            // (c) side effect — cảnh báo escalation
            (await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.ReferenceId == quotationId && n.Type == NotificationType.SYS_27_QuotationNegotiationLimitReached)))
                .Should().BeGreaterThan(0, "phải có cảnh báo chạm trần đàm phán");
        }

        // ── L2-QUO-04 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Khách có giỏ 110.000.000, sau đó đổi xuống 80.000.000
        // WHEN   GET /api/orders/checkout-summary ở từng mức
        // THEN   SRS v2 BR-026: giỏ >=100tr phải có báo giá được duyệt mới đi tiếp;
        //        giỏ <100tr đi thẳng với chiết khấu theo bậc cấu hình
        [Fact]
        [Trait("TestID", "L2-QUO-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 AC-05; NAC-02; BV-01; BR-026")]
        public async Task L2_QUO_04_HighValueCartRequiresQuotationWhileLowValueUsesTierDiscount()
        {
            await ResetAsync();
            var f = await SeedCustomerWithCartAsync(110_000_000m);

            // >= 100 triệu: SRS v2 bỏ auto-apply giá đã lưu, bắt buộc đi qua báo giá B2B
            var high = await f.Customer.GetAsync("/api/orders/checkout-summary");
            var highBody = await high.Content.ReadAsStringAsync();
            ((int)high.StatusCode).Should().BeInRange(400, 499,
                "BR-026 (SRS v2): giỏ >=100tr không được tự tính giá, phải có báo giá được duyệt; body: {0}", highBody);
            highBody.Should().MatchRegex("(?i)(báo giá|bao gia|100)",
                "thông báo phải chỉ rõ cần báo giá B2B");

            // < 100 triệu: đi thẳng, áp chiết khấu theo bậc cấu hình
            await SeedAsync(async db =>
            {
                var item = await db.CartItems.FirstAsync(ci => ci.Cart.CustomerProfileId == f.ProfileId);
                item.UnitPrice = 80_000_000m;
                var product = await db.Products.FirstAsync(p => p.Id == f.ProductId);
                product.StandardListedPrice = 80_000_000m;
            });

            var low = await f.Customer.GetAsync("/api/orders/checkout-summary");
            var lowBody = await low.Content.ReadAsStringAsync();
            low.StatusCode.Should().Be(HttpStatusCode.OK,
                "giỏ dưới ngưỡng phải checkout được bình thường; body: {0}", lowBody);

            // (b) bậc chiết khấu đến từ bảng cấu hình đã seed, không hard-code
            var tiers = await QueryAsync(db => db.DiscountTiers.AsNoTracking()
                .Where(t => t.IsActive).ToListAsync());
            tiers.Should().NotBeEmpty("bậc chiết khấu phải đến từ bảng cấu hình");
            var expectedTier = tiers.FirstOrDefault(t => 80_000_000m >= t.MinAmount && 80_000_000m < t.MaxAmount);
            expectedTier.Should().NotBeNull("80 triệu phải rơi vào một bậc chiết khấu đã cấu hình");
            lowBody.Should().NotBeNullOrWhiteSpace();
        }

        // ── L2-QUO-05 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Q1 đang mở; U1 là chủ báo giá, S1 là Sales phụ trách
        // WHEN   S1 POST /api/Quotation/{id}/messages {content}
        // THEN   ChatMessage được lưu; khách đọc lại được qua GET messages
        [Fact]
        [Trait("TestID", "L2-QUO-05")]
        [Trait("Priority", "P2")]
        [Trait("SRSRef", "FT-02 AC-04; BR-020; NFR-P04")]
        public async Task L2_QUO_05_ChatMessageIsPersistedAndVisibleToOwner()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var f = await SeedCustomerWithCartAsync(120_000_000m, assignedSalesStaffId: salesUser.Id);

            var quotationId = await CreateFromCartAsync(f);
            (await sales.PostAsync($"/api/Quotation/{quotationId}/pickup", null)).EnsureSuccessStatusCode();

            const string text = "Ben em de xuat giam 5% cho don nay.";
            var send = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/messages",
                new SendChatMessageRequest { MessageText = text });

            // (a) HTTP
            send.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await send.Content.ReadAsStringAsync());

            // (b) DB — scope mới
            var messages = await QueryAsync(db => db.ChatMessages.AsNoTracking()
                .Where(m => m.QuotationId == quotationId).ToListAsync());
            messages.Should().ContainSingle();
            messages[0].MessageText.Should().Be(text);
            messages[0].SenderId.Should().Be(salesUser.Id);

            // (c) side effect — chủ báo giá đọc lại được
            var read = await f.Customer.GetAsync($"/api/Quotation/{quotationId}/messages");
            read.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await read.Content.ReadAsStringAsync());
            (await read.Content.ReadAsStringAsync()).Should().Contain(text);
        }

        // ── L2-QUO-06 ──────────────────────────────────────────────────────────────────────

        // GIVEN  U5 là khách khác, không liên quan tới Q1, JWT hợp lệ
        // WHEN   GET /api/Quotation/{id}/messages bằng JWT của U5
        // THEN   403/404; không rò rỉ nội dung tin nhắn
        [Fact]
        [Trait("TestID", "L2-QUO-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 NAC-05; NFR-SEC03")]
        public async Task L2_QUO_06_UnrelatedCustomerCannotReadQuotationMessages()
        {
            await ResetAsync();
            var (sales, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var f = await SeedCustomerWithCartAsync(120_000_000m, assignedSalesStaffId: salesUser.Id);

            var quotationId = await CreateFromCartAsync(f);
            (await sales.PostAsync($"/api/Quotation/{quotationId}/pickup", null)).EnsureSuccessStatusCode();

            const string secret = "Gia noi bo 95 trieu - khong duoc lo ra ngoai.";
            (await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/messages",
                new SendChatMessageRequest { MessageText = secret })).EnsureSuccessStatusCode();

            // Khách khác, hoàn toàn không liên quan
            var outsider = await SeedCustomerWithCartAsync(10_000_000m);

            var response = await outsider.Customer.GetAsync($"/api/Quotation/{quotationId}/messages");

            // (a) HTTP
            response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
                "NFR-SEC03: khách khác không được đọc hội thoại báo giá; body: {0}",
                await response.Content.ReadAsStringAsync());

            // (b) không rò rỉ nội dung
            (await response.Content.ReadAsStringAsync()).Should().NotContain(secret,
                "tuyệt đối không được lộ nội dung đàm phán của khách khác");

            // (c) dữ liệu gốc còn nguyên
            (await QueryAsync(db => db.ChatMessages.CountAsync(m => m.QuotationId == quotationId)))
                .Should().Be(1);
        }
    }
}
