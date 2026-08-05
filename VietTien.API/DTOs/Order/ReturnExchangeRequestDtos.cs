using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Order
{
    public class CreateReturnExchangeRequestDto
    {
        [Required]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
        
        public string? EvidenceUrls { get; set; } // JSON array

        public List<ReturnExchangeItemDto> ReturnItems { get; set; } = new List<ReturnExchangeItemDto>();
        public List<ReturnExchangeNewItemDto> ExchangeItems { get; set; } = new List<ReturnExchangeNewItemDto>();
    }

    public class ReturnExchangeItemDto
    {
        [Required]
        public Guid ProductId { get; set; }
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }
    }

    public class ReturnExchangeNewItemDto
    {
        [Required]
        public Guid ProductId { get; set; }
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }
    }

    public class ProcessReturnExchangeRequestDto
    {
        public bool IsApproved { get; set; }

        [MaxLength(1000)]
        public string? ManagerNote { get; set; }
    }
}
