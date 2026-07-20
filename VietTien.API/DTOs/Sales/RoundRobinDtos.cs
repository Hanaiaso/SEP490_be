namespace VietTien.API.DTOs.Sales
{
    public class RoundRobinStateDto
    {
        public bool IsPaused { get; set; }
        public Guid? CursorStaffId { get; set; }
        public string? CursorStaffName { get; set; }
        public RoundRobinStaffDto? NextStaff { get; set; }
        public List<RoundRobinParticipantDto> Participants { get; set; } = new();
        public int UnassignedCustomerCount { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<RoundRobinAssignmentLogDto> RecentAssignments { get; set; } = new();
    }

    public class RoundRobinStaffDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class RoundRobinParticipantDto
    {
        public Guid ParticipantId { get; set; }
        public Guid StaffId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public int AssignedCustomerCount { get; set; }
    }

    public class RoundRobinAssignmentLogDto
    {
        public Guid Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string StaffName { get; set; } = string.Empty;
        public string? AssignedByName { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
    }

    public class UpdateRoundRobinRequest
    {
        public bool? IsPaused { get; set; }
        public Guid? CursorStaffId { get; set; }
        public List<ParticipantToggleDto>? Participants { get; set; }
    }

    public class ParticipantToggleDto
    {
        public Guid StaffId { get; set; }
        public bool IsActive { get; set; }
    }

    public class RoundRobinAssignResultDto
    {
        public int AssignedCount { get; set; }
        public Guid? NewCursorStaffId { get; set; }
        public List<RoundRobinAssignmentItemDto> Assignments { get; set; } = new();
    }

    public class RoundRobinAssignmentItemDto
    {
        public Guid CustomerProfileId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid StaffId { get; set; }
        public string StaffName { get; set; } = string.Empty;
    }
}
