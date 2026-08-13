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

        public CartService(IUnitOfWork unitOfWork, ApplicationDbContext context)
        {
            _unitOfWork = unitOfWork;
            _context = context;
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
            var isPriceExpired = (DateTime.UtcNow - cart.UpdatedAt).TotalHours > 24;

            // Map to DTO
            var cartDto = new CartDto
            {
                Id = cart.Id,
                CustomerProfileId = cart.CustomerProfileId,
                UpdatedAt = cart.UpdatedAt,
                IsPriceExpired = isPriceExpired,
                Items = cart.Items.Select(i => new CartItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.Product.Name,
                    ImageUrl = i.Product.ImageUrl,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList()
            };

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
        /// UnitPrice về giá niêm yết hiện hành và reset mốc 24h.</summary>
        public async Task<CartDto> RefreshCartPricesAsync(Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart != null)
            {
                foreach (var item in cart.Items)
                {
                    if (item.UnitPrice != item.Product.StandardListedPrice)
                        item.UnitPrice = item.Product.StandardListedPrice;
                }

                cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
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

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> UpdateCartItemAsync(Guid userId, Guid cartItemId, UpdateCartItemRequestDto request)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cartItem = await _unitOfWork.Carts.GetCartItemByIdAsync(cartItemId);

            if (cartItem == null || cartItem.Cart.CustomerProfileId != profile.Id)
                throw new Exception("Cart item not found.");

            cartItem.Quantity = request.Quantity;
            cartItem.Cart.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> RemoveItemFromCartAsync(Guid userId, Guid cartItemId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cartItem = await _unitOfWork.Carts.GetCartItemByIdAsync(cartItemId);

            if (cartItem == null || cartItem.Cart.CustomerProfileId != profile.Id)
                throw new Exception("Cart item not found.");

            _unitOfWork.Carts.RemoveCartItem(cartItem);
            cartItem.Cart.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            return await GetCartAsync(userId);
        }

        public async Task<CartDto> ClearCartAsync(Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart != null)
            {
                await _unitOfWork.Carts.ClearCartAsync(cart.Id);
                cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }

            return await GetCartAsync(userId);
        }
    }
}
