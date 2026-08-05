using FluentAssertions;
using VietTien.API.Models;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: OrderService — block Đối soát webhook SePay (L1-ORD-74..76, doc v2.3)
    /// và Công thức VAT (L1-ORD-77, 77b — đề xuất bổ sung cho v2.4).
    /// Dùng chung fixture với OrderServiceTests (partial class).
    ///
    /// ⚠ Đánh số bám THEO DOC v2.3: ORD-74 = trả thiếu · ORD-75 = trả thừa · ORD-76 = khớp chính xác.
    ///   (Bản 01/08 tôi tự đặt ORD-74 = VAT, ORD-75 = trả thiếu — đã đổi lại cho khớp doc.)
    /// </summary>
    public partial class OrderServiceTests
    {
        /// <summary>Đơn SePay đang chờ thanh toán, tổng phải trả 5.000.000đ.</summary>
        private Order SeedPendingSePayOrder(decimal finalPayment = 5_000_000m)
            => SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.SePay;
                o.PaymentStatus = PaymentStatus.Pending;
                o.OrderStatus = OrderStatus.PendingPayment;
                o.FinalPayment = finalPayment;
            });

        // ── Block: Đối soát webhook SePay — [Theory] 3 mốc −1 / khớp / +1 ────

        // L1-ORD-74 | EP-Invalid | transferAmount = FinalPayment − 1 -> KHÔNG ghi nhận Paid,
        // và phải để lại dấu vết cho nhân viên đối soát.
        //
        // 🔴 SPEC GAP: OrderService — `if (payload.transferAmount < order.FinalPayment) return;`
        // Hàm return IM LẶNG: không log, không tạo PaymentException, không báo ai. Tiền đã vào tài
        // khoản công ty nhưng đơn vẫn Unpaid và KHÔNG AI BIẾT có giao dịch treo.
        // Bằng chứng đây là thiếu sót chứ không phải thiết kế: nhánh "trả tiền sau khi đơn đã huỷ"
        // ngay bên dưới CÓ tạo PaymentException mã PAID_AFTER_CANCELLATION.
        [Fact]
        public async Task L1_ORD_74_Webhook_Underpayment_RaisesExceptionNotSilentReturn()
        {
            var order = SeedPendingSePayOrder();

            await _sut.ProcessSePayWebhookAsync(
                WebhookPayload(order.OrderCode, 4_999_999m), TestConfig.SePayApiToken);

            _db.ChangeTracker.Clear();
            var saved = _db.Orders.Single(o => o.Id == order.Id);

            // Phần ĐÚNG và phải giữ
            saved.PaymentStatus.Should().Be(PaymentStatus.Pending, "trả thiếu thì chưa thể tính là đã thanh toán");
            _db.PaymentTransactions.Should().BeEmpty("không tạo giao dịch cho khoản trả thiếu");

            // Phần ĐANG THIẾU: tiền đã vào tài khoản, phải có người biết để xử lý
            _db.PaymentExceptions.Should().NotBeEmpty(
                "khoản chuyển thiếu phải sinh ngoại lệ thanh toán để nhân viên đối soát, không được return im lặng");
        }

        // L1-ORD-75 | EP-Invalid | transferAmount = FinalPayment + 1 -> phải TỪ CHỐI và đưa vào đối soát
        //
        // 🔴 SPEC GAP: SRS BV-01 yêu cầu đối soát KHỚP CHÍNH XÁC, nhưng code dùng
        // `if (transferAmount < FinalPayment) return;` nên mọi khoản trả THỪA vẫn được set Paid.
        // Hệ quả: chênh lệch thừa không được ghi nhận ở đâu, đối soát ngân hàng sẽ lệch.
        // (Doc L1-ORD-16 đã ghi nhận việc dùng '>=' — case này chốt kỳ vọng đúng theo spec.)
        [Fact]
        public async Task L1_ORD_75_Webhook_Overpayment_IsRejectedForReconciliation()
        {
            var order = SeedPendingSePayOrder();

            await _sut.ProcessSePayWebhookAsync(
                WebhookPayload(order.OrderCode, 5_000_001m), TestConfig.SePayApiToken);

            _db.ChangeTracker.Clear();
            var saved = _db.Orders.Single(o => o.Id == order.Id);

            saved.PaymentStatus.Should().Be(PaymentStatus.Pending,
                "SRS BV-01: đối soát phải khớp CHÍNH XÁC — trả thừa không được tự động ghi nhận Paid");
            _db.PaymentExceptions.Should().NotBeEmpty(
                "khoản chênh lệch thừa phải vào danh sách đối soát để hoàn/điều chỉnh");
        }

        // L1-ORD-76 | EP-Valid | transferAmount = FinalPayment (khớp chính xác) -> Paid, đúng 1 giao dịch
        [Fact]
        public async Task L1_ORD_76_Webhook_ExactAmount_MarksPaid()
        {
            var order = SeedPendingSePayOrder();

            await _sut.ProcessSePayWebhookAsync(
                WebhookPayload(order.OrderCode, 5_000_000m), TestConfig.SePayApiToken);

            _db.ChangeTracker.Clear();
            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Paid);
            _db.PaymentTransactions.Should().ContainSingle()
                .Which.Amount.Should().Be(5_000_000m);
        }

        // ── Block: ⊕ đề xuất v2.4 — Công thức VAT ────────────────────────────

        // L1-ORD-77 | EP-Valid | VAT 10% tính SAU chiết khấu (không phải trên giá gốc),
        // FinalPayment = (gốc − chiết khấu) + VAT. Chốt con số chính xác thay vì chỉ "> 0".
        // ⚠ Chưa có ID trong doc v2.3 — đề xuất cấp mã ORD-77 ở v2.4.
        [Fact]
        public async Task L1_ORD_77_Vat_IsTenPercentAfterDiscount_ExactAmounts()
        {
            _profile.TaxCode = "0312345678"; // có MST -> chịu VAT
            _db.SaveChanges();
            SeedCartWithTotal(10_000_000m);  // rơi vào bậc chiết khấu 5% (>= 10 triệu)

            var preview = await _sut.GetCheckoutSummaryAsync(_customer.Id);

            preview.TotalAmount.Should().Be(10_000_000m);
            preview.DiscountAmount.Should().Be(500_000m, "5% của 10 triệu");
            preview.VatAmount.Should().Be(950_000m, "VAT 10% tính trên 9.500.000 (SAU chiết khấu), KHÔNG phải trên 10 triệu");
            preview.FinalPayment.Should().Be(10_450_000m);

            // VAT tính trên giá gốc sẽ ra 1.000.000 — khẳng định KHÔNG phải con số đó
            preview.VatAmount.Should().NotBe(1_000_000m);
        }

        // L1-ORD-77b | BVA | Tiền VND KHÔNG có đơn vị nhỏ hơn 1 đồng -> mọi khoản tiền phải là số nguyên
        //
        // 🔴 SPEC GAP: OrderService tính `vatAmount = totalAfterDiscount * 0.10m` và
        // `discountAmount = totalAmount * percent` mà KHÔNG hề làm tròn. Với giỏ hàng có tổng lẻ,
        // hệ thống sinh ra số tiền lẻ tới hàng phần nghìn đồng rồi lưu thẳng vào Order và đẩy sang SePay.
        // Test ĐỎ cho tới khi bổ sung Math.Round(..., 0) cho tiền VND.
        [Fact]
        public async Task L1_ORD_77b_MoneyAmounts_AreWholeDong()
        {
            _profile.TaxCode = "0312345678";
            _db.SaveChanges();
            SeedCartWithTotal(10_000_001m); // tổng lẻ -> 5% = 500.000,05đ

            var preview = await _sut.GetCheckoutSummaryAsync(_customer.Id);

            preview.DiscountAmount.Should().Be(decimal.Truncate(preview.DiscountAmount),
                "tiền chiết khấu phải là số nguyên đồng");
            preview.VatAmount.Should().Be(decimal.Truncate(preview.VatAmount),
                "tiền VAT phải là số nguyên đồng");
            preview.FinalPayment.Should().Be(decimal.Truncate(preview.FinalPayment),
                "số tiền khách phải trả không thể có phần lẻ nhỏ hơn 1 đồng");
        }
    }
}
