using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Hạ tầng dùng chung cho test unit của Controller.
    ///
    /// Vì sao test thẳng controller thay vì qua HTTP: các nhánh `catch` của controller
    /// (KeyNotFoundException -> 404, UnauthorizedAccessException -> 403,
    /// DbUpdateConcurrencyException -> 409, Exception -> 400) gần như không bao giờ chạy được
    /// qua đường HTTP thật vì phải dựng đúng trạng thái để service ném đúng loại exception.
    /// Mock service rồi `ThrowsAsync` là cách duy nhất chạm tới chúng — và đó là phần lớn
    /// số dòng chưa được phủ của tầng Controller.
    /// </summary>
    public static class ControllerTestHelper
    {
        /// <summary>
        /// Gắn ClaimsPrincipal vào controller để `User.FindFirst(ClaimTypes.NameIdentifier)`
        /// trong `GetUserId()` đọc được. Không gắn thì mọi action gọi GetUserId() sẽ ném
        /// UnauthorizedAccessException và ta chỉ test được đúng một nhánh.
        /// </summary>
        public static T WithUser<T>(this T controller, Guid? userId = null, string role = "Admin")
            where T : ControllerBase
        {
            var claims = new List<Claim> { new(ClaimTypes.Role, role) };
            if (userId.HasValue)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
            return controller;
        }

        /// <summary>Controller KHÔNG có claim user — dùng để ép nhánh token không hợp lệ.</summary>
        public static T WithAnonymousUser<T>(this T controller) where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };
            return controller;
        }

        /// <summary>
        /// Claim NameIdentifier CÓ mặt nhưng không parse được thành Guid.
        ///
        /// Vì sao cần riêng một helper: guard phổ biến trong dự án là
        /// `if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out var id))`. Khi test bằng
        /// <see cref="WithAnonymousUser"/> thì `IsNullOrEmpty` trả true và toán tử `||` **đoản mạch**
        /// — `Guid.TryParse` không bao giờ chạy, nên nhánh đó vĩnh viễn không được phủ dù line
        /// coverage đã 100%. Đây là cách duy nhất chạm tới nó.
        /// </summary>
        public static T WithMalformedUserId<T>(this T controller, string raw = "khong-phai-guid")
            where T : ControllerBase
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, raw),
                new(ClaimTypes.Role, "Customer")
            };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
            return controller;
        }

        /// <summary>
        /// Chỉ có claim `sub` (chuẩn JWT/OpenID), KHÔNG có `ClaimTypes.NameIdentifier`.
        ///
        /// `AuthController`, `UserProfileController` và `CustomerProfileController` đọc user bằng
        /// `User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub")`. Mọi test hiện có
        /// đều gắn NameIdentifier nên vế phải của `??` chưa bao giờ chạy — đây là đường mà token
        /// Google trả về đi qua, không phải nhánh chết.
        /// </summary>
        public static T WithSubClaimOnly<T>(this T controller, Guid userId, string role = "Customer")
            where T : ControllerBase
        {
            var claims = new List<Claim>
            {
                new("sub", userId.ToString()),
                new(ClaimTypes.Role, role)
            };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
            return controller;
        }

        /// <summary>
        /// Gắn thêm claim Email vào principal hiện có. Các controller ghi audit log đọc
        /// `User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty`; không gắn thì chỉ nhánh null
        /// được chạy.
        /// </summary>
        public static T WithEmailClaim<T>(this T controller, string email = "admin@viettien.vn")
            where T : ControllerBase
        {
            if (controller.ControllerContext?.HttpContext == null) controller.WithUser();
            var identity = (ClaimsIdentity)controller.ControllerContext.HttpContext.User.Identity!;
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
            return controller;
        }

        /// <summary>
        /// Gắn địa chỉ IP cho request. `DefaultHttpContext` để `RemoteIpAddress` null nên
        /// `HttpContext.Connection.RemoteIpAddress?.ToString()` chỉ chạy nhánh null.
        /// </summary>
        public static T WithRemoteIp<T>(this T controller, string ip = "203.0.113.10")
            where T : ControllerBase
        {
            if (controller.ControllerContext?.HttpContext == null) controller.WithUser();
            controller.ControllerContext.HttpContext.Connection.RemoteIpAddress =
                System.Net.IPAddress.Parse(ip);
            return controller;
        }

        /// <summary>Ép ModelState invalid để chạm nhánh `if (!ModelState.IsValid) return BadRequest(...)`.</summary>
        public static T WithInvalidModelState<T>(this T controller, string key = "field", string error = "loi")
            where T : ControllerBase
        {
            controller.ModelState.AddModelError(key, error);
            return controller;
        }

        /// <summary>Gắn header vào request (SePayController đọc x-sepay-token / Authorization / query).</summary>
        public static T WithHeader<T>(this T controller, string name, string value) where T : ControllerBase
        {
            if (controller.ControllerContext?.HttpContext == null) controller.WithUser();
            controller.ControllerContext.HttpContext.Request.Headers[name] = value;
            return controller;
        }

        /// <summary>Gắn query string vào request.</summary>
        public static T WithQuery<T>(this T controller, string name, string value) where T : ControllerBase
        {
            if (controller.ControllerContext?.HttpContext == null) controller.WithUser();
            controller.ControllerContext.HttpContext.Request.QueryString =
                new QueryString($"?{name}={Uri.EscapeDataString(value)}");
            return controller;
        }

        /// <summary>Lấy status code từ mọi loại IActionResult mà controller trong dự án này trả về.</summary>
        // ForbidResult phải đứng TRƯỚC StatusCodeResult? Không — ForbidResult không kế thừa
        // StatusCodeResult, nhưng OkResult/NotFoundResult/UnauthorizedResult thì CÓ, nên chỉ
        // cần một nhánh StatusCodeResult là đủ cho cả nhóm đó.
        public static int StatusOf(this IActionResult result) => result switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            ForbidResult => 403,
            StatusCodeResult s => s.StatusCode,
            _ => -1
        };

        /// <summary>
        /// Overload cho action khai báo `ActionResult&lt;T&gt;` (CartController, WarehouseShiftController...).
        /// Khi action `return Ok(x)` thì nằm ở `.Result`; khi `return x` trực tiếp thì nằm ở `.Value`
        /// và coi như 200.
        /// </summary>
        public static int StatusOf<T>(this ActionResult<T> result) =>
            result.Result is not null ? result.Result.StatusOf() : 200;
    }
}
