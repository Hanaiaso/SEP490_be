using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Admin
{
    public class SetSalesTargetRequest
    {
        [Required]
        public Guid SalesStaffId { get; set; }

        [Range(1, 9999)]
        public int Year { get; set; }

        [Range(1, 12)]
        public int Month { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Mục tiêu doanh thu không được âm.")]
        public decimal TargetAmount { get; set; }
    }

    public class SalesStaffTargetDto
    {
        public Guid SalesStaffId { get; set; }
        public string SalesStaffName { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal TargetAmount { get; set; } // 0 = chưa đặt mục tiêu cho tháng này
        public decimal ActualRevenue { get; set; } // doanh thu thực thu (AmountPaid) trong tháng đó
        public double? AchievementRate { get; set; } // ActualRevenue / TargetAmount, null nếu chưa có mục tiêu
        public DateTime? SetAt { get; set; }
        public string? SetByName { get; set; }
    }
}
