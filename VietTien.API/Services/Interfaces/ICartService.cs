using VietTien.API.DTOs.Cart;

namespace VietTien.API.Services.Interfaces
{
    public interface ICartService
    {
        Task<CartDto> GetCartAsync(Guid userId);
        Task<CartDto> AddItemToCartAsync(Guid userId, AddToCartRequestDto request);
        Task<CartDto> UpdateCartItemAsync(Guid userId, Guid cartItemId, UpdateCartItemRequestDto request);
        Task<CartDto> RemoveItemFromCartAsync(Guid userId, Guid cartItemId);
        Task<CartDto> ClearCartAsync(Guid userId);
    }
}
