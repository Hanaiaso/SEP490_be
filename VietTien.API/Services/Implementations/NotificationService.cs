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
                
                // Đẩy realtime theo Role group, lưu ý gửi chung 1 object notification (không có ID cụ thể của ai, Frontend dùng chung)
                // Hoặc có thể gửi object đầu tiên để map dữ liệu hiển thị cơ bản
                await _hubContext.Clients.Group($"Role_{targetRole.ToString()}").SendAsync("ReceiveNotification", notifications.First());
            }
        }
    }
}
