using System.ComponentModel.DataAnnotations;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Quotation
{
    public class CreateQuotationRequest
    {
        public Guid? CartId { get; set; }
        public string? GeneralNote { get; set; }
    }

    public class CreateQuotationVersionRequest
    {
        [Required]
        public decimal ProposedTotal { get; set; }
        public string? SalesNote { get; set; }
        [Required]
        public List<QuotationVersionItemRequest> Items { get; set; } = new List<QuotationVersionItemRequest>();
    }

    public class QuotationVersionItemRequest
    {
        [Required]
        public Guid ProductId { get; set; }
        [Required]
        public decimal ProposedUnitPrice { get; set; }
    }

    public class ManagerReviewRequest
    {
        [Required]
        public bool IsApproved { get; set; }
        public string? ManagerNote { get; set; }
    }

    public class CeoReviewRequest
    {
        [Required]
        public bool IsApproved { get; set; }
        public string? CeoNote { get; set; }
    }

    public class CustomerDecisionRequest
    {
        [Required]
        public bool IsAccepted { get; set; }
    }

    public class QuotationDto
    {
        public Guid Id { get; set; }
        public Guid CustomerProfileId { get; set; }
        public Guid? SalesStaffId { get; set; }
        public Guid? CartId { get; set; }
        public Guid? AcceptedVersionId { get; set; }
        
        public string CustomerName { get; set; } = string.Empty;
        public string? SalesStaffName { get; set; }
        
        public string Status { get; set; } = string.Empty;
        
        public decimal OriginalTotal { get; set; }
        
        public DateTime RequestDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        
        public string? GeneralNote { get; set; }

        public List<QuotationItemDto> Items { get; set; } = new List<QuotationItemDto>();
        public List<QuotationVersionDto> Versions { get; set; } = new List<QuotationVersionDto>();
    }

    public class QuotationItemDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductImageUrl { get; set; }
        public int Quantity { get; set; }
        public decimal OriginalUnitPrice { get; set; }
    }

    public class QuotationVersionDto
    {
        public Guid Id { get; set; }
        public int VersionNumber { get; set; }
        public decimal ProposedTotal { get; set; }
        public string? SalesNote { get; set; }
        public string? ManagerNote { get; set; }
        public string? CeoNote { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ManagerReviewedAt { get; set; }
        public DateTime? CeoReviewedAt { get; set; }
        public DateTime? ValidUntil { get; set; }
        public List<QuotationVersionItemDto> Items { get; set; } = new List<QuotationVersionItemDto>();
    }

    public class QuotationVersionItemDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductImageUrl { get; set; }
        public int Quantity { get; set; }
        public decimal OriginalUnitPrice { get; set; }
        public decimal ProposedUnitPrice { get; set; }
    }

    public class ChatMessageDto
    {
        public Guid Id { get; set; }
        public Guid QuotationId { get; set; }
        public Guid SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string SenderRole { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public string? FileUrl { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
    }

    public class SendChatMessageRequest
    {
        public string MessageText { get; set; } = string.Empty;
        public string? FileUrl { get; set; }
    }
}
