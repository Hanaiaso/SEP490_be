using System.Threading.Tasks;

namespace VietTien.API.Services.Interfaces
{
    public interface ISmsService
    {
        Task<(bool Success, string ErrorMessage)> SendSmsAsync(string phoneNumber, string message);
    }
}
