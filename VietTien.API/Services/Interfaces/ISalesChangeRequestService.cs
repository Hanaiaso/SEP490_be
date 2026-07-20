using VietTien.API.DTOs.SalesChange;

namespace VietTien.API.Services.Interfaces
{
    // LUỒNG 7: Khách hàng yêu cầu đổi nhân viên Sale phụ trách (WF-07; CUS-11, MGR-06)
    public interface ISalesChangeRequestService
    {
        // ─── Customer ───
        Task<Guid> CreateAsync(Guid userId, CreateSalesChangeRequestDto dto);
        Task<List<SalesChangeRequestDetailDto>> GetMineAsync(Guid userId);
        Task<MyAssignedSaleDto> GetMyAssignedSaleAsync(Guid userId);
        Task<List<SalesOptionDto>> GetSalesOptionsAsync();
        Task CancelAsync(Guid userId, Guid requestId);
        Task SubmitAdditionalInfoAsync(Guid userId, Guid requestId, string info);

        // ─── SalesStaff (Sale hiện tại) ───
        // Chỉ trả về các yêu cầu đã được Manager bấm "Yêu cầu giải trình" (gate bảo vệ khách hàng)
        Task<List<SalesChangeRequestDetailDto>> GetAboutMeAsync(Guid salesStaffId);
        Task SubmitExplanationAsync(Guid salesStaffId, Guid requestId, SaleExplanationDto dto);

        // ─── SalesManager ───
        Task<SalesChangeRequestPagedResultDto> GetPagedAsync(SalesChangeRequestQueryDto query);
        Task<SalesChangeRequestDetailDto> GetDetailAsync(Guid requestId);
        Task<ReviewContextDto> GetReviewContextAsync(Guid requestId);
        Task RequestExplanationAsync(Guid managerId, Guid requestId);
        Task RequestMoreInfoAsync(Guid managerId, Guid requestId, string note);
        Task RejectAsync(Guid managerId, Guid requestId, string note);
        Task ApproveAsync(Guid managerId, Guid requestId, ApproveSalesChangeRequestDto dto);
    }
}
