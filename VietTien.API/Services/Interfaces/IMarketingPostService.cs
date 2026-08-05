using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VietTien.API.DTOs.Marketing;

namespace VietTien.API.Services.Interfaces
{
    public interface IMarketingPostService
    {
        Task<IEnumerable<MarketingPostDto>> GetPostsAsync(string? status, Guid? productId, string? role, Guid userId);
        Task<MarketingPostDto> GetPostByIdAsync(Guid id);
        Task<MarketingPostDto> CreatePostAsync(CreateMarketingPostDto dto, Guid userId);
        Task<MarketingPostDto> UpdatePostAsync(Guid id, UpdateMarketingPostDto dto, Guid userId, string userRole);
        Task<MarketingPostDto> SubmitPostAsync(Guid id, Guid userId, string userRole);
        Task<MarketingPostDto> MakeDecisionAsync(Guid id, MarketingPostDecisionDto dto, Guid managerId);
        Task<MarketingPostDto> PublishNowAsync(Guid id, Guid managerId);
        Task<MarketingPostDto> HandleMakeWebhookCallbackAsync(Guid id, MakeWebhookCallbackDto dto);
    }
}
