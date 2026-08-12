using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Chốt MỘT LẦN hai khoảng lệch hệ thống giữa workbook Report_5_3 v1.3 và code thật, thay vì
    /// lặp lại cùng một ghi chú ở cả 172 case.
    ///
    /// Đây KHÔNG phải case trong workbook — là bằng chứng kỹ thuật kèm theo defect DEF-L3-001
    /// (thiếu error registry) và DEF-L3-002 (lệch mã HTTP so với SRS).
    /// </summary>
    public class L3ContractDriftTests : L3TestBase
    {
        public L3ContractDriftTests(L3SqlFixture factory) : base(factory) { }

        /// <summary>
        /// DEF-L3-001 — Workbook kỳ vọng ~80 mã lỗi nghiệp vụ trong thân phản hồi
        /// (PRICE_SNAPSHOT_EXPIRED, INVENTORY_INVARIANT_VIOLATION, AUTH_001, ...). Code KHÔNG có
        /// trường <c>errorCode</c> ở bất kỳ endpoint nào — mọi controller trả <c>{ message }</c>.
        ///
        /// Test quét các nhánh lỗi 4xx đại diện trải khắp 8 controller và khẳng định:
        ///   (1) không endpoint nào trả errorCode/error_code  ->  bằng chứng cho defect;
        ///   (2) endpoint nào cũng trả `message` khác rỗng    ->  vẫn có thông tin cho người dùng.
        ///
        /// Khi team bổ sung error registry, phần (1) sẽ đỏ — lúc đó chính là tín hiệu để cập nhật
        /// workbook và xoá defect DEF-L3-001.
        /// </summary>
        [Fact]
        public async Task L3_DRIFT_001_NoEndpointReturnsErrorCodeField()
        {
            var (customer, user) = await CreateClientAsAsync(SystemRole.Customer);
            await EnsureProfileAsync(user.Id);
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var anonymous = AnonymousClient();

            var errorResponses = new List<(string label, HttpResponseMessage res)>
            {
                ("POST /api/auth/register (email trùng)",
                    await anonymous.PostAsJsonAsync("/api/auth/register", new
                    {
                        FullName = "X", Email = L3Seed.CustomerEmail, PhoneNumber = NewPhone(),
                        Password = "Passw0rd!", ConfirmPassword = "Passw0rd!",
                    })),
                ("POST /api/auth/login (sai mật khẩu)",
                    await anonymous.PostAsJsonAsync("/api/auth/login",
                        new { Email = L3Seed.CustomerEmail, Password = "SaiMatKhau!" })),
                ("POST /api/orders/place-order (giỏ trống)",
                    await customer.PostAsJsonAsync("/api/orders/place-order",
                        new { PaymentMethod = PaymentMethod.COD })),
                ("GET /api/orders/checkout-summary (giỏ trống)",
                    await customer.GetAsync("/api/orders/checkout-summary")),
                ("POST /api/Quotation/from-cart (chưa đạt ngưỡng)",
                    await customer.PostAsJsonAsync("/api/Quotation/from-cart", new { })),
                ("POST /api/goods-issues/{id}/post (không tồn tại)",
                    await warehouse.PostAsJsonAsync($"/api/goods-issues/{Guid.NewGuid()}/post", new { })),
                ("POST /api/stock-transfers (kho nguồn == kho đích)",
                    await warehouse.PostAsJsonAsync("/api/stock-transfers", new
                    {
                        SourceWarehouseId = L3Seed.WarehouseDefaultId,
                        DestinationWarehouseId = L3Seed.WarehouseDefaultId,
                        Items = new[] { new { ProductId = L3Seed.ProductTapeTrongId, Quantity = 1 } },
                    })),
                ("PUT /api/admin/system-configs/{key} (hiệu lực quá khứ)",
                    await admin.PutAsJsonAsync("/api/admin/system-configs/QUOTATION_MIN_VALUE", new
                    {
                        Value = "1", EffectiveDate = DateTime.UtcNow.AddDays(-1), Reason = "hoi to",
                    })),
                ("POST /api/webhooks/sepay-callback (thiếu token)",
                    await anonymous.PostAsJsonAsync("/api/webhooks/sepay-callback", new { transferAmount = 1 })),
            };

            var withErrorCode = new List<string>();
            var withoutMessage = new List<string>();

            foreach (var (label, res) in errorResponses)
            {
                ((int)res.StatusCode).Should().BeInRange(400, 499, $"{label} phải là lỗi phía client");

                var raw = await res.Content.ReadAsStringAsync();
                JsonElement root;
                try { root = JsonDocument.Parse(raw).RootElement; }
                catch (JsonException) { continue; }
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (root.TryGetProperty("errorCode", out _) || root.TryGetProperty("error_code", out _))
                    withErrorCode.Add(label);

                var hasMessage = root.TryGetProperty("message", out var m)
                                 && !string.IsNullOrWhiteSpace(m.ToString());
                // ProblemDetails của ModelState dùng "title"/"errors" thay vì "message" — vẫn chấp nhận.
                var hasProblemDetails = root.TryGetProperty("title", out _) || root.TryGetProperty("errors", out _);
                if (!hasMessage && !hasProblemDetails) withoutMessage.Add(label);
            }

            withErrorCode.Should().BeEmpty(
                "DEF-L3-001: hiện KHÔNG endpoint nào trả errorCode. Workbook Report_5_3 kỳ vọng ~80 mã " +
                "lỗi nghiệp vụ. Nếu test này đỏ nghĩa là error registry đã được bổ sung — hãy cập nhật " +
                "workbook và đóng DEF-L3-001");

            withoutMessage.Should().BeEmpty(
                "dù chưa có errorCode, mọi nhánh lỗi vẫn phải trả thông điệp đọc được cho người dùng");
        }

        /// <summary>
        /// DEF-L3-002 — Mã HTTP của nhiều nhánh lỗi nghiệp vụ lệch so với SRS: SRS quy định 409
        /// (xung đột trạng thái/danh tính) và 429 (quá tần suất), code trả 400 cho phần lớn.
        ///
        /// Test này GHI NHẬN hiện trạng (không phải kỳ vọng đúng) để bảng kết quả có bằng chứng
        /// định lượng: liệt kê rõ nhánh nào lệch.
        /// </summary>
        [Fact]
        public async Task L3_DRIFT_002_BusinessErrorsUse400WhereSrsExpects409Or429()
        {
            var anonymous = AnonymousClient();

            // SRS: 409 DUPLICATE_IDENTITY
            var duplicate = await anonymous.PostAsJsonAsync("/api/auth/register", new
            {
                FullName = "X", Email = L3Seed.CustomerEmail, PhoneNumber = NewPhone(),
                Password = "Passw0rd!", ConfirmPassword = "Passw0rd!",
            });

            // SRS: 429 OTP_RESEND_TOO_SOON
            var email = NewEmail();
            await SeedAsync(db =>
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Resend",
                    Email = email,
                    PhoneNumber = NewPhone(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Passw0rd!"),
                    Role = SystemRole.Customer,
                    IsActive = true,
                    IsEmailVerified = false,
                    OtpCode = "111111",
                    OtpExpiry = DateTime.UtcNow.AddMinutes(5), // vừa gửi xong -> còn trong cooldown
                });
                return Task.CompletedTask;
            });
            var tooSoon = await anonymous.PostAsJsonAsync("/api/auth/resend-otp", new { Email = email });

            duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "DEF-L3-002: SRS quy định 409 cho trùng danh tính, code trả 400");
            tooSoon.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "DEF-L3-002: SRS quy định 429 cho gửi lại OTP quá sớm, code trả 400");

            // Bất biến quan trọng hơn mã số: nghiệp vụ VẪN bị chặn đúng.
            duplicate.IsSuccessStatusCode.Should().BeFalse();
            tooSoon.IsSuccessStatusCode.Should().BeFalse();
        }
    }
}
