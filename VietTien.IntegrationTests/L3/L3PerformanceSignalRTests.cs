using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-Performance</c> — case <b>L3-PERF-04</b> (NFR-P04).
    ///
    /// Vì sao case này KHÔNG nằm trong file .jmx như 8 case PERF còn lại: JMeter không nói được
    /// WebSocket/SignalR nếu không cài plugin ngoài (WebSocket Samplers). Kể cả có plugin thì vẫn phải
    /// tự implement handshake + negotiate + framing của giao thức SignalR — dễ đo sai hơn là đo đúng.
    /// Dùng thẳng <c>Microsoft.AspNetCore.SignalR.Client</c> (đã có sẵn trong csproj) là cách đo trung
    /// thực nhất: cùng thư viện client mà frontend đang dùng, đi qua đúng pipeline hub thật.
    ///
    /// Kịch bản đúng như workbook: <b>2 client kết nối · 100 tin · độ trễ end-to-end &lt; 2s</b>.
    ///   - Client A = khách hàng sở hữu báo giá, Client B = Sales Staff được gán.
    ///     (ChatHub.SendMessage -> QuotationService.SendMessageAsync chỉ cho 2 danh tính này gửi.)
    ///   - Đo end-to-end THẬT: bấm giờ từ lúc A gọi <c>SendMessage</c> tới lúc B nhận được
    ///     <c>ReceiveMessage</c> đúng tin đó — tức đã bao gồm cả ghi DB và broadcast theo group.
    ///
    /// Số liệu đo được ghi ra <c>tests/reports/l3_perf_signalr.json</c> để tools/l3_report.py đưa vào
    /// bảng kết quả, thay vì chép tay con số.
    /// </summary>
    public class L3PerformanceSignalRTests : L3TestBase
    {
        public L3PerformanceSignalRTests(L3SqlFixture factory) : base(factory) { }

        /// <summary>Số tin nhắn theo workbook.</summary>
        private const int MessageCount = 100;

        /// <summary>Ngưỡng NFR-P04: độ trễ end-to-end &lt; 2 giây.</summary>
        private const int ThresholdMs = 2000;

        [Fact]
        public async Task L3_PERF_04_ChatHub_TwoClients_100Messages_EndToEndLatencyUnder2s()
        {
            // ── Arrange: báo giá có khách sở hữu + Sales được gán ──────────────────────────────
            var (customerClient, customerUser) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(customerUser.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(120_000_000m, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 120_000_000m));

            var created = await customerClient.PostAsJsonAsync("/api/Quotation/from-cart",
                new { GeneralNote = "Bao gia do hieu nang SignalR" });
            created.IsSuccessStatusCode.Should().BeTrue(
                $"phải tạo được báo giá ({await ReadMessageAsync(created)})");
            var quotationId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

            var salesClient = await ClientForSeededAsync(L3Seed.SalesStaffId);
            await AssignQuotationToSalesAsync(quotationId);

            var customerToken = TokenFor(customerUser.Id);
            var salesToken = TokenFor(L3Seed.SalesStaffId);

            // ── Arrange: 2 kết nối SignalR thật qua TestServer ─────────────────────────────────
            await using var sender = BuildConnection(customerToken);    // client A — khách hàng
            await using var receiver = BuildConnection(salesToken);     // client B — Sales Staff

            // Bắt tin đến ở phía B, khớp theo số thứ tự nhúng trong nội dung tin.
            var pending = new ConcurrentDictionary<int, TaskCompletionSource<long>>();
            receiver.On<JsonElement>("ReceiveMessage", dto =>
            {
                var text = dto.GetProperty("messageText").GetString() ?? string.Empty;
                var seq = ParseSequence(text);
                if (seq >= 0 && pending.TryGetValue(seq, out var tcs))
                    tcs.TrySetResult(Stopwatch.GetTimestamp());
            });

            await sender.StartAsync();
            await receiver.StartAsync();
            sender.State.Should().Be(HubConnectionState.Connected, "client A phải kết nối được hub");
            receiver.State.Should().Be(HubConnectionState.Connected, "client B phải kết nối được hub");

            await sender.InvokeAsync("JoinQuotationChat", quotationId.ToString());
            await receiver.InvokeAsync("JoinQuotationChat", quotationId.ToString());

            // Tin khởi động: nuốt chi phí JIT + mở connection pool DB, không tính vào số liệu.
            await SendAndWaitAsync(sender, pending, quotationId, seq: -0);

            // ── Act: gửi tuần tự 100 tin, đo từng tin ─────────────────────────────────────────
            var latencies = new List<double>(MessageCount);
            for (var i = 1; i <= MessageCount; i++)
            {
                latencies.Add(await SendAndWaitAsync(sender, pending, quotationId, i));
            }

            // ── Assert ────────────────────────────────────────────────────────────────────────
            latencies.Should().HaveCount(MessageCount, "cả 100 tin phải tới được client B");

            var sorted = latencies.OrderBy(x => x).ToList();
            var p95 = sorted[(int)Math.Round(0.95 * (sorted.Count - 1))];
            var max = sorted[^1];
            var avg = sorted.Average();

            WriteStats(sorted.Count, avg, p95, max);

            max.Should().BeLessThan(ThresholdMs,
                $"NFR-P04 đòi độ trễ end-to-end < {ThresholdMs}ms; đo được avg={avg:F1}ms, " +
                $"p95={p95:F1}ms, max={max:F1}ms trên {sorted.Count} tin");

            // Bất biến kèm theo: mọi tin đều được ghi xuống DB, không chỉ broadcast rồi thôi.
            (await QueryAsync(db => db.ChatMessages.CountAsync(m => m.QuotationId == quotationId)))
                .Should().Be(MessageCount + 1, "100 tin đo + 1 tin khởi động đều phải được lưu");
        }

        // ── Helper ────────────────────────────────────────────────────────────────────────────

        private string TokenFor(Guid userId)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VietTien.API.Data.ApplicationDbContext>();
            var user = db.Users.Single(u => u.Id == userId);
            return scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(user);
        }

        /// <summary>
        /// Nối HubConnection vào TestServer của WebApplicationFactory.
        ///
        /// Hai điểm bắt buộc:
        ///  - JWT phải đi qua query <c>?access_token=</c>: WebSocket không gửi được header Authorization,
        ///    nên Program.cs:142 đọc token từ query cho mọi path bắt đầu bằng /hubs.
        ///  - TestServer không mở cổng TCP thật, nên phải trỏ SignalR vào handler in-memory của nó:
        ///    HttpMessageHandlerFactory cho bước negotiate, WebSocketFactory cho kênh WebSocket.
        /// </summary>
        private HubConnection BuildConnection(string accessToken)
        {
            var uri = new Uri(Factory.Server.BaseAddress, $"hubs/chat?access_token={accessToken}");

            return new HubConnectionBuilder()
                .WithUrl(uri, options =>
                {
                    options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        var wsClient = Factory.Server.CreateWebSocketClient();
                        // TestServer nhận http/https; SignalR đưa vào ws/wss -> đổi lại scheme.
                        var httpUri = new UriBuilder(context.Uri)
                        {
                            Scheme = context.Uri.Scheme == "wss" ? "https" : "http"
                        }.Uri;
                        return await wsClient.ConnectAsync(httpUri, cancellationToken);
                    };
                })
                .Build();
        }

        /// <summary>Gửi 1 tin và chờ đúng tin đó tới client B; trả về độ trễ (ms).</summary>
        private static async Task<double> SendAndWaitAsync(
            HubConnection sender,
            ConcurrentDictionary<int, TaskCompletionSource<long>> pending,
            Guid quotationId,
            int seq)
        {
            var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[seq] = tcs;

            var start = Stopwatch.GetTimestamp();
            await sender.InvokeAsync("SendMessage", quotationId.ToString(), FormatMessage(seq));

            // Timeout rộng hơn ngưỡng nhiều lần: nếu tin không bao giờ tới thì phải fail vì MẤT TIN,
            // với thông điệp rõ ràng, chứ không treo cho tới khi cả suite bị kill.
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (finished != tcs.Task)
                throw new TimeoutException($"Tin #{seq} không tới được client B sau 30s (mất tin).");

            var elapsed = (await tcs.Task) - start;
            pending.TryRemove(seq, out _);
            return elapsed * 1000.0 / Stopwatch.Frequency;
        }

        private const string Marker = "L3-PERF-04 #";

        private static string FormatMessage(int seq) => $"{Marker}{seq}";

        private static int ParseSequence(string text) =>
            text.StartsWith(Marker, StringComparison.Ordinal)
             && int.TryParse(text.AsSpan(Marker.Length), out var seq) ? seq : -1;

        /// <summary>
        /// Ghi số liệu ra tests/reports/l3_perf_signalr.json để tools/l3_report.py đọc — cùng cách
        /// 8 case PERF kia lấy số từ .jtl, không chép tay con số vào báo cáo.
        /// </summary>
        private static void WriteStats(int samples, double avgMs, double p95Ms, double maxMs)
        {
            var root = FindRepoRoot();
            if (root == null) return;

            var dir = Path.Combine(root, "tests", "reports");
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new
            {
                testId = "L3-PERF-04",
                samples,
                avgMs = Math.Round(avgMs, 1),
                p95Ms = Math.Round(p95Ms, 1),
                maxMs = Math.Round(maxMs, 1),
                thresholdMs = ThresholdMs,
                clients = 2,
                transport = "WebSocket (SignalR client, qua TestServer in-memory)",
                runAtUtc = DateTime.UtcNow.ToString("O"),
            }, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(Path.Combine(dir, "l3_perf_signalr.json"), json);
        }

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VietTien.sln")))
                dir = dir.Parent;
            return dir?.FullName;
        }
    }
}
