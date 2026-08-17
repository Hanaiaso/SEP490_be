using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IOrderInvoiceService
    {
        /// <summary>Sinh Hóa đơn PDF chính thức cho 1 đơn hàng — luôn theo yêu cầu (không cache), phản
        /// ánh dữ liệu mới nhất kể cả khi hóa đơn đỏ được nhập SAU khi đơn đã tạo.</summary>
        byte[] GenerateInvoicePdf(Order order);
    }
}
