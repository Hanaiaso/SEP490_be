using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VietTien.API.Models
{
    public class CreditTransaction
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CustomerProfileId { get; set; }

        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public Guid? OrderId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("CustomerProfileId")]
        public CustomerProfile CustomerProfile { get; set; } = null!;

        [ForeignKey("OrderId")]
        public Order? Order { get; set; }
    }
}
