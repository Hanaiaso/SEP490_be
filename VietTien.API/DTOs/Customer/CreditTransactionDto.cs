using System;

namespace VietTien.API.DTOs.Customer
{
    public class CreditTransactionDto
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Guid? OrderId { get; set; }
    }
}
