using VietTien.API.Models;

namespace VietTien.API.Services.Implementations
{
    // Dùng chung giữa CartService (hiển thị IsPriceExpired) và OrderService.PlaceOrderAsync (chặn đặt
    // hàng khi giá hết hạn giữ) để tránh 2 nơi tự tính "còn giữ giá 24h không" theo 2 cách khác nhau.
    // Mỗi dòng giỏ hàng có mốc giữ giá RIÊNG (CartItem.PriceLockedAt, set khi 1 ProductPriceUpdateOrder
    // thực thi đổi giá sản phẩm đó) — nếu dòng chưa từng bị 1 đợt cập nhật giá nào đụng tới thì fallback
    // về mốc chung của cả giỏ (Cart.UpdatedAt, quy tắc BR-025 cũ).
    public static class CartPriceLockHelper
    {
        public static bool IsItemExpired(DateTime? priceLockedAt, DateTime cartUpdatedAt) =>
            (DateTime.UtcNow - (priceLockedAt ?? cartUpdatedAt)).TotalHours > 24;

        public static bool IsAnyItemExpired(IEnumerable<CartItem> items, DateTime cartUpdatedAt) =>
            items.Any(i => IsItemExpired(i.PriceLockedAt, cartUpdatedAt));
    }
}
