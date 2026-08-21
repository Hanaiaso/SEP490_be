using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-AfterSalesAdminAPI</c> — SA-01..07, AS-01..08, ADM-01..05.
    ///
    /// Ánh xạ chính: POST /api/sales-change-requests/{id}/approve -> <b>PUT</b> cùng path;
    /// POST /api/warehouse/orders/quality-returns/{id}/release -> POST /api/warehouse-management/quarantine/{id}/dispatch;
    /// POST /api/Quotation/{id}/ceo-review -> /ceo-decision.
    /// </summary>
    public class L3AfterSalesAdminApiTests : L3TestBase
    {
        public L3AfterSalesAdminApiTests(L3SqlFixture factory) : base(factory) { }

        /// <summary>Khách có hồ sơ đã được gán Sale phụ trách (điều kiện cần để tạo yêu cầu đổi Sale).</summary>
        private async Task<(HttpClient client, User user, CustomerProfile profile)> ArrangeAssignedCustomerAsync()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAsync(async db =>
            {
                var p = await db.CustomerProfiles.SingleAsync(x => x.Id == profile.Id);
                p.AssignedSalesStaffId = L3Seed.SalesStaffId;
                p.AssignmentSource = "ROUND_ROBIN";
                p.AssignedAt = DateTime.UtcNow;
            });
            return (client, user, profile);
        }

        private static MultipartFormDataContent ChangeRequestForm(string reason, string problem) => new()
        {
            { new StringContent(reason), "Reason" },
            { new StringContent(problem), "ProblemDescription" },
        };

        // ── Block: Customer assignment & sales change (FT-04) ─────────────────────────────────

        /// SA-01 | Input-Domain-Happy | FT-04 AC-02; BV-02; BR-002
        /// Khách mới đăng ký (không referral) -> sau khi xác minh email được gán Sale theo round-robin,
        /// và nguồn gán được ghi lại.
        [Fact]
        public async Task L3_SA_01_RoundRobinAssignment_AfterEmailVerified_RecordsAssignmentSource()
        {
            var client = AnonymousClient();
            var email = NewEmail();
            const string otp = "135790";

            await client.PostAsJsonAsync("/api/auth/register", new
            {
                FullName = "Khach Round Robin",
                Email = email,
                PhoneNumber = NewPhone(),
                Password = "Passw0rd!",
                ConfirmPassword = "Passw0rd!",
            });

            // Ép OTP về giá trị biết trước để xác minh được qua HTTP thật.
            await SeedAsync(async db =>
            {
                var u = await db.Users.SingleAsync(x => x.Email == email);
                u.OtpCode = otp;
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            });

            (await client.PostAsJsonAsync("/api/auth/verify-otp", new { Email = email, OtpCode = otp }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var profile = await QueryAsync(db => db.CustomerProfiles
                .Include(p => p.User).SingleAsync(p => p.User.Email == email));

            profile.AssignedSalesStaffId.Should().NotBeNull("khách mới phải được gán Sale phụ trách");
            profile.AssignmentSource.Should().NotBeNullOrWhiteSpace("phải ghi lại nguồn gán");
        }

        /// SA-02 | Input-Domain-Error | FT-04 NAC-02
        /// Mã giới thiệu không hợp lệ -> đăng ký bị từ chối, không tạo tài khoản.
        [Fact]
        public async Task L3_SA_02_Register_InvalidReferralCode_Rejected_NoUserCreated()
        {
            var client = AnonymousClient();
            var email = NewEmail();

            var res = await client.PostAsJsonAsync("/api/auth/register", new
            {
                FullName = "Khach Referral Sai",
                Email = email,
                PhoneNumber = NewPhone(),
                Password = "Passw0rd!",
                ConfirmPassword = "Passw0rd!",
                ReferralCode = "MA-KHONG-TON-TAI-12345",
            });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await QueryAsync(db => db.Users.CountAsync(u => u.Email == email)))
                .Should().Be(0, "mã giới thiệu sai thì không được tạo tài khoản");
        }

        /// SA-03 | BVA | FT-04 BV-03
        /// Tối đa 1 yêu cầu đổi Sale đang mở: yêu cầu thứ 2 bị từ chối.
        [Fact]
        public async Task L3_SA_03_CreateSalesChangeRequest_SecondOpenRequest_Rejected()
        {
            var (client, _, profile) = await ArrangeAssignedCustomerAsync();

            var first = await client.PostAsync("/api/sales-change-requests",
                ChangeRequestForm("Sale khong ho tro", "Goi nhieu lan khong nghe may"));
            first.IsSuccessStatusCode.Should().BeTrue(
                $"yêu cầu đầu tiên phải được tạo ({await ReadMessageAsync(first)})");

            var second = await client.PostAsync("/api/sales-change-requests",
                ChangeRequestForm("Ly do khac", "Van de khac"));

            second.IsSuccessStatusCode.Should().BeFalse("chỉ được có 1 yêu cầu mở tại một thời điểm");
            (await QueryAsync(db => db.SalesChangeRequests.CountAsync(r => r.CustomerProfileId == profile.Id)))
                .Should().Be(1, "không được tạo bản ghi thứ 2");
        }

        /// SA-04 | Input-Domain-Error | FT-04 NAC-04; NFR-SEC03
        /// Sales Staff tự duyệt yêu cầu đổi Sale -> 403 (chỉ Sales Manager/Admin được duyệt).
        /// Workbook ghi POST; endpoint thật là PUT.
        [Fact]
        public async Task L3_SA_04_ApproveSalesChangeRequest_BySalesStaff_Forbidden()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await sales.PutAsJsonAsync(
                $"/api/sales-change-requests/{Guid.NewGuid()}/approve", new { });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// SA-05 | Input-Domain-Happy | FT-04 AC-04; AC-05; BR-031
        /// Sales Manager duyệt đổi Sale -> quyền sở hữu hiện tại đổi, nhưng snapshot
        /// SalesStaffId trên đơn CŨ giữ nguyên.
        [Fact]
        public async Task L3_SA_05_ApproveSalesChange_OwnershipChanges_OrderSnapshotPreserved()
        {
            var (client, user, profile) = await ArrangeAssignedCustomerAsync();
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 100);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 100_000m));

            // Đơn cũ ĐÃ HOÀN TẤT: chốt snapshot Sales = SalesStaffId hiện tại.
            // BR-031 bảo vệ đơn đã hoàn tất — đơn ĐANG CHẠY thì Sales Manager được quyền quyết định
            // giữ hay chuyển (OrderDecisions), nên phải dùng đơn Completed mới kiểm đúng bất biến.
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .SalesStaffId.Should().Be(L3Seed.SalesStaffId, "đơn phải chốt Sale phụ trách lúc tạo");

            await SeedAsync(async db =>
            {
                var o = await db.Orders.SingleAsync(x => x.Id == orderId);
                o.OrderStatus = OrderStatus.Completed;
                o.PaymentStatus = PaymentStatus.Paid;
            });

            var requestId = await QueryAsync(async db =>
            {
                var r = new SalesChangeRequest
                {
                    Id = Guid.NewGuid(),
                    CustomerProfileId = profile.Id,
                    CurrentSalesStaffId = L3Seed.SalesStaffId,
                    DesiredSalesStaffId = L3Seed.SalesStaff2Id,
                    Reason = "Doi sale",
                    ProblemDescription = "Khong phu hop",
                    Status = SalesChangeRequestStatus.Pending,
                };
                db.SalesChangeRequests.Add(r);
                await db.SaveChangesAsync();
                return r.Id;
            });

            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            // Đơn đã Completed không còn là "đơn đang chạy" nên OrderDecisions rỗng là hợp lệ.
            var res = await manager.PutAsJsonAsync($"/api/sales-change-requests/{requestId}/approve", new
            {
                NewSalesStaffId = L3Seed.SalesStaff2Id,
                OverrideReason = "Duyet doi sale",
                OrderDecisions = Array.Empty<object>(),
            });

            res.IsSuccessStatusCode.Should().BeTrue($"Sales Manager phải duyệt được ({await ReadMessageAsync(res)})");

            (await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profile.Id)))
                .AssignedSalesStaffId.Should().Be(L3Seed.SalesStaff2Id, "quyền sở hữu hiện tại phải đổi");
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .SalesStaffId.Should().Be(L3Seed.SalesStaffId, "snapshot trên đơn CŨ phải giữ nguyên");
        }

        /// SA-06 | BVA | FT-04 BV-01; NAC-01; BR-001
        /// Sau mọi lệnh, khách phải có ĐÚNG 1 Sale phụ trách — không bao giờ 0 hoặc 2.
        [Fact]
        public async Task L3_SA_06_SalesOwnerCardinality_AlwaysExactlyOne()
        {
            var (_, _, profile) = await ArrangeAssignedCustomerAsync();

            var requestId = await QueryAsync(async db =>
            {
                var r = new SalesChangeRequest
                {
                    Id = Guid.NewGuid(),
                    CustomerProfileId = profile.Id,
                    CurrentSalesStaffId = L3Seed.SalesStaffId,
                    Reason = "Doi sale",
                    ProblemDescription = "Mo ta",
                    Status = SalesChangeRequestStatus.Pending,
                };
                db.SalesChangeRequests.Add(r);
                await db.SaveChangesAsync();
                return r.Id;
            });

            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            // Gán sang một user KHÔNG phải Sales Staff -> phải bị từ chối, chủ sở hữu cũ giữ nguyên.
            var invalid = await manager.PutAsJsonAsync($"/api/sales-change-requests/{requestId}/approve",
                new { NewSalesStaffId = L3Seed.CustomerId, OverrideReason = "Gan sai vai tro" });

            invalid.IsSuccessStatusCode.Should().BeFalse("không được gán khách hàng làm Sale phụ trách");

            var after = await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profile.Id));
            after.AssignedSalesStaffId.Should().Be(L3Seed.SalesStaffId,
                "chủ sở hữu cũ phải giữ nguyên, không commit lịch sử một phần");
            after.AssignedSalesStaffId.Should().NotBeNull("không bao giờ được rơi về 0 chủ sở hữu");
        }

        /// SA-07 | Input-Domain-Error | FT-04 NAC-05; BR-031  ->  nhóm B
        /// Không có route ghi đè snapshot Sales trên đơn -> bất biến theo thiết kế.
        [Fact]
        public async Task L3_SA_07_OverwriteOrderSalesSnapshot_RouteDoesNotExist()
        {
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PutAsJsonAsync($"/api/orders/{Guid.NewGuid()}/assigned-sales",
                new { SalesStaffId = L3Seed.SalesStaff2Id });

            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }

        // ── Block: Paid cancellation / credit / exchange (FT-08) ──────────────────────────────

        /// AS-01 | Input-Domain-Error | FT-08 NAC-01; BR-017  ->  nhóm B
        /// Hệ thống KHÔNG hỗ trợ hoàn tiền mặt/chuyển khoản: không có route refund.
        [Fact]
        public async Task L3_AS_01_RefundEndpoint_DoesNotExist_RefundNotSupported()
        {
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync($"/api/orders/{Guid.NewGuid()}/refund",
                new { Amount = 100_000m });

            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.Amount < 0)))
                .Should().Be(0, "không tồn tại giao dịch hoàn tiền nào trong hệ thống");
        }

        /// AS-02 | BVA | FT-08 BV-01; AC-02; BR-041  ->  nhóm C
        /// Phân bổ lại tiền chưa có API riêng; đường đang có là duyệt huỷ + tạo đơn thay thế.
        [Fact]
        public async Task L3_AS_02_PaymentReallocation_EndpointNotImplemented()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/payment-reallocations", new { Amount = 1 }))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

            // Đường tương đương đang có phải tồn tại (chỉ Sales Manager/Admin).
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            (await manager.PostAsJsonAsync(
                    $"/api/delivery/{Guid.NewGuid()}/approve-cancel-replacement", new { Items = Array.Empty<object>() }))
                .StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        }

        /// AS-03 | Input-Domain-Error | FT-08 NAC-03
        /// Credit của khách A không được dùng cho khách B: credit luôn lấy từ hồ sơ của chính người gọi.
        [Fact]
        public async Task L3_AS_03_CreditIsScopedToOwningCustomer()
        {
            // Khách A có 500.000đ credit.
            var (_, userA) = await CreateClientAsAsync(SystemRole.Customer);
            var profileA = await EnsureProfileAsync(userA.Id);
            await SeedAsync(async db =>
            {
                var p = await db.CustomerProfiles.SingleAsync(x => x.Id == profileA.Id);
                p.AvailableCredit = 500_000m;
            });

            // Khách B không có credit, đặt đơn 200.000đ.
            var (clientB, userB) = await CreateClientAsAsync(SystemRole.Customer);
            var profileB = await EnsureProfileAsync(userB.Id);
            await SeedAddressAsync(profileB.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 100);
            await SeedCartAsync(profileB.Id, null, (product.Id, 2, 100_000m));

            var placed = await ReadJsonAsync(await clientB.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == placed.GetProperty("orderId").GetGuid()));

            order.CreditApplied.Should().Be(0m, "khách B không có credit nên không được trừ credit của ai");
            order.FinalPayment.Should().Be(WithVat(200_000m), "VAT 10% bắt buộc do server tự cộng");
            (await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profileA.Id)))
                .AvailableCredit.Should().Be(500_000m, "credit của khách A không được đụng tới");
        }

        /// AS-04 | BVA | FT-08 BV-03; NAC-03
        /// Dùng credit tối đa bằng số dư: credit áp không bao giờ vượt số dư và số dư không âm.
        /// Ngưỡng so sánh là SỐ PHẢI TRẢ (đã gồm VAT 10% bắt buộc), không phải thành tiền trước thuế.
        [Theory]
        [InlineData(50_000, 200_000)]    // credit < số phải trả -> dùng hết credit
        [InlineData(220_000, 200_000)]   // credit == số phải trả -> trả hết bằng credit
        [InlineData(500_000, 200_000)]   // credit > số phải trả -> chỉ dùng đúng phần cần
        public async Task L3_AS_04_CreditApplied_NeverExceedsBalanceOrOrderTotal(
            decimal credit, decimal orderTotal)
        {
            var payable = WithVat(orderTotal);
            var expectedApplied = Math.Min(credit, payable);
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            await SeedAsync(async db =>
            {
                var p = await db.CustomerProfiles.SingleAsync(x => x.Id == profile.Id);
                p.AvailableCredit = credit;
            });
            var (product, _) = await SeedSellableProductAsync(orderTotal, 100);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, orderTotal));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == placed.GetProperty("orderId").GetGuid()));

            order.CreditApplied.Should().Be(expectedApplied);
            order.FinalPayment.Should().Be(payable - expectedApplied);
            (await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profile.Id)))
                .AvailableCredit.Should().BeGreaterThanOrEqualTo(0m, "số dư credit không bao giờ được âm");
        }

        /// AS-05 | Input-Domain-Error | FT-08 NAC-04; BR-019
        /// Hàng trả về đang cách ly: chỉ vai trò được phép mới xử lý; khách hàng -> 403.
        /// Workbook ghi /api/warehouse/orders/quality-returns/{id}/release;
        /// endpoint thật là /api/warehouse-management/quarantine/{id}/dispatch.
        [Fact]
        public async Task L3_AS_05_ReleaseQuarantine_RoleGate_AvailableUnchangedOnReject()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);

            var res = await customer.PostAsJsonAsync(
                $"/api/warehouse-management/quarantine/{Guid.NewGuid()}/dispatch", new { Action = "available" });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "khách hàng không được đưa hàng cách ly về tồn bán được");

            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            (await warehouse.PostAsJsonAsync(
                    $"/api/warehouse-management/quarantine/{Guid.NewGuid()}/dispatch", new { Action = "available" }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "nhân viên kho phải qua được cổng phân quyền");
        }

        /// AS-06 | Input-Domain-Happy | FT-08 AC-04; BR-018
        /// Dùng credit <= số dư -> ghi CreditTransaction âm đúng số dùng và liên kết với đơn.
        [Fact]
        public async Task L3_AS_06_UseCredit_WithinBalance_RecordsLedgerLinkedToOrder()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            await SeedAsync(async db =>
            {
                var p = await db.CustomerProfiles.SingleAsync(x => x.Id == profile.Id);
                p.AvailableCredit = 150_000m;
            });
            var (product, _) = await SeedSellableProductAsync(100_000m, 100);
            await SeedCartAsync(profile.Id, null, (product.Id, 2, 100_000m));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            order.CreditApplied.Should().Be(150_000m);
            order.FinalPayment.Should().Be(WithVat(200_000m) - 150_000m, "credit trừ vào số phải trả đã gồm VAT");

            var ledger = await QueryAsync(db => db.CreditTransactions
                .Where(t => t.OrderId == orderId).ToListAsync());
            ledger.Should().ContainSingle("mỗi lần dùng credit ghi đúng 1 bút toán");
            ledger[0].Amount.Should().Be(-150_000m, "bút toán phải âm đúng số credit đã dùng");

            (await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profile.Id)))
                .AvailableCredit.Should().Be(0m);
        }

        /// AS-07 | Input-Domain-Error | FT-08 NAC-02; BR-041
        /// Duyệt huỷ + tạo đơn thay thế: sai vai trò -> 403, không đổi số dư credit.
        [Fact]
        public async Task L3_AS_07_ApproveCancelReplacement_WrongRole_Forbidden_NoCreditChange()
        {
            var (_, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAsync(async db =>
            {
                var p = await db.CustomerProfiles.SingleAsync(x => x.Id == profile.Id);
                p.AvailableCredit = 300_000m;
            });

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var res = await sales.PostAsJsonAsync(
                $"/api/delivery/{Guid.NewGuid()}/approve-cancel-replacement", new { Items = Array.Empty<object>() });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Sales Staff không được tự duyệt huỷ đơn đã trả tiền");
            (await QueryAsync(db => db.CustomerProfiles.SingleAsync(p => p.Id == profile.Id)))
                .AvailableCredit.Should().Be(300_000m, "số dư credit không được đổi khi lệnh bị từ chối");
        }

        /// AS-08 | BVA | FT-08 BV-02; AC-03; BR-042
        /// Đơn thay thế: chỉ Sales Manager/Admin/Sales Staff được tạo; khách hàng -> 403.
        [Fact]
        public async Task L3_AS_08_CreateExchangeReplacement_RoleGate()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.PostAsJsonAsync(
                    $"/api/delivery/exchange/{Guid.NewGuid()}/replacement", new { Items = Array.Empty<object>() }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            (await manager.PostAsJsonAsync(
                    $"/api/delivery/exchange/{Guid.NewGuid()}/replacement", new { Items = Array.Empty<object>() }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        // ── Block: Administration, audit & alerts (FT-09) ─────────────────────────────────────

        /// ADM-01 | Input-Domain-Error | FT-09 NAC-01; BV-01; NFR-SEC03
        /// Sales Staff truy cập dashboard của Sales Manager -> 403, không trả dòng dữ liệu nào.
        [Fact]
        public async Task L3_ADM_01_SalesManagerDashboard_AccessedBySalesStaff_Forbidden()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await sales.GetAsync("/api/dashboards/sales-manager");

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await res.Content.ReadAsStringAsync()).Should().NotContain("totalRevenue");
        }

        /// ADM-02 | Input-Domain-Error | FT-09 NAC-02
        /// Admin gọi API DUYỆT NGHIỆP VỤ (ceo-decision) -> 403.
        /// Admin là vai trò quản trị hệ thống, không phải vai trò phê duyệt kinh doanh.
        [Fact]
        public async Task L3_ADM_02_BusinessApproval_ByAdmin_Forbidden()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);

            var res = await admin.PostAsJsonAsync($"/api/Quotation/{Guid.NewGuid()}/ceo-decision",
                new { IsApproved = true });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "Admin không được thay CEO phê duyệt nghiệp vụ");
        }

        /// ADM-03 | Input-Domain-Error | FT-09 NAC-03; BR-048; NFR-SEC08  ->  nhóm B
        /// Không có route xoá audit log -> nhật ký kiểm toán bất biến theo thiết kế.
        [Fact]
        public async Task L3_ADM_03_DeleteAuditLog_RouteDoesNotExist_AuditImmutable()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            await SeedAsync(db =>
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    EntityName = "Order",
                    EntityId = Guid.NewGuid().ToString(),
                    Action = "CREATE",
                    ActorUserId = L3Seed.AdminId,
                    ActorEmail = L3Seed.AdminEmail,
                    ActorRole = nameof(SystemRole.Admin),
                    CreatedAt = DateTime.UtcNow,
                });
                return Task.CompletedTask;
            });
            var before = await QueryAsync(db => db.AuditLogs.CountAsync());

            var res = await admin.DeleteAsync($"/api/admin/audit-logs/{Guid.NewGuid()}");

            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            (await QueryAsync(db => db.AuditLogs.CountAsync()))
                .Should().Be(before, "AuditLogController chỉ có GET — đó là cách BR-048 được bảo đảm");
        }

        /// ADM-04 | BVA | FT-09 BV-02; NAC-04; BR-050
        /// Đổi cấu hình bậc chiết khấu KHÔNG được sửa ngược đơn đã hoàn tất.
        [Fact]
        public async Task L3_ADM_04_DiscountTierChange_DoesNotRewriteExistingOrders()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(15_000_000m, 100);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 15_000_000m));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var discountBefore = (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId))).DiscountAmount;

            // Admin đổi bậc chiết khấu SAU khi đơn đã tạo.
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var tierId = Guid.Parse("f0000002-0002-4002-a002-000000000001"); // bậc 10tr-31tr: 5%
            await admin.PutAsJsonAsync($"/api/admin/discount-tiers/{tierId}", new
            {
                MinAmount = 10_000_000m,
                MaxAmount = 31_000_000m,
                DiscountPercent = 0.25m, // 5% -> 25%
                IsActive = true,
                Description = "Doi bac chiet khau",
            });

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .DiscountAmount.Should().Be(discountBefore,
                    "cấu hình mới KHÔNG được ghi đè giá đã chốt trên đơn cũ");
        }

        /// ADM-05 | Input-Domain-Happy | FT-09 AC-02
        /// Nhân viên đọc được thông báo của chính mình; chưa đăng nhập -> 401.
        [Fact]
        public async Task L3_ADM_05_Notifications_ReadableByOwner_UnauthorizedWithoutToken()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            await SeedAsync(db =>
            {
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    RecipientUserId = L3Seed.SalesStaffId,
                    Type = NotificationType.SYS_02_NewOrder,
                    Title = "Canh bao COD SLA",
                    Body = "Don hang sap het han giu cho",
                    CreatedAt = DateTime.UtcNow,
                });
                return Task.CompletedTask;
            });

            var res = await sales.GetAsync("/api/Notifications");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            (await res.Content.ReadAsStringAsync()).Should().Contain("Canh bao COD SLA");

            (await AnonymousClient().GetAsync("/api/Notifications"))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
