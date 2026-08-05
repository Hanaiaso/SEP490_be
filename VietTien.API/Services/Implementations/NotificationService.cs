using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using VietTien.API.Data;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task CreateNotificationAsync(NotificationType type, Guid recipientUserId, string title, string body, Guid? referenceId = null, string? referenceType = null)
        {
            var notification = new Notification
            {
                RecipientUserId = recipientUserId,
                Type = type,
                Title = title,
                Body = body,
                ReferenceId = referenceId,
                ReferenceType = referenceType
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Đẩy realtime
            await _hubContext.Clients.Group($"User_{recipientUserId}").SendAsync("ReceiveNotification", notification);
        }

        public async Task CreateRoleNotificationAsync(NotificationType type, SystemRole targetRole, string title, string body, Guid? referenceId = null, string? referenceType = null)
        {
            // Tìm tất cả user có role tương ứng
            var users = await _context.Users
                .Where(u => u.Role == targetRole)
                .Select(u => u.Id)
                .ToListAsync();

            var notifications = users.Select(userId => new Notification
            {
                RecipientUserId = userId,
                Type = type,
                Title = title,
                Body = body,
                ReferenceId = referenceId,
                ReferenceType = referenceType
            }).ToList();

            if (notifications.Any())
            {
                _context.Notifications.AddRange(notifications);
                await _context.SaveChangesAsync();

                // Đẩy realtime theo Role group — payload KHÔNG kèm Id/RecipientUserId của một người cụ thể
                // (mỗi user trong role có 1 bản ghi Notification riêng, Id khác nhau). Gửi thẳng
                // notifications.First() sẽ khiến mọi client trong group nhận nhầm Id của người khác,
                // dẫn đến đánh dấu "đã đọc" sai bản ghi. Client chỉ dùng payload này làm tín hiệu
                // "có thông báo mới" rồi tự gọi API lấy danh sách/Id thông báo của chính mình.
                await _hubContext.Clients.Group($"Role_{targetRole.ToString()}").SendAsync("ReceiveNotification", new
                {
                    type = type.ToString(),
                    title,
                    body,
                    referenceId,
                    referenceType,
                    createdAt = notifications[0].CreatedAt
                });
            }
        }
    }
}
