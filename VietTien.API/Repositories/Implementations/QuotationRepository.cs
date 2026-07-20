using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;

namespace VietTien.API.Repositories.Implementations
{
    public class QuotationRepository : IQuotationRepository
    {
        private readonly ApplicationDbContext _context;

        public QuotationRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Quotation> CreateAsync(Quotation quotation)
        {
            _context.Quotations.Add(quotation);
            await _context.SaveChangesAsync();
            return quotation;
        }

        public async Task<Quotation?> GetByIdAsync(Guid id)
        {
            return await _context.Quotations
                .Include(q => q.Items)
                    .ThenInclude(i => i.Product)
                .Include(q => q.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(q => q.SalesStaff)
                .Include(q => q.Versions)
                    .ThenInclude(v => v.Items)
                .Include(q => q.ChatMessages.OrderBy(m => m.SentAt))
                    .ThenInclude(m => m.Sender)
                .FirstOrDefaultAsync(q => q.Id == id);
        }

        public async Task<IEnumerable<Quotation>> GetByCustomerProfileIdAsync(Guid customerProfileId)
        {
            return await _context.Quotations
                .Include(q => q.SalesStaff)
                .Include(q => q.Items)
                .Include(q => q.Versions)
                .Where(q => q.CustomerProfileId == customerProfileId)
                .OrderByDescending(q => q.RequestDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Quotation>> GetBySalesStaffIdAsync(Guid salesStaffId)
        {
            return await _context.Quotations
                .Include(q => q.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(q => q.Versions)
                .Where(q => q.SalesStaffId == salesStaffId)
                .OrderByDescending(q => q.RequestDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Quotation>> GetAllPendingAsync()
        {
            return await _context.Quotations
                .Include(q => q.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Where(q => q.Status == QuotationStatus.Draft)
                .OrderBy(q => q.RequestDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Quotation>> GetAllAsync()
        {
            return await _context.Quotations
                .Include(q => q.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(q => q.SalesStaff)
                .Include(q => q.Versions)
                .OrderByDescending(q => q.RequestDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Quotation>> GetManagerPendingApprovalAsync()
        {
            return await _context.Quotations
                .Include(q => q.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(q => q.SalesStaff)
                .Include(q => q.Versions)
                .Where(q => q.Status == QuotationStatus.PendingManager)
                .OrderBy(q => q.RequestDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Quotation>> GetCeoPendingApprovalAsync()
        {
            return await _context.Quotations
                .Include(q => q.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(q => q.SalesStaff)
                .Include(q => q.Versions)
                .Where(q => q.Status == QuotationStatus.PendingCeo)
                .OrderBy(q => q.RequestDate)
                .ToListAsync();
        }

        public async Task<Quotation> UpdateAsync(Quotation quotation)
        {
            _context.Quotations.Update(quotation);
            await _context.SaveChangesAsync();
            return quotation;
        }

        // Version

        public async Task<QuotationVersion?> GetVersionByIdAsync(Guid versionId)
        {
            return await _context.QuotationVersions
                .Include(v => v.Quotation)
                .Include(v => v.Items)
                .FirstOrDefaultAsync(v => v.Id == versionId);
        }

        public async Task<QuotationVersion> CreateVersionAsync(QuotationVersion version)
        {
            _context.QuotationVersions.Add(version);
            await _context.SaveChangesAsync();
            return version;
        }

        public async Task<QuotationVersion> UpdateVersionAsync(QuotationVersion version)
        {
            _context.QuotationVersions.Update(version);
            await _context.SaveChangesAsync();
            return version;
        }

        // Chat

        public async Task<ChatMessage> AddMessageAsync(ChatMessage message)
        {
            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();
            return message;
        }

        public async Task<IEnumerable<ChatMessage>> GetMessagesByQuotationIdAsync(Guid quotationId)
        {
            return await _context.ChatMessages
                .Include(m => m.Sender)
                .Where(m => m.QuotationId == quotationId)
                .OrderBy(m => m.SentAt)
                .ToListAsync();
        }
    }
}
