using VietTien.API.Models;

namespace VietTien.API.Repositories.Interfaces
{
    public interface ICartRepository
    {
        Task<Cart?> GetCartByCustomerIdAsync(Guid customerProfileId);
        Task<Cart> CreateCartAsync(Guid customerProfileId);
        Task<CartItem?> GetCartItemAsync(Guid cartId, Guid productId);
        Task<CartItem?> GetCartItemByIdAsync(Guid cartItemId);
        Task AddCartItemAsync(CartItem item);
        void RemoveCartItem(CartItem item);
        Task ClearCartAsync(Guid cartId);
    }
}
