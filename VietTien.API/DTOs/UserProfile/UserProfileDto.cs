namespace VietTien.API.DTOs.UserProfile
{
    public class UserProfileDto
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string? DefaultAddressId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool EmailVerified { get; set; }
    }
}

