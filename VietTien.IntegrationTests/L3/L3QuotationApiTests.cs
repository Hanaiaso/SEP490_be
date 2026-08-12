using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-QuotationAPI</c> — QUO-01..08.
    ///
    /// Ánh xạ endpoint: POST /api/Quotation -> /api/Quotation/from-cart;
    /// {id}/ceo-review -> {id}/ceo-decision; {id}/manager-review -> {id}/manager-decision.
    /// </summary>
    public class L3QuotationApiTests : L3TestBase
    {
        public L3QuotationApiTests(L3SqlFixture factory) : base(factory) { }

        /// <summary>Khách + hồ sơ + địa chỉ + giỏ đạt ngưỡng báo giá (mặc định 120 triệu).</summary>
        private async Task<(HttpClient client, User user, CustomerProfile profile, Product product)>
            ArrangeB2BCustomerAsync(decimal cartTotal = 120_000_000m)
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(cartTotal, 100);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, cartTotal));
            return (client, user, profile, product);
        }

        /// <summary>Tạo báo giá qua API và trả về Id.</summary>
        private static async Task<Guid> CreateQuotationAsync(HttpClient customer)
        {
            var res = await customer.PostAsJsonAsync("/api/Quotation/from-cart",
                new { GeneralNote = "Yeu cau bao gia L3" });
            res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            return (await ReadJsonAsync(res)).GetProperty("id").GetGuid();
        }

        /// <summary>Đưa báo giá tới trạng thái có 1 version đã được CEO duyệt.</summary>
        private async Task<(Guid quotationId, Guid versionId)> ArrangeCeoApprovedQuotationAsync(
            HttpClient customer, Guid productId, decimal proposedTotal = 110_000_000m)
        {
            var quotationId = await CreateQuotationAsync(customer);

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            (await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/pickup", new { }))
                .IsSuccessStatusCode.Should().BeTrue("Sales phải nhận được yêu cầu báo giá");

            var version = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions", new
            {
                ProposedTotal = proposedTotal,
                SalesNote = "De xuat gia",
                Items = new[] { new { ProductId = productId, Quantity = 1, ProposedUnitPrice = proposedTotal } }
            });
            version.IsSuccessStatusCode.Should().BeTrue("tạo version mới phải thành công");
            var versionId = (await ReadJsonAsync(version)).GetProperty("id").GetGuid();

            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            (await manager.PostAsJsonAsync($"/api/Quotation/{quotationId}/manager-decision",
                new { IsApproved = true, ManagerNote = "OK" })).IsSuccessStatusCode.Should().BeTrue();

            var ceo = await ClientForSeededAsync(L3Seed.CeoId);
            (await ceo.PostAsJsonAsync($"/api/Quotation/{quotationId}/ceo-decision",
                new { IsApproved = true, CeoNote = "OK" })).IsSuccessStatusCode.Should().BeTrue();

            return (quotationId, versionId);
        }

        /// QUO-01 | Input-Domain-Happy | FT-02 AC-03; BR-026
        /// Giỏ >= 100 triệu -> tạo được 1 QuotationRequest gắn với hồ sơ khách.
        [Fact]
        public async Task L3_QUO_01_CreateQuotationFromCart_AtOrAboveThreshold_Created()
        {
            var (client, _, profile, _) = await ArrangeB2BCustomerAsync();

            var quotationId = await CreateQuotationAsync(client);

            var quotation = await QueryAsync(db => db.Quotations.SingleAsync(q => q.Id == quotationId));
            quotation.CustomerProfileId.Should().Be(profile.Id);
            quotation.OriginalTotal.Should().Be(120_000_000m);
            quotation.Status.Should().Be(QuotationStatus.Draft, "báo giá mới ở trạng thái chờ Sales nhận");
        }

        /// QUO-02 | Input-Domain-Happy | FT-02 AC-04; BR-027
        /// Sales được gán tạo version -> version ở trạng thái chờ Sales Manager duyệt.
        [Fact]
        public async Task L3_QUO_02_CreateVersion_ByAssignedSales_AwaitsManagerApproval()
        {
            var (client, _, _, product) = await ArrangeB2BCustomerAsync();
            var quotationId = await CreateQuotationAsync(client);

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/pickup", new { });

            var res = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions", new
            {
                ProposedTotal = 110_000_000m,
                SalesNote = "Giam gia cho khach quen",
                Items = new[] { new { ProductId = product.Id, Quantity = 1, ProposedUnitPrice = 110_000_000m } }
            });

            res.IsSuccessStatusCode.Should().BeTrue();
            var version = await QueryAsync(db => db.QuotationVersions
                .SingleAsync(v => v.QuotationId == quotationId));
            version.Status.Should().Be(QuotationVersionStatus.PendingManager,
                "version mới phải chờ Sales Manager duyệt trước");
        }

        /// QUO-03 | Input-Domain-Error | FT-02 NAC-05; BR-027
        /// Sales Staff gọi API duyệt cấp CEO -> 403 (sai vai trò duyệt).
        [Fact]
        public async Task L3_QUO_03_CeoDecision_BySalesStaff_Forbidden()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await sales.PostAsJsonAsync($"/api/Quotation/{Guid.NewGuid()}/ceo-decision",
                new { IsApproved = true });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// QUO-04 | Input-Domain-Error | FT-02 NAC-05
        /// Khách chấp nhận version CHƯA qua đủ 2 cấp duyệt -> bị từ chối, trạng thái không đổi.
        [Fact]
        public async Task L3_QUO_04_CustomerDecision_BeforeTwoLevelApproval_Rejected()
        {
            var (client, _, _, product) = await ArrangeB2BCustomerAsync();
            var quotationId = await CreateQuotationAsync(client);

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/pickup", new { });
            await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/versions", new
            {
                ProposedTotal = 110_000_000m,
                Items = new[] { new { ProductId = product.Id, Quantity = 1, ProposedUnitPrice = 110_000_000m } }
            });
            // CỐ Ý bỏ qua manager-decision và ceo-decision.

            var res = await client.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                new { IsAccepted = true });

            res.IsSuccessStatusCode.Should().BeFalse("chưa qua 2 cấp duyệt thì không được chấp nhận");
            (await QueryAsync(db => db.Quotations.SingleAsync(q => q.Id == quotationId)))
                .Status.Should().NotBe(QuotationStatus.CustomerAccepted);
        }

        /// QUO-05 | BVA | FT-02 BV-03; NAC-04; BR-007  ->  <b>FAIL — defect DEF-L3-003 (P1)</b>
        ///
        /// Sau khi khách chấp nhận báo giá 110 triệu cho giỏ 1 đơn vị, khách sửa giỏ thành 2 đơn vị
        /// (240 triệu) rồi đặt đơn. Theo BR-007 giỏ đã khác version đã duyệt nên phải bị chặn
        /// (QUOTATION_VERSION_STALE) và bắt tạo version mới + duyệt lại.
        ///
        /// THỰC TẾ: đơn được tạo với FinalPayment = 110.000.000đ cho 240.000.000đ hàng.
        /// Nguyên nhân: <c>OrderService.CalculateDiscountAsync</c> (OrderService.cs:88-103) chỉ tìm
        /// "báo giá đã chấp nhận BẤT KỲ còn hiệu lực của khách này" rồi lấy
        /// <c>negotiatedDiscount = totalAmount - acceptedVersion.ProposedTotal</c>. Nó KHÔNG hề đối
        /// chiếu giỏ hiện tại với giỏ/danh mục hàng của version đã duyệt — trường
        /// <c>Quotation.CartId</c> có trong model nhưng không được dùng ở đây.
        /// Hệ quả: khách chỉ cần được duyệt MỘT báo giá là mua được giỏ hàng lớn tuỳ ý ở mức giá đó.
        ///
        /// Test này CỐ Ý đỏ: nó assert theo đúng SRS, và sẽ xanh khi code được sửa.
        [Fact]
        public async Task L3_QUO_05_PlaceOrder_CartChangedAfterQuotationAccepted_MustBeRejected()
        {
            var (client, _, profile, product) = await ArrangeB2BCustomerAsync();
            var (quotationId, _) = await ArrangeCeoApprovedQuotationAsync(client, product.Id);

            (await client.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                new { IsAccepted = true })).IsSuccessStatusCode.Should().BeTrue();

            // Khách sửa giỏ SAU khi báo giá đã chốt: 1 -> 2 đơn vị (120tr -> 240tr).
            await SeedCartAsync(profile.Id, null, (product.Id, 2, 120_000_000m));

            var res = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            if (!res.IsSuccessStatusCode) return; // hành vi đúng theo SRS

            var orderId = (await ReadJsonAsync(res)).GetProperty("orderId").GetGuid();
            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));

            order.FinalPayment.Should().NotBe(110_000_000m,
                "DEF-L3-003: giỏ 240.000.000đ bị áp nguyên giá 110.000.000đ của version đã duyệt cho " +
                "một giỏ KHÁC — hệ thống phải chặn bằng QUOTATION_VERSION_STALE và bắt duyệt lại");
        }

        /// QUO-06 | Input-Domain-Error | FT-02 NAC-05; BR-020
        /// Tin nhắn báo giá đã gửi là BẤT BIẾN — hệ thống không mở route sửa tin nhắn.
        /// Nhóm B: bất biến được bảo đảm bằng việc KHÔNG tồn tại endpoint.
        [Fact]
        public async Task L3_QUO_06_EditSentQuotationMessage_RouteDoesNotExist_ImmutableByAbsence()
        {
            var (client, _, _, product) = await ArrangeB2BCustomerAsync();
            var quotationId = await CreateQuotationAsync(client);
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/pickup", new { });

            var send = await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/messages",
                new { MessageText = "Bao gia 110 trieu" });
            send.IsSuccessStatusCode.Should().BeTrue();
            var messageId = await QueryAsync(db => db.ChatMessages
                .Where(m => m.QuotationId == quotationId).Select(m => m.Id).FirstAsync());
            var originalText = await QueryAsync(db => db.ChatMessages
                .Where(m => m.Id == messageId).Select(m => m.MessageText).SingleAsync());

            var res = await sales.PutAsJsonAsync($"/api/Quotation/{quotationId}/messages/{messageId}",
                new { MessageText = "NOI DUNG DA BI SUA" });

            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            (await QueryAsync(db => db.ChatMessages.Where(m => m.Id == messageId)
                    .Select(m => m.MessageText).SingleAsync()))
                .Should().Be(originalText,
                    "không có route sửa tin nhắn — đó chính là cách BR-020 được bảo đảm");
        }

        /// QUO-07 | Input-Domain-Error | FT-02 NAC-05; NFR-SEC03
        /// Khách KHÁC đọc nội dung đàm phán -> bị chặn, không trả nội dung.
        [Fact]
        public async Task L3_QUO_07_GetMessages_ByOtherCustomer_Forbidden_NoContentLeak()
        {
            var (owner, _, _, _) = await ArrangeB2BCustomerAsync();
            var quotationId = await CreateQuotationAsync(owner);
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/pickup", new { });
            await sales.PostAsJsonAsync($"/api/Quotation/{quotationId}/messages",
                new { MessageText = "NOI DUNG DAM PHAN BI MAT" });

            var (intruder, intruderUser) = await CreateClientAsAsync(SystemRole.Customer);
            await EnsureProfileAsync(intruderUser.Id);

            var res = await intruder.GetAsync($"/api/Quotation/{quotationId}/messages");

            res.IsSuccessStatusCode.Should().BeFalse("không phải chủ sở hữu thì không được đọc");
            (await res.Content.ReadAsStringAsync()).Should().NotContain("NOI DUNG DAM PHAN BI MAT");
        }

        /// QUO-08 | BVA | FT-02 AC-02; NAC-03; BV-02; BR-025
        /// Version báo giá đã HẾT HẠN (ValidUntil ở quá khứ) -> khách không chấp nhận được nữa.
        [Fact]
        public async Task L3_QUO_08_CustomerDecision_ExpiredVersion_Rejected()
        {
            var (client, _, _, product) = await ArrangeB2BCustomerAsync();
            var (quotationId, versionId) = await ArrangeCeoApprovedQuotationAsync(client, product.Id);

            await SeedAsync(async db =>
            {
                var v = await db.QuotationVersions.SingleAsync(x => x.Id == versionId);
                v.ValidUntil = DateTime.UtcNow.AddSeconds(-1); // vừa hết hạn
            });

            var res = await client.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                new { IsAccepted = true });

            res.IsSuccessStatusCode.Should().BeFalse("version hết hạn thì không được chấp nhận");
            var quotation = await QueryAsync(db => db.Quotations.SingleAsync(q => q.Id == quotationId));
            quotation.Status.Should().Be(QuotationStatus.Expired);
            quotation.AcceptedVersionId.Should().BeNull("không được chốt version đã hết hạn");
        }
    }
}
