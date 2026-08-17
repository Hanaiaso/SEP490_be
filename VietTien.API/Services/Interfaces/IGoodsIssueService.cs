using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Services.Interfaces
{
    public interface IGoodsIssueService
    {
        // Mọi hàm đều nhận staffId: phiếu xuất kho tác động trực tiếp lên tồn kho vật lý nên phải
        // đối chiếu kho được phân công (SRS NAC-05), kể cả các hàm chỉ đọc/sửa metadata chứng từ.
        Task<IEnumerable<GoodsIssueDto>> GetGoodsIssuesAsync(string? type, Guid staffId);
        Task<GoodsIssueDto> GetGoodsIssueByIdAsync(Guid id, Guid staffId);
        Task<byte[]> ExportExcelAsync(Guid id, Guid staffId);
        Task<GoodsIssueDto> CreateGoodsIssueAsync(CreateGoodsIssueRequestDto request, Guid staffId);
        Task<GoodsIssueDto> UploadProofAsync(Guid issueId, Guid staffId, IFormFile file);
        Task<GoodsIssueDto> UpdateHandoverInfoAsync(Guid issueId, Guid staffId, UpdateGoodsIssueHandoverDto dto);
        Task<GoodsIssueDto> PostGoodsIssueAsync(Guid issueId, Guid staffId);
        Task<GoodsIssueDto> CreateReversalAsync(Guid issueId, CreateReversalRequestDto dto, Guid staffId);
    }
}
