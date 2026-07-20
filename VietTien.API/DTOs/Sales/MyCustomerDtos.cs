namespace VietTien.API.DTOs.Sales
{
    public class MyCustomerQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Search { get; set; }
    }

    public class MyCustomerListItemDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? Representative { get; set; }
        public string? TaxCode { get; set; }
        public decimal AvailableCredit { get; set; }
        public string? AssignmentSource { get; set; }
        public DateTime? AssignedAt { get; set; }
        public string? SalesNote { get; set; }
        public int OrderCount { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }

    public class MyCustomerDetailDto : MyCustomerListItemDto
    {
        public string? CompanyAddress { get; set; }
        public string? InvoiceEmail { get; set; }
        public List<CustomerAddressDto> Addresses { get; set; } = new();
        public List<CustomerRecentOrderDto> RecentOrders { get; set; } = new();
        public List<AssignmentHistoryDto> AssignmentHistory { get; set; } = new();
    }

    public class CustomerAddressDto
    {
        public Guid Id { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string FullAddress { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    public class CustomerRecentOrderDto
    {
        public Guid Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public decimal FinalPayment { get; set; }
        public string OrderStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
    }

    public class AssignmentHistoryDto
    {
        public Guid Id { get; set; }
        public string SalesStaffName { get; set; } = string.Empty;
        public string? AssignedByName { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
    }

    public class UpdateCustomerNoteRequest
    {
        public string? Note { get; set; }
    }
}
