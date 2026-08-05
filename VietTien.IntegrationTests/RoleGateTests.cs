using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// L3 — Phase 3: các case Blocked ở tầng L1 vì phân quyền enforce bằng [Authorize(Roles=...)]
    /// tại Controller (không unit-test được ở tầng service). Test qua HTTP thật để middleware
    /// Authorization thật sự chạy. Xem FIX_PLAN_2026-08-03.md mục "Phase 3".
    ///
    /// Quy ước mỗi case: (1) role KHÔNG được phép -> 403 Forbidden (bằng chứng gate hoạt động);
    /// (2) role ĐƯỢC phép + id không tồn tại -> response KHÁC 403 (404/400/200 tuỳ endpoint),
    /// chứng minh request đã lọt qua Authorization và tới được tầng service thật.
    /// </summary>
    public class RoleGateTests : IntegrationTestBase
    {
        public RoleGateTests(CustomWebApplicationFactory factory) : base(factory) { }

        // L1-ORD-47 | Non-manager (SalesStaff) approve huỷ đơn đã Paid -> forbidden
        [Fact]
        public async Task L1_ORD_47_ApproveCancelReplacement_NonManagerRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.PostAsJsonAsync($"/api/delivery/{Guid.NewGuid()}/approve-cancel-replacement",
                new { Items = new object[0] });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_ORD_47_ApproveCancelReplacement_ManagerRole_PassesAuthGate()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesManager);

            var res = await client.PostAsJsonAsync($"/api/delivery/{Guid.NewGuid()}/approve-cancel-replacement",
                new { Items = new object[0] });

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        // L1-ORD-53 | Sales Staff tự duyệt yêu cầu đổi/trả -> forbidden
        [Fact]
        public async Task L1_ORD_53_ProcessReturnExchange_SalesStaffRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.PostAsJsonAsync($"/api/orders/exchange-request/{Guid.NewGuid()}/process",
                new { IsApproved = true });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_ORD_53_ProcessReturnExchange_ManagerRole_PassesAuthGate()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesManager);

            var res = await client.PostAsJsonAsync($"/api/orders/exchange-request/{Guid.NewGuid()}/process",
                new { IsApproved = true });

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        // L1-ORD-71 | Vai trò ngoài Sales/Manager/Admin xem chi tiết đơn dạng Sales -> forbidden.
        // Đây chỉ xác nhận role-gate (đơn không tồn tại nên không phân biệt được 403 do role hay do
        // scope). Phần IDOR sâu hơn (Sales xem đơn của khách do Sales KHÁC phụ trách) đã fix, có test
        // riêng ở L1: VietTien.Tests/Services/OrderServiceTests.Scoping.cs (L1_ORD_71_*).
        [Fact]
        public async Task L1_ORD_71_GetSalesOrderDetail_CustomerRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.Customer);

            var res = await client.GetAsync($"/api/orders/sales/{Guid.NewGuid()}");

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_ORD_71_GetSalesOrderDetail_SalesStaffRole_PassesAuthGate()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.GetAsync($"/api/orders/sales/{Guid.NewGuid()}");

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        // L1-QUO-09 | Approval bởi vai trò không phải CEO -> forbidden
        [Fact]
        public async Task L1_QUO_09_CeoDecision_NonCeoRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.PostAsJsonAsync($"/api/Quotation/{Guid.NewGuid()}/ceo-decision",
                new { IsApproved = true });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_QUO_09_CeoDecision_CeoRole_PassesAuthGate()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.CEO);

            var res = await client.PostAsJsonAsync($"/api/Quotation/{Guid.NewGuid()}/ceo-decision",
                new { IsApproved = true });

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        // L1-AUD-07 | Export audit log bởi vai trò không phải Admin -> forbidden, không sinh file
        [Fact]
        public async Task L1_AUD_07_ExportAuditLogs_CustomerRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.Customer);

            var res = await client.GetAsync("/api/admin/audit-logs/export");

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_AUD_07_ExportAuditLogs_AdminRole_ReturnsCsv()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.Admin);

            var res = await client.GetAsync("/api/admin/audit-logs/export");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            res.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        }

        // L1-MKT-03 | Sales Staff tự duyệt bài của mình -> forbidden
        [Fact]
        public async Task L1_MKT_03_MakeDecision_SalesStaffRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.PostAsJsonAsync($"/api/marketing-posts/{Guid.NewGuid()}/decision",
                new { Action = "Approve" });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_MKT_03_MakeDecision_ManagerRole_PassesAuthGate()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesManager);

            var res = await client.PostAsJsonAsync($"/api/marketing-posts/{Guid.NewGuid()}/decision",
                new { Action = "Approve" });

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        // L1-DASH-02 | callerId luôn lấy từ JWT — vai trò khác SalesStaff không gọi được dashboard này.
        // (Không có cách truyền id người khác vào vì endpoint không nhận tham số đó — xem doc.)
        [Fact]
        public async Task L1_DASH_02_SalesStaffDashboard_CustomerRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.Customer);

            var res = await client.GetAsync("/api/dashboards/sales-staff");

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_DASH_02_SalesStaffDashboard_SalesStaffRole_ReturnsOwnData()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.GetAsync("/api/dashboards/sales-staff");

            res.StatusCode.Should().Be(HttpStatusCode.OK); // callerId lấy từ chính token này, không có tham số nào override được
        }

        // L1-DASH-05 | Vai trò Customer/Warehouse gọi dashboard cấp quản lý -> forbidden
        [Fact]
        public async Task L1_DASH_05_SalesManagerDashboard_CustomerRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.Customer);

            var res = await client.GetAsync("/api/dashboards/sales-manager");

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_DASH_05_CeoDashboard_WarehouseStaffRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);

            var res = await client.GetAsync("/api/dashboards/ceo");

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task L1_DASH_05_SalesManagerDashboard_SalesManagerRole_PassesAuthGate()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesManager);

            var res = await client.GetAsync("/api/dashboards/sales-manager");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
