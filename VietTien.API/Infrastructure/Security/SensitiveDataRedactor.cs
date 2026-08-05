using System.Text.Json;
using System.Text.Json.Nodes;

namespace VietTien.API.Infrastructure.Security
{
    // Lưới an toàn dùng chung cho AuditLog, JobRun.ErrorMessage, WebhookLog.LastError:
    // tuyệt đối không ghi password/OTP/token/secret/apikey/pin vào bất kỳ log nào (DoD mục 8.4).
    public static class SensitiveDataRedactor
    {
        private static readonly string[] SensitiveKeywords =
        {
            "password", "otp", "token", "secret", "apikey", "pin"
        };

        // PII: che một phần (giữ vài ký tự đầu/cuối để vẫn tra cứu/đối chiếu được), không redact toàn bộ.
        private static readonly string[] PiiKeywords =
        {
            "phone", "taxcode", "mst"
        };

        public static bool IsSensitive(string fieldName)
        {
            foreach (var keyword in SensitiveKeywords)
            {
                if (fieldName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool IsPii(string fieldName)
        {
            foreach (var keyword in PiiKeywords)
            {
                if (fieldName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Giữ 4 ký tự đầu + 3 ký tự cuối, che phần giữa. VD: "0912345678" -> "0912***678".
        public static string MaskPiiValue(string value)
        {
            const int visibleStart = 4;
            const int visibleEnd = 3;
            if (string.IsNullOrEmpty(value) || value.Length <= visibleStart + visibleEnd)
                return new string('*', value?.Length ?? 0);

            var maskedLength = value.Length - visibleStart - visibleEnd;
            return value[..visibleStart] + new string('*', maskedLength) + value[^visibleEnd..];
        }

        // Dùng cho object/DTO trước khi lưu vào AuditLog.BeforeJson/AfterJson.
        public static string? RedactToJson(object? value)
        {
            if (value is null) return null;

            var node = JsonSerializer.SerializeToNode(value, new JsonSerializerOptions
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            });

            RedactNode(node);
            return node?.ToJsonString();
        }

        // Dùng cho thông điệp lỗi dạng text tự do (Exception.Message, stack trace...).
        // Không cố gắng che từng phần (dễ sót) — nếu phát hiện từ khóa nhạy cảm thì thay
        // toàn bộ message bằng placeholder chung, chỉ giữ lại tên loại exception.
        public static string RedactPlainText(string? message, string? exceptionTypeName = null)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            if (IsSensitive(message))
            {
                var suffix = string.IsNullOrEmpty(exceptionTypeName) ? string.Empty : $" ({exceptionTypeName})";
                return $"Đã xảy ra lỗi, chi tiết bị ẩn do có thể chứa dữ liệu nhạy cảm{suffix}.";
            }

            const int maxLength = 2000;
            return message.Length > maxLength ? message[..maxLength] : message;
        }

        private static void RedactNode(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(kv => kv.Key).ToList())
                    {
                        if (IsSensitive(key))
                        {
                            obj[key] = "***REDACTED***";
                        }
                        else if (IsPii(key) && obj[key] is JsonValue piiValue && piiValue.TryGetValue<string>(out var piiText))
                        {
                            obj[key] = MaskPiiValue(piiText);
                        }
                        else
                        {
                            RedactNode(obj[key]);
                        }
                    }
                    break;

                case JsonArray arr:
                    foreach (var item in arr)
                        RedactNode(item);
                    break;
            }
        }
    }
}
