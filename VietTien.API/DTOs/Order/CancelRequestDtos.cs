using System;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Order
{
    public class ProcessCancelRequestDto
    {
        public bool IsApproved { get; set; }

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }
    
    public class RequestCancelOrderDto
    {
        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }
}
