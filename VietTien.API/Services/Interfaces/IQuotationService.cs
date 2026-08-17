using VietTien.API.DTOs.Quotation;

namespace VietTien.API.Services.Interfaces
{
    public interface IQuotationService
    {
        Task<QuotationDto> CreateQuotationFromCartAsync(Guid userId, CreateQuotationRequest request);
        Task<QuotationDto> GetQuotationByIdAsync(Guid quotationId, Guid userId, string userRole);
        Task<IEnumerable<QuotationDto>> GetCustomerQuotationsAsync(Guid userId);
        Task<IEnumerable<QuotationDto>> GetSalesQuotationsAsync(Guid userId);
        Task<IEnumerable<QuotationDto>> GetAllPendingQuotationsAsync();
        Task<IEnumerable<QuotationDto>> GetAllQuotationsAsync();
        
        // Admin / Manager Views
        Task<IEnumerable<QuotationDto>> GetManagerPendingApprovalQuotationsAsync();
        Task<IEnumerable<QuotationDto>> GetCeoPendingApprovalQuotationsAsync();

        // Actions
        Task<QuotationDto> PickUpQuotationAsync(Guid quotationId, Guid salesStaffId);

        // Sales Manager phân công thủ công cho báo giá ≥ ngưỡng B2B (thay cho Sale tự nhận xử lý).
        Task<QuotationDto> AssignQuotationAsync(Guid quotationId, Guid managerId, AssignQuotationRequest request);

        // New Versioning Logic
        Task<QuotationVersionDto> CreateVersionAsync(Guid quotationId, Guid salesStaffId, CreateQuotationVersionRequest request);
        Task<QuotationVersionDto> ManagerReviewVersionAsync(Guid quotationId, Guid managerId, ManagerReviewRequest request);
        Task<QuotationVersionDto> CeoReviewVersionAsync(Guid quotationId, Guid ceoId, CeoReviewRequest request);
        Task<QuotationVersionDto> CustomerDecisionAsync(Guid quotationId, Guid customerId, CustomerDecisionRequest request);
        
        Task<QuotationDto> CancelQuotationAsync(Guid quotationId, Guid customerId);

        // Chat
        Task<ChatMessageDto> SendMessageAsync(Guid quotationId, Guid senderId, SendChatMessageRequest request);
        Task<IEnumerable<ChatMessageDto>> GetMessagesAsync(Guid quotationId, Guid userId, string userRole);
    }
}
