using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Exceptions;
using VietTien.API.Infrastructure.Security;

namespace VietTien.API.Infrastructure.Middleware
{
    /// <summary>
    /// Lưới an toàn cuối cùng cho mọi exception chưa được controller tự bắt.
    /// Không thay thế các try/catch đã có (chúng vẫn chạy như cũ) — chỉ xử lý phần lọt lưới,
    /// đồng thời chuẩn hoá status code theo loại exception thay vì luôn trả 500 trống hoặc 400 tràn lan.
    ///
    /// Quy ước phân loại dựa trên convention đã có sẵn trong code: `throw new Exception("...")`
    /// (đúng kiểu gốc, không phải subclass) được dùng khắp nơi cho lỗi nghiệp vụ mong đợi (400).
    /// Các subclass framework/EF không được liệt kê rõ ràng (NullReferenceException, SqlException,
    /// DbUpdateException...) được coi là lỗi hệ thống ngoài dự kiến -> 500, không lộ chi tiết.
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Response đã bắt đầu ghi (vd streaming) -> không thể đổi status code nữa, chỉ log rồi rethrow.
                _logger.LogError(ex, "Exception xảy ra sau khi response đã bắt đầu ghi.");
                throw ex;
            }

            var (statusCode, body) = MapException(ex);

            if (statusCode == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex, "Lỗi hệ thống ngoài dự kiến tại {Method} {Path}",
                    context.Request.Method, context.Request.Path);
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }

        private static (int StatusCode, object Body) MapException(Exception ex)
        {
            switch (ex)
            {
                case ProfileIncompleteException profileEx:
                    // Giữ đúng hành vi hiện tại của CartController cho exception này.
                    return (StatusCodes.Status409Conflict, new { code = "PROFILE_INCOMPLETE", message = profileEx.Message });

                case KeyNotFoundException notFoundEx:
                    return (StatusCodes.Status404NotFound, new { message = Redact(notFoundEx) });

                case UnauthorizedAccessException forbiddenEx:
                    return (StatusCodes.Status403Forbidden, new { message = Redact(forbiddenEx) });

                case InvalidOperationException conflictEx:
                    return (StatusCodes.Status409Conflict, new { message = Redact(conflictEx) });

                case DbUpdateConcurrencyException:
                    return (StatusCodes.Status409Conflict, new
                    {
                        message = "Dữ liệu đã được cập nhật bởi người khác, vui lòng tải lại và thử lại."
                    });

                case ArgumentException argEx:
                    return (StatusCodes.Status400BadRequest, new { message = Redact(argEx) });

                case FormatException formatEx:
                    return (StatusCodes.Status400BadRequest, new { message = Redact(formatEx) });

                // Đúng kiểu gốc System.Exception (không phải subclass) -> quy ước nghiệp vụ hiện có
                // (throw new Exception("Không đủ hàng...")) -> giữ nguyên hành vi UX hiện tại: 400.
                case Exception plainEx when plainEx.GetType() == typeof(Exception):
                    return (StatusCodes.Status400BadRequest, new { message = Redact(plainEx) });

                // Mọi subclass khác chưa được liệt kê rõ ràng (NullReferenceException, SqlException,
                // DbUpdateException, TaskCanceledException...) -> lỗi hệ thống ngoài dự kiến.
                default:
                    return (StatusCodes.Status500InternalServerError, new
                    {
                        message = "Đã có lỗi xảy ra, vui lòng thử lại sau."
                    });
            }
        }

        private static string Redact(Exception ex) =>
            SensitiveDataRedactor.RedactPlainText(ex.Message, ex.GetType().Name);
    }
}
