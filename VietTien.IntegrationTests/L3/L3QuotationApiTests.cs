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

        /// QUO-05 | BVA | FT-02 BV-03; NAC-04; BR-007 (đã sửa lại luật nghiệp vụ ngày 2026-08-13)
        ///
        /// Luật CŨ (tới trước 2026-08-13): mọi báo giá đã duyệt là 1 tổng tiền cố định, chỉ áp đúng
        /// khi giỏ khớp CHÍNH XÁC từng dòng SKU+số lượng đã đàm phán — sửa số lượng dù 1 đơn vị cũng
        /// bị chặn, bắt xin báo giá mới (DEF-L3-003 mô tả bug KHÔNG kiểm tra khớp giỏ trước khi có luật này).
        ///
        /// Luật MỚI (theo yêu cầu người vận hành 2026-08-13): đơn giá/SKU đã đàm phán & duyệt trở thành
        /// "bảng giá riêng" của ĐÚNG khách đó cho SKU đó, áp cho MỌI số lượng ở mọi đơn hàng >=ngưỡng
        /// sau này (không giới hạn đúng số lượng đã đàm phán ban đầu). SKU nào khách chưa từng đàm phán
        /// vẫn tính giá niêm yết bình thường — không chặn cả đơn.
        ///
        /// Test này xác nhận: khách đàm phán 110tr/đơn vị cho 1 đơn vị, sau đó mua 2 đơn vị cùng SKU
        /// (không xin báo giá lại) -> đơn PHẢI được tạo (không bị chặn), và phải trả đúng
        /// 110.000.000đ × 2 = 220.000.000đ — không phải 110tr (giá cũ, thiếu tiền) và không phải
        /// 240tr giá niêm yết (bỏ qua đàm phán).
        [Fact]
        public async Task L3_QUO_05_PlaceOrder_QuantityChangedAfterQuotationAccepted_NegotiatedUnitPriceAppliedPerUnit()
        {
            var (client, _, profile, product) = await ArrangeB2BCustomerAsync();
            var (quotationId, _) = await ArrangeCeoApprovedQuotationAsync(client, product.Id);

            (await client.PostAsJsonAsync($"/api/Quotation/{quotationId}/customer-decision",
                new { IsAccepted = true })).IsSuccessStatusCode.Should().BeTrue();

            // Khách sửa giỏ SAU khi báo giá đã chốt: 1 -> 2 đơn vị cùng SKU (giá niêm yết 120tr/đơn vị).
            await SeedCartAsync(profile.Id, null, (product.Id, 2, 120_000_000m));

            var res = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            res.IsSuccessStatusCode.Should().BeTrue("giá/SKU đã đàm phán áp cho mọi số lượng, không còn bị chặn khi đổi số lượng");

            var orderId = (await ReadJsonAsync(res)).GetProperty("orderId").GetGuid();
            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));

            order.FinalPayment.Should().Be(220_000_000m,
                "110.000.000đ/đơn vị (giá đã đàm phán) × 2 đơn vị — không phải 110tr (thiếu tiền) hay 240tr (bỏ qua đàm phán)");
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
