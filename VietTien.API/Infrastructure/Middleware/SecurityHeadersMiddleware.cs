namespace VietTien.API.Infrastructure.Middleware
{
    /// <summary>
    /// L3-SEC-15: gắn các security header chuẩn (HSTS, X-Content-Type-Options, X-Frame-Options) vào
    /// mọi response — trước đây hoàn toàn không có, ZAP/Newman baseline scan báo thiếu cả 3.
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                if (context.Request.IsHttps)
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }
                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
