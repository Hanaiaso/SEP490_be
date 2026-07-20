using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IQuotationService _quotationService;

        public ChatHub(IQuotationService quotationService)
        {
            _quotationService = quotationService;
        }

        public async Task JoinQuotationChat(string quotationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, quotationId);
        }

        public async Task LeaveQuotationChat(string quotationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, quotationId);
        }

        public async Task SendMessage(string quotationId, string messageText)
        {
            var userId = Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var request = new SendChatMessageRequest { MessageText = messageText };
            var chatMessageDto = await _quotationService.SendMessageAsync(Guid.Parse(quotationId), userId, request);

            await Clients.Group(quotationId).SendAsync("ReceiveMessage", chatMessageDto);
        }
    }
}
