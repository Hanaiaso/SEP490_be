using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-Security</c> — phần chạy được bằng xUnit qua HTTP thật:
    /// SEC-01..04, 07..09, 11..14, 17, 19.
    ///
    /// Các case còn lại chạy ngoài suite này:
    ///   SEC-05 (ép HTTP -> HTTPS) và SEC-15 (security headers) — cần server thật, chạy bằng Newman.
    ///   SEC-06 (mật khẩu hash) và SEC-13 (sửa/xoá AuditLog ở mức DB) — chạy bằng SQL, xem tests/sql/.
    ///   SEC-10 (SQL Injection) và SEC-18 (race condition) — có bản xUnit ở đây, đối chứng bằng JMeter.
    /// </summary>
    public class L3SecurityTests : L3TestBase
    {
        public L3SecurityTests(L3SqlFixture factory) : base(factory) { }

        // ── A01 Broken Access Control ─────────────────────────────────────────────────────────

        /// SEC-01 | A01 | NFR-SEC03; FT-01 NAC-05
        /// IDOR: đổi orderId sang đơn của khách khác -> không lộ trường nào của đơn kia.
        [Fact]
        public async Task L3_SEC_01_Idor_OtherCustomerOrder_NoFieldLeak()
        {
            var (clientA, userA) = await CreateClientAsAsync(SystemRole.Customer);
            var profileA = await EnsureProfileAsync(userA.Id);
            await SeedAddressAsync(profileA.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 50);
            await SeedCartAsync(profileA.Id, null, (product.Id, 1, 100_000m));

            var placed = await ReadJsonAsync(await clientA.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            var (clientB, userB) = await CreateClientAsAsync(SystemRole.Customer);
            await EnsureProfileAsync(userB.Id);

            var res = await clientB.GetAsync($"/api/orders/my-history/{orderId}");

            res.IsSuccessStatusCode.Should().BeFalse("khách B không được đọc đơn của khách A");
            (await res.Content.ReadAsStringAsync()).Should().NotContain(orderCode);
        }

        /// SEC-02 | A01 | NFR-SEC03; FT-09 NAC-02
        /// Leo thang chiều dọc: Admin gọi API duyệt nghiệp vụ -> 403.
        [Fact]
        public async Task L3_SEC_02_VerticalEscalation_AdminCallsBusinessApproval_Forbidden()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);

            (await admin.PostAsJsonAsync($"/api/Quotation/{Guid.NewGuid()}/ceo-decision",
                    new { IsApproved = true }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await admin.PostAsJsonAsync($"/api/Quotation/{Guid.NewGuid()}/manager-decision",
                    new { IsApproved = true }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// SEC-03 | A01 | NFR-SEC03; FT-05 NAC-05
        /// Nhân viên kho thao tác kho KHÔNG được gán -> bị từ chối, tồn kho không đổi.
        [Fact]
        public async Task L3_SEC_03_WarehouseStaff_ActsOnUnassignedWarehouse_Rejected()
        {
            var inventoryId = L3Seed.InventoryPeWrapDefaultId; // thuộc WH-DEFAULT
            var before = await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == inventoryId));

            var otherWarehouse = await ClientForSeededAsync(L3Seed.WarehouseStaff2Id); // gán WH-TRADE
            var res = await otherWarehouse.PutAsJsonAsync($"/api/inventory/{inventoryId}/adjust",
                new { NewQuantity = 1, Reason = "vuot pham vi kho" });

            res.IsSuccessStatusCode.Should().BeFalse();
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == inventoryId)))
                .OnHandQuantity.Should().Be(before.OnHandQuantity);
        }

        /// SEC-04 | A01 | NFR-SEC03
        /// Quét endpoint nhạy cảm KHÔNG kèm Authorization -> 100% phải trả 401, không rò dữ liệu.
        [Theory]
        [InlineData("GET", "/api/orders/my-history")]
        [InlineData("GET", "/api/orders/sales")]
        [InlineData("GET", "/api/admin/users")]
        [InlineData("GET", "/api/admin/audit-logs")]
        [InlineData("GET", "/api/admin/system-configs")]
        [InlineData("GET", "/api/admin/system-health/job-runs")]
        [InlineData("GET", "/api/dashboards/ceo")]
        [InlineData("GET", "/api/dashboards/sales-manager")]
        [InlineData("GET", "/api/Notifications")]
        [InlineData("GET", "/api/Cart")]
        [InlineData("GET", "/api/customer-profile")]
        [InlineData("GET", "/api/stock-transfers")]
        [InlineData("GET", "/api/purchase-orders")]
        [InlineData("GET", "/api/goods-issues")]
        [InlineData("GET", "/api/warehouse/orders?tabType=OnlinePending")]
        [InlineData("GET", "/api/Quotation")]
        [InlineData("GET", "/api/sales/my-customers")]
        [InlineData("POST", "/api/orders/place-order")]
        public async Task L3_SEC_04_ProtectedEndpoint_WithoutToken_Returns401(string method, string path)
        {
            var client = AnonymousClient();

            var res = method == "GET"
                ? await client.GetAsync(path)
                : await client.PostAsJsonAsync(path, new { });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"{method} {path} phải được bảo vệ, không được trả dữ liệu cho request ẩn danh");
        }

        // ── A02 / A07 Cryptographic & Auth failures ───────────────────────────────────────────

        /// SEC-07 | A07 | NFR-SEC02
        /// Dùng lại refresh token đã rotate -> 401.
        [Fact]
        public async Task L3_SEC_07_ReuseRotatedRefreshToken_Unauthorized()
        {
            var client = AnonymousClient();
            var login = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = L3Seed.CustomerEmail, Password = L3Seed.DefaultPassword });
            var rt1 = (await ReadJsonAsync(login)).GetProperty("data").GetProperty("refreshToken").GetString()!;

            (await client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = rt1 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            (await client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = rt1 }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "token cũ phải bị vô hiệu hoá");
        }

        /// SEC-08 | A07 | NFR-SEC04; FT-01 BV-02
        /// Brute-force OTP: sai liên tiếp -> bị chặn, và OTP KHÔNG xuất hiện trong thân phản hồi.
        [Fact]
        public async Task L3_SEC_08_OtpBruteForce_BlockedAndOtpNeverEchoed()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer, u => u.IsPhoneVerified = false);
            var phone = NewPhone();
            const string realOtp = "246813";

            await SeedAsync(async db =>
            {
                var u = await db.Users.SingleAsync(x => x.Id == user.Id);
                u.PhoneOtpCode = $"{BCrypt.Net.BCrypt.HashPassword(realOtp)}:{phone}";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5);
                u.PhoneOtpFailedAttempts = 0;
            });

            for (var i = 1; i <= 5; i++)
            {
                var res = await client.PostAsJsonAsync("/api/auth/verify-phone-otp",
                    new { OtpCode = "999999", PhoneNumber = phone });
                (await res.Content.ReadAsStringAsync()).Should().NotContain(realOtp,
                    $"lần {i}: OTP thật không bao giờ được lộ ra phản hồi");
            }

            var blocked = await client.PostAsJsonAsync("/api/auth/verify-phone-otp",
                new { OtpCode = realOtp, PhoneNumber = phone });

            blocked.IsSuccessStatusCode.Should().BeFalse("vượt giới hạn thì mã ĐÚNG cũng bị chặn");
            (await QueryAsync(db => db.Users.SingleAsync(u => u.Id == user.Id)))
                .IsPhoneVerified.Should().BeFalse();
        }

        /// SEC-09 | A07 | NFR-SEC02
        /// JWT tự ký bằng khoá của kẻ tấn công -> 401 (chữ ký không hợp lệ).
        [Fact]
        public async Task L3_SEC_09_ForgedJwtSignature_Unauthorized()
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateForgedJwt());

            var res = await client.GetAsync("/api/orders/my-history");

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "chữ ký sai phải bị từ chối");
        }

        // ── A03 / A08 Injection & Integrity ───────────────────────────────────────────────────

        /// SEC-10 | A03 | NFR-SEC03
        /// SQL Injection qua tham số tìm kiếm -> không trả toàn bộ bảng, không lộ lỗi SQL.
        [Theory]
        [InlineData("' OR 1=1--")]
        [InlineData("'; DROP TABLE Products;--")]
        [InlineData("' UNION SELECT NULL,NULL,NULL--")]
        public async Task L3_SEC_10_SqlInjectionInSearch_Neutralised(string payload)
        {
            var client = AnonymousClient();
            var totalProducts = await QueryAsync(db => db.Products.CountAsync());

            var res = await client.GetAsync($"/api/products?search={Uri.EscapeDataString(payload)}");

            res.StatusCode.Should().Be(HttpStatusCode.OK, "EF tham số hoá nên truy vấn vẫn chạy bình thường");
            var body = await res.Content.ReadAsStringAsync();
            body.Should().NotContainAny("SqlException", "Incorrect syntax", "System.Data.SqlClient",
                "không được lộ chi tiết lỗi SQL ra ngoài");

            // Bảng Products vẫn còn nguyên -> payload không được thực thi.
            (await QueryAsync(db => db.Products.CountAsync())).Should().Be(totalProducts);
        }

        /// SEC-11 | A03 | NFR-U02
        /// XSS: nội dung &lt;script&gt; nhập vào phải được trả về dưới dạng DỮ LIỆU, không phải HTML
        /// thực thi được.
        ///
        /// Phạm vi L3 chỉ kiểm được tới lớp API: phản hồi phải là <c>application/json</c> (trình duyệt
        /// không bao giờ thực thi thân JSON như HTML) và payload được lưu nguyên văn dưới dạng chuỗi.
        /// Việc escape KHI HIỂN THỊ là trách nhiệm của frontend — thuộc L4, xem Report_5_4.
        ///
        /// Ghi nhận thêm: API trả `&lt;` nguyên văn thay vì `<`, tức đang dùng
        /// UnsafeRelaxedJsonEscaping. Vô hại với Content-Type JSON đúng chuẩn, nhưng mất một lớp
        /// phòng vệ chiều sâu nếu sau này có endpoint nào trả nhầm text/html.
        [Fact]
        public async Task L3_SEC_11_XssPayload_StoredAsDataNotExecutableHtml()
        {
            const string payload = "<script>alert(1)</script>";
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            await EnsureProfileAsync(user.Id);

            var update = await client.PutAsJsonAsync("/api/customer-profile", new
            {
                CompanyName = payload,
                TaxCode = "0123456789",
            });
            update.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

            var read = await client.GetAsync("/api/customer-profile");
            read.StatusCode.Should().Be(HttpStatusCode.OK);

            read.Content.Headers.ContentType!.MediaType.Should().Be("application/json",
                "phản hồi PHẢI là JSON — nếu trả text/html thì payload sẽ thực thi được trên trình duyệt");

            // Payload được giữ nguyên văn dưới dạng chuỗi dữ liệu, không bị diễn giải thành cấu trúc.
            var companyName = (await ReadJsonAsync(read)).GetProperty("companyName").GetString();
            companyName.Should().Be(payload, "nội dung phải được lưu nguyên văn như dữ liệu");
        }

        /// SEC-12 | A08 | NFR-SEC05; FT-03 NAC-01
        /// Giả mạo webhook SePay không có token hợp lệ -> 401, KHÔNG đổi trạng thái thanh toán,
        /// và vẫn ghi lại dấu vết.
        [Theory]
        [InlineData(null)]              // không gửi token
        [InlineData("token-gia-mao")]   // token sai
        public async Task L3_SEC_12_ForgedSePayWebhook_Rejected_PaymentUnchanged(string? token)
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

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay-callback")
            {
                Content = JsonContent.Create(new
                {
                    gateway = "TPBank",
                    accountNumber = "0000000000",
                    transferAmount = 200_000m,
                    transferContent = orderCode,
                    content = orderCode,
                    referenceCode = "REF-FORGED",
                    referenceNumber = "REF-FORGED",
                }),
            };
            if (token != null) req.Headers.Add("x-sepay-token", token);

            var res = await AnonymousClient().SendAsync(req);

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .PaymentStatus.Should().Be(PaymentStatus.Pending, "webhook giả mạo không được đổi trạng thái");
            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                .Should().Be(0);
        }

        /// SEC-14 | A08 | NFR-SEC07  ->  <b>FAIL — defect DEF-L3-008 (P2)</b>
        ///
        /// Theo NFR-SEC07, file ngoài allowlist phải bị từ chối (415/413) và không được lưu trữ.
        ///
        /// THỰC TẾ: <c>UserProfileService.UploadAvatarAsync</c> (UserProfileService.cs:83-85) chỉ kiểm
        /// PHẦN MỞ RỘNG của tên file — thứ do chính người gửi đặt — và kích thước. Không kiểm magic
        /// byte, cũng không kiểm Content-Type thật. Một file PE/EXE (header <c>MZ</c>) đổi tên thành
        /// <c>.png</c> đi lọt hoàn toàn và được đẩy lên kho lưu trữ.
        ///
        /// Mức độ P2 (không phải P1): file được đẩy sang Cloudinary chứ không nằm/không thực thi trên
        /// máy chủ ứng dụng. Rủi ro là biến kho ảnh thành nơi phát tán mã độc dưới tên miền công ty.
        ///
        /// Test này CỐ Ý đỏ: sẽ xanh khi có bước kiểm magic byte trước lúc upload.
        [Fact]
        public async Task L3_SEC_14_UploadDisallowedFileType_MustBeRejected()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.Customer);

            using var content = new MultipartFormDataContent();
            var exe = new ByteArrayContent(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }); // header MZ (PE/EXE)
            exe.Headers.ContentType = new MediaTypeHeaderValue("image/png");        // MIME nói dối
            content.Add(exe, "file", "payload.png");

            var res = await client.PostAsync("/api/user/avatar", content);

            res.IsSuccessStatusCode.Should().BeFalse(
                "DEF-L3-008: file PE/EXE đổi đuôi .png phải bị từ chối — hiện chỉ kiểm phần mở rộng " +
                "tên file nên đi lọt và được lưu trữ");
        }

        /// SEC-16 | A09 Logging Failures | NFR-SEC06
        /// PII và secret KHÔNG được lọt vào nhật ký ứng dụng.
        ///
        /// Hệ thống có <c>SensitiveDataRedactor</c> đang được dùng ở AuditLog / JobRun.ErrorMessage /
        /// WebhookLog.LastError: từ khoá nhạy cảm (password/otp/token/secret/apikey/pin) bị REDACT hẳn,
        /// còn PII (phone/taxcode/mst) bị che một phần theo dạng <c>0912***678</c>.
        /// Test đi qua HTTP thật rồi soi thẳng nội dung đã ghi xuống bảng AuditLogs.
        [Fact]
        public async Task L3_SEC_16_AuditLog_RedactsSecretsAndMasksPii()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var email = NewEmail();
            const string password = "SieuBiMat_2026!";
            var phone = "0912345678";

            // Tạo tài khoản qua API -> AdminUserService ghi AuditLog cho hành động này.
            var res = await admin.PostAsJsonAsync("/api/admin/users", new
            {
                FullName = "Nhan vien kiem thu PII",
                Email = email,
                PhoneNumber = phone,
                Password = password,
                Role = nameof(SystemRole.SalesStaff),
            });
            res.IsSuccessStatusCode.Should().BeTrue($"tạo tài khoản phải thành công ({await ReadMessageAsync(res)})");

            var logs = await QueryAsync(db => db.AuditLogs
                .Where(a => a.EntityName == "User")
                .Select(a => (a.BeforeJson ?? "") + "|" + (a.AfterJson ?? "") + "|" + (a.Reason ?? ""))
                .ToListAsync());
            logs.Should().NotBeEmpty("hành động quản trị phải để lại vết audit");

            var allLogText = string.Join("\n", logs);

            allLogText.Should().NotContain(password,
                "mật khẩu tuyệt đối không được xuất hiện trong nhật ký");
            allLogText.Should().NotContain(phone,
                $"số điện thoại phải được che một phần (dạng 0912***678), không ghi nguyên {phone}");

            // Response của API cũng không được vọng lại mật khẩu hay hash.
            var body = await res.Content.ReadAsStringAsync();
            body.Should().NotContain(password);
            body.Should().NotContain("passwordHash");
        }

        /// SEC-17 | A04 | NFR-SEC05; FT-02 NAC-01
        /// Bỏ qua giá server: client gửi tổng tiền thấp hơn thực tế -> server phải tự tính lại.
        [Fact]
        public async Task L3_SEC_17_ClientSuppliedTotal_IgnoredByServer()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(1_000_000m, 50);
            await SeedCartAsync(profile.Id, null, (product.Id, 3, 1_000_000m));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order", new
            {
                PaymentMethod = PaymentMethod.COD,
                TotalAmount = 1m,
                FinalPayment = 1m,
                DiscountAmount = 2_999_999m,
            }));

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == placed.GetProperty("orderId").GetGuid()));
            order.TotalAmount.Should().Be(3_000_000m, "server phải tự tính từ giỏ");
            order.FinalPayment.Should().Be(WithVat(3_000_000m), "VAT 10% bắt buộc do server tự cộng");
            order.DiscountAmount.Should().Be(0m);
        }

        /// SEC-18 | A04 | NFR-D01; NFR-A05; BR-032
        /// Race condition: 2 request ĐỒNG THỜI mua đơn vị tồn cuối cùng.
        /// Chỉ 1 được phép thành công; tồn khả dụng THÔ không bao giờ âm.
        [Fact]
        public async Task L3_SEC_18_ConcurrentCheckout_LastUnit_OnlyOneSucceeds()
        {
            var (product, _) = await SeedSellableProductAsync(100_000m, 1); // CHỈ CÒN 1 đơn vị

            var (clientA, userA) = await CreateClientAsAsync(SystemRole.Customer);
            var profileA = await EnsureProfileAsync(userA.Id);
            await SeedAddressAsync(profileA.Id);
            await SeedCartAsync(profileA.Id, null, (product.Id, 1, 100_000m));

            var (clientB, userB) = await CreateClientAsAsync(SystemRole.Customer);
            var profileB = await EnsureProfileAsync(userB.Id);
            await SeedAddressAsync(profileB.Id);
            await SeedCartAsync(profileB.Id, null, (product.Id, 1, 100_000m));

            var body = new { PaymentMethod = PaymentMethod.COD };
            var results = await Task.WhenAll(
                clientA.PostAsJsonAsync("/api/orders/place-order", body),
                clientB.PostAsJsonAsync("/api/orders/place-order", body));

            results.Count(r => r.IsSuccessStatusCode).Should().Be(1,
                "chỉ 1 trong 2 khách được mua đơn vị tồn cuối cùng");

            var inv = await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id));
            // Kiểm biểu thức THÔ — thuộc tính AvailableQuantity đã Math.Max(0, ...) nên che mất giá trị âm.
            (inv.OnHandQuantity - inv.ReservedQuantity - inv.AllocatedQuantity
             - inv.DamagedQuantity - inv.QuarantineQuantity)
                .Should().BeGreaterThanOrEqualTo(0, "tồn khả dụng thô không bao giờ được âm");
            inv.ReservedQuantity.Should().Be(1, "đúng 1 đơn vị được giữ, không giữ đúp");
        }

        /// SEC-19 | A08 | NFR-D02; BR-021; BR-020; BR-048
        /// Chứng từ đã post, tin nhắn báo giá đã gửi, nhật ký audit đều KHÔNG sửa/xoá được.
        [Fact]
        public async Task L3_SEC_19_PostedDocumentsAndLogs_AreImmutable()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var admin = await ClientForSeededAsync(L3Seed.AdminId);

            // 1) Phiếu xuất đã POST -> post lần 2 phải bị từ chối.
            var (product, _) = await SeedSellableProductAsync(50_000m, 100);
            var issue = new GoodsIssue
            {
                Id = Guid.NewGuid(),
                Code = "GI-SEC19",
                Type = GoodsIssueType.SalesOrder,
                WarehouseId = L3Seed.WarehouseDefaultId,
                IssuedByUserId = L3Seed.WarehouseStaffId,
                Status = GoodsIssueStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.GoodsIssues.Add(issue);
                db.GoodsIssueItems.Add(new GoodsIssueItem
                { Id = Guid.NewGuid(), GoodsIssueId = issue.Id, ProductId = product.Id, Quantity = 5 });
                return Task.CompletedTask;
            });

            (await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { }))
                .StatusCode.Should().Be(HttpStatusCode.Conflict, "chứng từ đã post là bất biến");

            // 2) Không có route sửa phiếu nhập đã post.
            (await warehouse.PutAsJsonAsync($"/api/goods-receipts/{Guid.NewGuid()}", new { Note = "x" }))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

            // 3) Không có route sửa/xoá tin nhắn báo giá đã gửi.
            (await warehouse.PutAsJsonAsync(
                    $"/api/Quotation/{Guid.NewGuid()}/messages/{Guid.NewGuid()}", new { MessageText = "x" }))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

            // 4) Không có route xoá nhật ký audit.
            (await admin.DeleteAsync($"/api/admin/audit-logs/{Guid.NewGuid()}"))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }
    }
}
