using VietTien.API.Models;

namespace VietTien.API.Repositories.Interfaces
{
    public interface IQuotationRepository
    {
        Task<Quotation> CreateAsync(Quotation quotation);
        Task<Quotation?> GetByIdAsync(Guid id);
        Task<IEnumerable<Quotation>> GetByCustomerProfileIdAsync(Guid customerProfileId);
        Task<IEnumerable<Quotation>> GetBySalesStaffIdAsync(Guid salesStaffId);
        Task<IEnumerable<Quotation>> GetAllPendingAsync(); // Draft
        Task<IEnumerable<Quotation>> GetManagerPendingApprovalAsync(); // PendingManager
        Task<IEnumerable<Quotation>> GetCeoPendingApprovalAsync(); // PendingCeo
        Task<IEnumerable<Quotation>> GetAllAsync();
        Task<Quotation> UpdateAsync(Quotation quotation);
        
        // Version
        Task<QuotationVersion?> GetVersionByIdAsync(Guid versionId);
        Task<QuotationVersion> CreateVersionAsync(QuotationVersion version);
        Task<QuotationVersion> UpdateVersionAsync(QuotationVersion version);

        Task<ChatMessage> AddMessageAsync(ChatMessage message);
        Task<IEnumerable<ChatMessage>> GetMessagesByQuotationIdAsync(Guid quotationId);
    }
}
