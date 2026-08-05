using FluentAssertions;
using VietTien.API.Data;
using VietTien.API.DTOs.Payment;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: ManualPaymentService — L1-MP-01..05. EF InMemory.
    /// Lệch spec (ghi Notes): khi thiếu bằng chứng/đối soát, code từ chối xác nhận nhưng KHÔNG tự hủy đơn
    /// (spec L1-MP-02 nói 'temp order cancelled').
    /// </summary>
    public class ManualPaymentServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly ManualPaymentService _sut;
        private readonly User _manager;
        private readonly CustomerProfile _profile;
        private readonly Product _p1;

        public ManualPaymentServiceTests()
        {
            _sut = new ManualPaymentService(_db, new FakeInventoryReservationService(_db));
            _manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(_manager);
            (_, _profile) = TestData.SeedCustomer(_db);
            _p1 = TestData.SeedProduct(_db);
        }

        private Order SeedSePayOrder(PaymentStatus payment, OrderStatus status, int qty = 2, int stock = 10)
        {
            var order = TestData.Order(_profile.Id, o =>
            {
                o.PaymentMethod = PaymentMethod.SePay;
                o.PaymentStatus = payment;
                o.OrderStatus = status;
                o.TotalAmount = 5_000_000m;
                o.FinalPayment = 5_000_000m;
            });
            _db.Orders.Add(order);
            _db.OrderItems.Add(TestData.OrderItem(order.Id, _p1.Id, qty));
            _db.SaveChanges();
            if (stock >= 0) TestData.SeedInventory(_db, _p1.Id, stock);
            return order;
        }

        private static ManualConfirmPaymentRequest ValidRequest(decimal amount = 5_000_000m, string txId = "TX-001") => new()
        {
            ExternalTransactionId = txId,
            ActualAmount = amount,
            EvidenceUrl = "https://cdn/evidence.png",
            TransferContent = "CK don hang"
        };

        // L1-MP-01 | State-Valid | Đơn SePay chưa Paid + bằng chứng + đúng số tiền + đủ tồn -> Paid + Confirmed, ghi người xác nhận
        [Fact]
        public async Task L1_MP_01_ManualConfirm_Valid_PaidAndAllocated()
        {
            var order = SeedSePayOrder(PaymentStatus.Pending, OrderStatus.PendingPayment);

            var res = await _sut.ManualConfirmAsync(order.Id, ValidRequest(), _manager.Id);

            res.PaymentStatus.Should().Be("Paid");
            res.AllocationStatus.Should().Be("ALLOCATED");
            res.OrderStatus.Should().Be("Confirmed");
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.PaymentStatus.Should().Be(PaymentStatus.Paid);
            updated.ManualConfirmedByUserId.Should().Be(_manager.Id);
            _db.PaymentTransactions.Should().ContainSingle(t => t.TransactionId == "TX-001" && t.IsManualConfirmed);
        }

        // L1-MP-02 | State-Invalid | Xác nhận KHÔNG kèm bằng chứng đối soát -> từ chối, thanh toán không đổi
        // (Lệch spec: code không hủy đơn, chỉ từ chối xác nhận.)
        [Fact]
        public async Task L1_MP_02_ManualConfirm_NoEvidence_Rejected()
        {
            var order = SeedSePayOrder(PaymentStatus.Pending, OrderStatus.PendingPayment);
            var request = ValidRequest() with { EvidenceUrl = "" };

            var act = () => _sut.ManualConfirmAsync(order.Id, request, _manager.Id);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("MANUAL_CONFIRM_EVIDENCE_REQUIRED*");
            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Pending);
            _db.PaymentTransactions.Count().Should().Be(0);
        }

        // L1-MP-03 | State-Invalid | Đơn đã Paid, gửi mã giao dịch khác -> conflict, không tạo ledger entry trùng
        [Fact]
        public async Task L1_MP_03_ManualConfirm_AlreadyPaid_Conflict()
        {
            var order = SeedSePayOrder(PaymentStatus.Pending, OrderStatus.PendingPayment);
            await _sut.ManualConfirmAsync(order.Id, ValidRequest(txId: "TX-001"), _manager.Id); // Paid lần 1

            var act = () => _sut.ManualConfirmAsync(order.Id, ValidRequest(txId: "TX-KHAC"), _manager.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("PAYMENT_ALREADY_CONFIRMED*");
            _db.PaymentTransactions.Count().Should().Be(1); // không có bản ghi thứ 2
        }

        // L1-MP-04 | EP-Valid | Retry allocation khi đơn PaidReviewRequired và tồn kho đã đủ -> Confirmed, exception RESOLVED
        [Fact]
        public async Task L1_MP_04_RetryAllocation_NowSufficientStock_Allocated()
        {
            // Đơn Paid nhưng thiếu tồn -> PaidReviewRequired kèm PaymentException OPEN
            var order = SeedSePayOrder(PaymentStatus.Pending, OrderStatus.PendingPayment, qty: 5, stock: 0);
            await _sut.ManualConfirmAsync(order.Id, ValidRequest(), _manager.Id);
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.PaidReviewRequired);
            // Bổ sung tồn kho
            _db.Inventories.First(i => i.ProductId == _p1.Id).OnHandQuantity = 20;
            _db.SaveChanges();

            var res = await _sut.RetryAllocationAsync(order.Id, _manager.Id, "retry sau nhập kho");

            res.AllocationStatus.Should().Be("ALLOCATED");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Confirmed);
            _db.PaymentExceptions.Single().Status.Should().Be("RESOLVED");
        }

        // L1-MP-05 | EP-Valid | GetExceptions chỉ trả các ngoại lệ CHƯA xử lý (Pending quá 30' hoặc PaidReviewRequired)
        [Fact]
        public async Task L1_MP_05_GetExceptions_OnlyUnresolvedReturned()
        {
            // TH1: Pending quá 30 phút
            var pendingOld = SeedSePayOrder(PaymentStatus.Pending, OrderStatus.PendingPayment);
            _db.Orders.Single(o => o.Id == pendingOld.Id).CreatedAt = DateTime.UtcNow.AddMinutes(-45);
            // TH2: Paid nhưng chưa phân bổ được
            SeedSePayOrder(PaymentStatus.Paid, OrderStatus.PaidReviewRequired);
            // Đã xử lý xong: Paid + Confirmed -> KHÔNG được trả về
            SeedSePayOrder(PaymentStatus.Paid, OrderStatus.Confirmed);
            _db.SaveChanges();

            var list = await _sut.GetExceptionsAsync();

            list.Should().HaveCount(2);
            list.Should().NotContain(x => x.OrderStatus == "Confirmed");
        }
    }
}
