using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VietTien.API.Data;

namespace VietTien.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        private const int MaxPageLimit = 100;

        // P1: tham số trước đây tên là "page"/"limit" trong khi FE (notificationService.js) luôn gửi
        // "pageNumber"/"pageSize"/"isRead" -> model binder không khớp tên nên "page"/"limit" luôn rơi về
        // giá trị mặc định (trang 1, 20 dòng) bất kể FE gửi gì, và "isRead" chưa từng được đọc/áp dụng.
        [HttpGet]
        public async Task<IActionResult> GetNotifications(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool? isRead = null)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            pageNumber = pageNumber < 1 ? 1 : pageNumber;
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, MaxPageLimit);

            var query = _context.Notifications
                .Where(n => n.RecipientUserId == userId);

            if (isRead.HasValue)
            {
                query = query.Where(n => n.IsRead == isRead.Value);
            }

            query = query.OrderByDescending(n => n.CreatedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Items = items
            });
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var count = await _context.Notifications
                .Where(n => n.RecipientUserId == userId && !n.IsRead)
                .CountAsync();

            return Ok(new { UnreadCount = count });
        }

        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == userId);
            if (notif == null) return NotFound();

            notif.IsRead = true;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var unreadNotifs = await _context.Notifications
                .Where(n => n.RecipientUserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in unreadNotifs)
            {
                n.IsRead = true;
            }

            if (unreadNotifs.Any())
            {
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNotification(Guid id)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == userId);
            if (notif == null) return NotFound();

            _context.Notifications.Remove(notif);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("read-all")]
        public async Task<IActionResult> DeleteAllRead()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var readNotifs = await _context.Notifications
                .Where(n => n.RecipientUserId == userId && n.IsRead)
                .ToListAsync();

            if (readNotifs.Any())
            {
                _context.Notifications.RemoveRange(readNotifs);
                await _context.SaveChangesAsync();
            }

            return Ok(new { Deleted = readNotifs.Count });
        }
    }
}
