using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VietTien.API.Hubs
{
    [Authorize]
    public class WarehouseHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // Có thể add vào group tương ứng
            await Groups.AddToGroupAsync(Context.ConnectionId, "WarehouseStaff");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "WarehouseStaff");
            await base.OnDisconnectedAsync(exception);
        }
    }
}
