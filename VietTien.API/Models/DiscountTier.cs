namespace VietTien.API.Models
{
    // Khung chiết khấu tự động theo CR-01: đơn từ MinAmount đến trước MaxAmount được giảm DiscountPercent.
    // MaxAmount = null nghĩa là không giới hạn trên (mở). Không có tier khớp => 0% (giá niêm yết).
    public class DiscountTier
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public decimal MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public decimal DiscountPercent { get; set; } // 0..1, vd 0.05 = 5%
        public bool IsActive { get; set; } = true;
        public string? Description { get; set; }
    }
}
