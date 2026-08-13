namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi giỏ hàng (SKU/số lượng) không còn khớp tuyệt đối với version báo giá đã được khách
    /// chấp nhận — theo SRS NAC-04/AC-05 (FT-02) + BR-007 (Quotation Version Immutability): bất kỳ
    /// thay đổi SKU/quantity/UOM/ngày giao nào cũng làm version cũ hết hiệu lực, phải đàm phán version mới.
    /// </summary>
    public class QuotationVersionStaleException : Exception
    {
        public QuotationVersionStaleException(string message) : base(message) { }
    }
}
