namespace VietTien.API.DTOs.Order
{
    // ─── Query / Filter ───────────────────────────────────────────────────────

    /// <summary>Tham số lọc + phân trang cho danh sách lịch sử đơn hàng (BR-OH-07)</summary>
    public class OrderHistoryQueryDto
    {
        /// <summary>Tìm kiếm theo mã đơn hàng (OrderCode)</summary>
        public string? Search { get; set; }

        /// <summary>Lọc theo OrderStatus: New | Received | Packing | Shortage | InTransit | Delivered | Cancelled | all</summary>
        public string? Status { get; set; }

        /// <summary>Lọc theo PaymentStatus: Pending | Paid | Failed | all</summary>
        public string? PaymentStatus { get; set; }

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate   { get; set; }

        public int Page     { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    // ─── List Item ────────────────────────────────────────────────────────────

    /// <summary>Dữ liệu 1 dòng trong danh sách lịch sử đơn hàng (BR-OH-06)</summary>
    public class OrderHistoryItemDto
    {
        public Guid   Id              { get; set; }
        public string OrderCode       { get; set; } = string.Empty;
        public DateTime CreatedAt     { get; set; }

        // Tài chính – lấy từ PriceSnapshot, không để lộ giá vốn (BR-OH-08, BR-OH-18)
        public decimal TotalAmount    { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal VatAmount      { get; set; }
        public decimal FinalPayment   { get; set; }

        public string PaymentMethod   { get; set; } = string.Empty;
        public string PaymentStatus   { get; set; } = string.Empty;
        public string OrderStatus     { get; set; } = string.Empty;
        public string RedInvoiceStatus{ get; set; } = string.Empty;

        public int    ItemCount        { get; set; }

        // Metadata hành động
        public bool   HasInvoicePdf   { get; set; }
        public bool   CanRequestVat   { get; set; }
    }

    // ─── Order Item (Line) ────────────────────────────────────────────────────

    /// <summary>1 dòng sản phẩm trong đơn hàng – dùng PriceSnapshot (BR-OH-08)</summary>
    public class OrderItemDetailDto
    {
        public Guid    ProductId    { get; set; }
        public string  ProductName  { get; set; } = string.Empty;
        public string  ProductSku   { get; set; } = string.Empty;
        public string? ProductImage { get; set; }

        public int     Quantity      { get; set; }
        public decimal PriceSnapshot { get; set; }   // Đơn giá snapshot (BR-OH-08)
        public decimal LineTotal     { get; set; }   // = PriceSnapshot * Quantity
        // CostSnapshot KHÔNG được trả về (BR-OH-18)
    }

    // ─── Detail ───────────────────────────────────────────────────────────────

    /// <summary>Chi tiết đầy đủ của 1 đơn hàng dành cho Customer (BR-OH-12)</summary>
    public class OrderHistoryDetailDto
    {
        public Guid   Id              { get; set; }
        public string OrderCode       { get; set; } = string.Empty;
        public DateTime CreatedAt     { get; set; }

        // Tài chính
        public decimal TotalAmount    { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal VatAmount      { get; set; }
        public decimal FinalPayment   { get; set; }

        // Trạng thái
        public string PaymentMethod   { get; set; } = string.Empty;
        public string PaymentStatus   { get; set; } = string.Empty;
        public string OrderStatus     { get; set; } = string.Empty;
        public string RedInvoiceStatus{ get; set; } = string.Empty;

        // Thông tin khách hàng & giao hàng
        public string? CustomerName    { get; set; }
        public string? CustomerPhone   { get; set; }
        public string? ShippingAddress { get; set; }
        public string? DeliveryShift   { get; set; }
        public int? DeliveryVehicleId  { get; set; }
        public string? DeliveryStatus  { get; set; }
        public DateTime? ScheduledDeliveryDate { get; set; }

        // Giờ dự kiến (chuyến giao — Nhóm C) — chỉ có khi đơn đã được gán vào 1 DeliveryTrip đang Loading/InDelivery.
        public DateTime? PlannedDepartureAt { get; set; }
        public DateTime? PlannedArrivalAt   { get; set; }
        public DateTime? DeliveredAt   { get; set; }
        public string? CustomerSignatureUrl { get; set; }
        public string? DeliveryPhotoUrl      { get; set; }
        public int FailedDeliveryCount { get; set; }
        public decimal AmountPaid      { get; set; }

        // Đổi trả
        public List<ReturnExchangeRequestSnapshotDto> ReturnExchangeRequests { get; set; } = new();

        // Hóa đơn PDF – URL Cloudinary (BR-OH-12, BR-OH-13)
        public string? InvoicePdfUrl  { get; set; }

        // Yêu cầu VAT (BR-OH-14)
        public bool   CanRequestVat   { get; set; }
        public DateTime? VatDeadline  { get; set; }

        // Hóa đơn đỏ thật (nhập bởi Sale) — chỉ đọc phía khách hàng
        public string? RedInvoiceNumber { get; set; }
        public DateTime? RedInvoiceIssuedAt { get; set; }
        public string? RedInvoiceDocumentUrl { get; set; }

        // Danh sách sản phẩm (BR-OH-08, BR-OH-09)
        public List<OrderItemDetailDto> Items { get; set; } = new();
    }

    // ─── VAT Request ─────────────────────────────────────────────────────────

    /// <summary>Yêu cầu hóa đơn VAT (BR-OH-14, BR-OH-15)</summary>
    public class VatRequestDto
    {
        public Guid   OrderId        { get; set; }
        public string TaxCode        { get; set; } = string.Empty;
        public string CompanyName    { get; set; } = string.Empty;
        public string CompanyAddress { get; set; } = string.Empty;
        public string InvoiceEmail   { get; set; } = string.Empty;
    }

    // ─── Spending Stats (Thống kê chi tiêu cá nhân) ───────────────────────────

    /// <summary>Tham số lọc cho thống kê chi tiêu cá nhân của Customer</summary>
    public class SpendingStatsQueryDto
    {
        /// <summary>Kỳ thống kê: week | month | quarter | year</summary>
        public string Period { get; set; } = "month";
    }

    /// <summary>1 điểm dữ liệu chi tiêu theo tháng (biểu đồ vùng)</summary>
    public class SpendingPointDto
    {
        public string Label { get; set; } = string.Empty;  // ví dụ "T6"
        public decimal Value { get; set; }
    }

    /// <summary>1 dòng top sản phẩm – xếp hạng theo tổng số lượng đặt</summary>
    public class TopProductDto
    {
        public string Name  { get; set; } = string.Empty;
        public int    Value { get; set; }   // tổng Quantity
    }

    /// <summary>Số liệu thống kê chi tiêu cá nhân của Customer trong 1 kỳ</summary>
    public class SpendingStatsDto
    {
        public int     TotalOrders     { get; set; }   // số đơn Paid trong kỳ
        public decimal TotalSpent      { get; set; }   // tổng FinalPayment (Paid)
        public string  TopProductName  { get; set; } = "Chưa có dữ liệu";
        public int     VatInvoiceCount { get; set; }   // đơn có RedInvoiceStatus != None

        public List<SpendingPointDto> SpendingByMonth { get; set; } = new();
        public List<TopProductDto>    TopProducts     { get; set; } = new();
    }

    // ─── Paged Result ─────────────────────────────────────────────────────────

    /// <summary>Wrapper phân trang dùng chung (NFR-OH-01)</summary>
    public class PagedResultDto<T>
    {
        public List<T> Items      { get; set; } = new();
        public int TotalCount     { get; set; }
        public int Page           { get; set; }
        public int PageSize       { get; set; }
        public int TotalPages     { get; set; }
    }
}
