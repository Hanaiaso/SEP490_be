using System.Net;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Handler giả cho HttpClient: ghi lại mọi request đã gửi và trả về response được cấu hình sẵn.
    /// Dùng cho sheet ExternalIntegrations (eSmsService / MakeWebhookService / AiGeneratorService) —
    /// test hợp đồng gọi ra ngoài mà không phát sinh network thật.
    /// </summary>
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public int CallCount => Requests.Count;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        /// <summary>Luôn trả về một status + body cố định.</summary>
        public static FakeHttpMessageHandler Returning(HttpStatusCode status, string body = "{}")
            => new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

        /// <summary>Luôn ném — mô phỏng timeout / mất kết nối.</summary>
        public static FakeHttpMessageHandler Throwing(Exception ex)
            => new(_ => throw ex);

        public HttpClient CreateClient(string baseAddress = "https://fake.test/")
            => new(this) { BaseAddress = new Uri(baseAddress) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }
}
