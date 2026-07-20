using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class ShortageAlertRequestDto
    {
        [Required]
        public Guid ProductId { get; set; }
        
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Missing Quantity must be greater than 0")]
        public int MissingQuantity { get; set; }
        
        public string? Note { get; set; }
    }
}
