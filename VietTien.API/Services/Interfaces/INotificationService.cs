using System;
using System.Threading.Tasks;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface INotificationService
    {
        Task CreateNotificationAsync(NotificationType type, Guid recipientUserId, string title, string body, Guid? referenceId = null, string? referenceType = null);
        Task CreateRoleNotificationAsync(NotificationType type, SystemRole targetRole, string title, string body, Guid? referenceId = null, string? referenceType = null);
    }
}
