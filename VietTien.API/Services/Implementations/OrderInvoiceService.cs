using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    // Sinh Hóa đơn PDF CHÍNH THỨC phía server — thay cho bản PDF tự tạo ở trình duyệt trước đây
    // (exportPdf.js, ghi rõ "Không phải hóa đơn VAT nhà nước"). Bố cục mirror lại đúng nội dung của
    // exportPdf.js (header công ty, mã đơn, thông tin khách, bảng dòng hàng, tổng tiền/chiết khấu/
    // VAT/thành tiền, số tiền bằng chữ, thông tin chuyển khoản, chữ ký) nhưng đổi tiêu đề thành
    // "HÓA ĐƠN" và thêm khối số hóa đơn đỏ/ngày xuất khi đã có (RedInvoiceStatus == Issued).
    public class OrderInvoiceService : IOrderInvoiceService
    {
        private const string CompanyName = "CÔNG TY TNHH VIỆT TIẾN";
        private const string CompanyAddress = "Số 5, Đường Lê Lợi, TP. Thái Bình";
        private const string CompanyPhone = "0227 3 123 456";
        private const string CompanyTaxCode = "1000123456";
        private const string BankInfo = "Ngân hàng TP Bank | STK: 71111810204 | Chủ TK: CONG TY VIET TIEN";

        public byte[] GenerateInvoicePdf(Order order)
        {
            var subtotal = order.TotalAmount;
            var discount = order.DiscountAmount;
            var vat = order.VatAmount;
            var total = order.FinalPayment;
            var discountRate = subtotal > 0 && discount > 0 ? discount / subtotal : 0m;
            var isRedInvoiceIssued = order.RedInvoiceStatus == RedInvoiceStatus.Issued || order.RedInvoiceStatus == RedInvoiceStatus.SentToCustomer;

            var customerName = order.CustomerProfile?.User?.FullName ?? order.CustomerProfile?.CompanyName ?? "Khách hàng";
            var customerPhone = order.CustomerProfile?.User?.PhoneNumber ?? order.CustomerProfile?.CompanyPhone;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);

                        // Header: công ty + số/ngày
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(CompanyName).Bold().FontSize(11).FontColor("#1e3a5f");
                                c.Item().Text($"Địa chỉ: {CompanyAddress}");
                                c.Item().Text($"Tel: {CompanyPhone} | MST: {CompanyTaxCode}");
                            });
                            row.ConstantItem(90).Column(c =>
                            {
                                c.Item().AlignRight().Text($"Số: {order.OrderCode}").Bold().FontColor("#1e3a5f");
                                c.Item().AlignRight().Text(order.CreatedAt.ToString("dd/MM/yyyy"));
                            });
                        });

                        col.Item().LineHorizontal(1.5f).LineColor("#1e3a5f");

                        // Title
                        col.Item().AlignCenter().Text("HÓA ĐƠN").Bold().FontSize(16).LetterSpacing(0.05f);
                        if (isRedInvoiceIssued)
                        {
                            col.Item().AlignCenter().Text(
                                $"Hóa đơn đỏ số {order.RedInvoiceNumber} — ngày {order.RedInvoiceIssuedAt:dd/MM/yyyy}")
                                .FontSize(9).FontColor("#15803d").Bold();
                        }

                        // Customer info
                        col.Item().Column(c =>
                        {
                            c.Item().Text($"Tên khách hàng: {customerName}").Bold();
                            c.Item().Text($"Địa chỉ giao hàng: {order.ShippingAddress}");
                            if (!string.IsNullOrEmpty(customerPhone))
                                c.Item().Text($"Số điện thoại: {customerPhone}");
                        });

                        // Items table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(4);
                                cd.RelativeColumn(1);
                                cd.RelativeColumn(2);
                                cd.RelativeColumn(2.5f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Border(1).Padding(3).Text("Tên sản phẩm").Bold();
                                header.Cell().Border(1).Padding(3).AlignCenter().Text("SL").Bold();
                                header.Cell().Border(1).Padding(3).AlignRight().Text("Đơn giá").Bold();
                                header.Cell().Border(1).Padding(3).AlignRight().Text("Thành tiền").Bold();
                            });

                            foreach (var item in order.OrderItems)
                            {
                                table.Cell().Border(1).Padding(3).Text(item.Product?.Name ?? "(Sản phẩm đã bị xóa)");
                                table.Cell().Border(1).Padding(3).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Border(1).Padding(3).AlignRight().Text(FormatPrice(item.PriceSnapshot));
                                table.Cell().Border(1).Padding(3).AlignRight().Text(FormatPrice(item.PriceSnapshot * item.Quantity));
                            }

                            table.Cell().ColumnSpan(3).Border(1).Padding(3).AlignCenter().Text("Cộng").Bold();
                            table.Cell().Border(1).Padding(3).AlignRight().Text(FormatPrice(subtotal)).Bold().FontColor("#dc2626");

                            if (discount > 0)
                            {
                                table.Cell().ColumnSpan(3).Border(1).Padding(3).AlignRight().Text($"Chiết khấu ({Math.Round(discountRate * 100)}%)");
                                table.Cell().Border(1).Padding(3).AlignRight().Text($"-{FormatPrice(discount)}");
                            }

                            if (vat > 0)
                            {
                                table.Cell().ColumnSpan(3).Border(1).Padding(3).AlignRight().Text("Thuế VAT (10%)");
                                table.Cell().Border(1).Padding(3).AlignRight().Text($"+{FormatPrice(vat)}");
                            }

                            table.Cell().ColumnSpan(3).Border(1).Padding(4).Background("#eff6ff").AlignCenter().Text("Tổng thanh toán").Bold();
                            table.Cell().Border(1).Padding(4).Background("#eff6ff").AlignRight().Text(FormatPrice(total)).Bold().FontSize(11).FontColor("#b91c1c");
                        });

                        col.Item().Text(text =>
                        {
                            text.Span("Thành tiền viết thành chữ: ");
                            text.Span(NumberToVietnameseWords(total)).Bold().Italic();
                        });

                        col.Item().Background("#f8fafc").Padding(6).Column(c =>
                        {
                            c.Item().Text("Thông tin tài khoản công ty:").Bold().FontColor("#1e3a5f");
                            c.Item().Text(BankInfo);
                        });

                        col.Item().AlignRight().Text($"Ngày {order.CreatedAt:dd} tháng {order.CreatedAt:MM} năm {order.CreatedAt:yyyy}").Italic();

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().Text("Người nhận hàng").Bold();
                                c.Item().Text("(Ký, ghi rõ họ tên)").FontSize(7).FontColor(Colors.Grey.Medium);
                                c.Item().Height(30);
                            });
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().Text("Người bán hàng").Bold();
                                c.Item().Text("(Ký, ghi rõ họ tên)").FontSize(7).FontColor(Colors.Grey.Medium);
                                c.Item().Height(30);
                            });
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().Text("Người giao hàng").Bold();
                                c.Item().Text("(Ký, ghi rõ họ tên)").FontSize(7).FontColor(Colors.Grey.Medium);
                                c.Item().Height(30);
                            });
                        });
                    });

                    page.Footer().AlignCenter().Text("Hóa đơn được xuất bởi Hệ thống Quản lý Việt Tiến | viettien.store")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });

            return document.GeneratePdf();
        }

        private static string FormatPrice(decimal n) => n.ToString("N0", new System.Globalization.CultureInfo("vi-VN"));

        // Port từ frontend/src/utils/exportPdf.js numberToVietnameseWords — giữ đúng logic đọc số
        // tiền bằng chữ tiếng Việt để 2 bên nhất quán.
        private static readonly string[] Units = { "", " nghìn", " triệu", " tỷ", " nghìn tỷ", " triệu tỷ" };
        private static readonly string[] Digits = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };

        private static string ReadGroup3(int n, bool showZeroHundred)
        {
            var hundred = n / 100;
            var ten = (n % 100) / 10;
            var unit = n % 10;
            var res = "";
            if (hundred > 0 || showZeroHundred) res += Digits[hundred] + " trăm ";
            if (ten > 0)
            {
                res += ten == 1 ? "mười " : Digits[ten] + " mươi ";
            }
            else if (hundred > 0 && unit > 0)
            {
                res += "lẻ ";
            }
            if (unit > 0)
            {
                if (unit == 1 && ten > 1) res += "mốt";
                else if (unit == 5 && ten > 0) res += "lăm";
                else res += Digits[unit];
            }
            return res.Trim();
        }

        private static string NumberToVietnameseWords(decimal amount)
        {
            if (amount <= 0) return "Không đồng";

            var strAmount = Math.Floor(amount).ToString("F0");
            var groups = new List<string>();
            while (strAmount.Length > 0)
            {
                var start = Math.Max(0, strAmount.Length - 3);
                groups.Add(strAmount[start..]);
                strAmount = strAmount[..start];
            }

            var resultStr = "";
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                var groupVal = int.Parse(groups[i]);
                if (groupVal > 0)
                {
                    var showZeroHundred = i < groups.Count - 1;
                    resultStr += ReadGroup3(groupVal, showZeroHundred) + Units[i] + " ";
                }
            }
            resultStr = resultStr.Trim();
            if (string.IsNullOrEmpty(resultStr)) return "Không đồng";
            return char.ToUpper(resultStr[0]) + resultStr[1..] + " đồng chẵn.";
        }
    }
}
