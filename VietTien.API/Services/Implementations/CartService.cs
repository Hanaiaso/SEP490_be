using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Cart;
using VietTien.API.Exceptions;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class CartService : ICartService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;
        private readonly IInventoryReservationService _inventoryReservationService;

        public CartService(IUnitOfWork unitOfWork, ApplicationDbContext context, IInventoryReservationService inventoryReservationService)
        {
            _unitOfWork = unitOfWork;
            _context = context;
            _inventoryReservationService = inventoryReservationService;
        }

        /// <summary>Lấy hoặc tự tạo CustomerProfile cho user (tránh lỗi "không tìm thấy" với tài khoản mới)</summary>
        private async Task<CustomerProfile> GetCustomerProfileAsync(Guid userId)
        {
            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile != null) return profile;

            profile = new CustomerProfile { UserId = userId };
            await _unitOfWork.Users.AddCustomerProfileAsync(profile);
            await _unitOfWork.SaveChangesAsync();
            return profile;
        }

        public async Task<CartDto> GetCartAsync(Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart == null)
            {
                return new CartDto
                {
                    CustomerProfileId = profile.Id,
                    Items = new List<CartItemDto>(),
                    TotalItems = 0,
                    TotalPrice = 0,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            // Quy tắc giữ giá 24h (BR-025): CHỈ báo hiệu qua IsPriceExpired, KHÔNG tự sửa giá/UpdatedAt
            // ở đây. Trước đây hàm này tự "chữa lành" giá + reset UpdatedAt ngay khi đọc giỏ, nên
            // guard 24h ở OrderService.PlaceOrderAsync không bao giờ còn thấy giỏ hết hạn — màn Cart/
            // Checkout luôn gọi GetCartAsync (qua checkout-summary) trước khi khách kịp bấm đặt hàng,
            // xoá mất dấu vết hết hạn một cách âm thầm. Việc làm mới giá thật sự chỉ diễn ra khi khách
            // bấm nút xác nhận, xem RefreshCartPricesAsync.
            //
            // Mỗi dòng có mốc giữ giá RIÊNG (PriceLockedAt, set khi 1 đợt ProductPriceUpdateOrder đổi
            // giá sản phẩm đó trong lúc đang nằm trong giỏ) — dòng nào chưa từng bị đụng tới thì fallback
            // về mốc chung Cart.UpdatedAt như quy tắc BR-025 cũ (xem CartPriceLockHelper).

            // Map to DTO
            var cartDto = new CartDto
            {
                Id = cart.Id,
                CustomerProfileId = cart.CustomerProfileId,
                UpdatedAt = cart.UpdatedAt,
                Items = cart.Items.Select(i => new CartItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.Product.Name,
                    ImageUrl = i.Product.ImageUrl,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    IsPriceExpired = CartPriceLockHelper.IsItemExpired(i.PriceLockedAt, cart.UpdatedAt)
                }).ToList()
            };
            cartDto.IsPriceExpired = cartDto.Items.Any(i => i.IsPriceExpired);

            cartDto.TotalItems = cartDto.Items.Sum(i => i.Quantity);
            var baseTotalPrice = cartDto.Items.Sum(i => i.TotalPrice);

            // Xử lý Tiered Pricing cơ bản (Có thể thay đổi công thức sau)
            if (baseTotalPrice >= 100_000_000 && !string.IsNullOrEmpty(profile.SavedB2BPriceSnapshot))
            {
                // TODO: Parse SavedB2BPriceSnapshot and apply specific prices
                // Tạm thời bỏ qua nếu chưa có JSON format chuẩn
                cartDto.TotalPrice = baseTotalPrice;
            }
            else if (baseTotalPrice >= 10_000_000 && baseTotalPrice < 100_000_000)
            {
                // Ví dụ: Chiết khấu 5% cho đơn 10-100tr
                cartDto.TotalPrice = baseTotalPrice * 0.95m; 
            }
            else
            {
                // Dưới 10tr: giá niêm yết
                cartDto.TotalPrice = baseTotalPrice;
            }

            return cartDto;
        }

        /// <summary>BR-025: khách xác nhận làm mới giá cho giỏ đã hết hạn giữ giá 24h — cập nhật
        /// UnitPrice về giá niêm yết hiện hành, reset mốc 24h, và giữ chỗ tồn kho lại cho các dòng đã
        /// bị CartReservationExpiryJob nhả (best-effort — nếu hết hàng thật thì ném lỗi rõ ràng, khách
        /// vẫn có thể thấy qua alert(err.message) sẵn có ở nút "Làm mới giá").</summary>
        public async Task<CartDto> RefreshCartPricesAsync(Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart != null)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var item in cart.Items)
                    {
                        if (item.UnitPrice != item.Product.StandardListedPrice)
                            item.UnitPrice = item.Product.StandardListedPrice;
                        item.PriceLockedAt = null;

                        if (item.ReservationReleasedAt != null)
                        {
                            await _inventoryReservationService.ReserveAsync(new[] { (item.ProductId, item.Quantity) });
                            item.ReservationReleasedAt = null;
                        }
                    }

                    cart.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> AddItemToCartAsync(Guid userId, AddToCartRequestDto request)
        {
            var profile = await GetCustomerProfileAsync(userId);

            // Yêu cầu hồ sơ đầy đủ (có ít nhất 1 địa chỉ giao hàng) trước khi cho thêm vào giỏ
            var addressCount = await _unitOfWork.Addresses.CountByCustomerProfileIdAsync(profile.Id);
            if (addressCount == 0)
                throw new ProfileIncompleteException("Vui lòng cập nhật địa chỉ giao hàng trước khi mua hàng.");

            var cart = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart == null)
            {
                cart = await _unitOfWork.Carts.CreateCartAsync(profile.Id);
                await _unitOfWork.SaveChangesAsync(); // Cần lưu để sinh Id
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
            if (product == null || product.IsDiscontinued)
                throw new Exception("Product not found or discontinued.");

            var cartItem = await _unitOfWork.Carts.GetCartItemAsync(cart.Id, request.ProductId);

            // Giữ chỗ tồn kho NGAY khi thêm vào giỏ (không đợi tới lúc đặt hàng) — để khách bỏ giỏ
            // trước không bị người mua sau (giỏ khác hoặc mua trực tiếp) giành mất hàng trong lúc còn
            // hiệu lực giữ giá 24h. Ném lỗi rõ ràng và KHÔNG tạo/cộng dòng giỏ nếu không đủ tồn.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _inventoryReservationService.ReserveAsync(new[] { (product.Id, request.Quantity) });

                if (cartItem != null)
                {
                    cartItem.Quantity += request.Quantity;
                    // Có thể cân nhắc việc update giá hay giữ nguyên, tạm thời giữ nguyên giá của sản phẩm đã thêm
                }
                else
                {
                    cartItem = new CartItem
                    {
                        CartId = cart.Id,
                        ProductId = product.Id,
                        Quantity = request.Quantity,
                        UnitPrice = product.StandardListedPrice
                    };
                    await _unitOfWork.Carts.AddCartItemAsync(cartItem);
                }

                cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> UpdateCartItemAsync(Guid userId, Guid cartItemId, UpdateCartItemRequestDto request)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cartItem = await _unitOfWork.Carts.GetCartItemByIdAsync(cartItemId);

            if (cartItem == null || cartItem.Cart.CustomerProfileId != profile.Id)
                throw new Exception("Cart item not found.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (cartItem.ReservationReleasedAt != null)
                {
                    // Đang không giữ chỗ gì cho dòng này (đã bị nhả trước đó) -> giữ chỗ TOÀN BỘ số
                    // lượng mới, không chỉ phần chênh lệch.
                    await _inventoryReservationService.ReserveAsync(new[] { (cartItem.ProductId, request.Quantity) });
                    cartItem.ReservationReleasedAt = null;
                }
                else
                {
                    var delta = request.Quantity - cartItem.Quantity;
                    if (delta > 0)
                        await _inventoryReservationService.ReserveAsync(new[] { (cartItem.ProductId, delta) });
                    else if (delta < 0)
                        await _inventoryReservationService.ReleaseReservedAsync(new[] { (cartItem.ProductId, -delta) });
                }

                cartItem.Quantity = request.Quantity;
                cartItem.Cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> RemoveItemFromCartAsync(Guid userId, Guid cartItemId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cartItem = await _unitOfWork.Carts.GetCartItemByIdAsync(cartItemId);

            if (cartItem == null || cartItem.Cart.CustomerProfileId != profile.Id)
                throw new Exception("Cart item not found.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (cartItem.ReservationReleasedAt == null)
                    await _inventoryReservationService.ReleaseReservedAsync(new[] { (cartItem.ProductId, cartItem.Quantity) });

                _unitOfWork.Carts.RemoveCartItem(cartItem);
                cartItem.Cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> ClearCartAsync(Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart != null)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var toRelease = cart.Items
                        .Where(i => i.ReservationReleasedAt == null)
                        .Select(i => (i.ProductId, i.Quantity));
                    await _inventoryReservationService.ReleaseReservedAsync(toRelease);

                    await _unitOfWork.Carts.ClearCartAsync(cart.Id);
                    cart.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            return await GetCartAsync(userId);
        }
    }
}
