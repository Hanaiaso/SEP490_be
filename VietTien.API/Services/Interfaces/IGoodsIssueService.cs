using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Services.Interfaces
{
    public interface IGoodsIssueService
    {
        Task<IEnumerable<GoodsIssueDto>> GetGoodsIssuesAsync(string? type);
        Task<GoodsIssueDto> GetGoodsIssueByIdAsync(Guid id);
        Task<GoodsIssueDto> CreateGoodsIssueAsync(CreateGoodsIssueRequestDto request, Guid staffId);
        Task<GoodsIssueDto> UploadProofAsync(Guid issueId, IFormFile file);
        Task<GoodsIssueDto> UpdateHandoverInfoAsync(Guid issueId, UpdateGoodsIssueHandoverDto dto);
        Task<GoodsIssueDto> PostGoodsIssueAsync(Guid issueId, Guid staffId);
        Task<GoodsIssueDto> CreateReversalAsync(Guid issueId, CreateReversalRequestDto dto, Guid staffId);
    }
}
