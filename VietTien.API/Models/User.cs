namespace VietTien.API.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public SystemRole Role { get; set; } = SystemRole.Customer;
        public string? ReferralCode { get; set; } // Dành cho Sale Staff

        // Khách hàng nhập mã giới thiệu khi đăng ký → Sale giới thiệu (WF-01, nguồn REFERRAL)
        public Guid? ReferredBySalesStaffId { get; set; }
        public User? ReferredBySalesStaff { get; set; }

        // Google OAuth
        public string? GoogleId { get; set; }

        // Email Verification (OTP)
        public bool IsEmailVerified { get; set; } = false;
        public string? OtpCode { get; set; }
        public DateTime? OtpExpiry { get; set; }

        // Phone Verification (SMS)
        public bool IsPhoneVerified { get; set; } = false;
        public string? PhoneOtpCode { get; set; }
        public DateTime? PhoneOtpExpiry { get; set; }

        // Password Reset
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpiry { get; set; }

        // JWT Refresh Token
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public CustomerProfile? CustomerProfile { get; set; }
        public EmployeeSalary? EmployeeSalary { get; set; }
        public ICollection<AiMarketingCampaign> MarketingCampaigns { get; set; } = new List<AiMarketingCampaign>();
        public ICollection<PayrollDetail> PayrollDetails { get; set; } = new List<PayrollDetail>();
    }

    public enum SystemRole
    {
        Guest,
        Customer,
        SalesStaff,
        SalesManager,
        WarehouseStaff,
        AccountingStaff,
        CEO,
        Admin
    }
}
