namespace VietTien.API.Services.Interfaces
{
    /// <summary>
    /// Quản lý vòng đời giữ tồn cho đơn hàng: ReservedQuantity (giữ mềm lúc checkout)
    /// và AllocatedQuantity (giữ chắc khi Sales xác nhận đơn) trên Inventory.
    /// Mọi thao tác tăng đều atomic (guarded SQL UPDATE) để tránh oversell khi có nhiều
    /// request đặt hàng đồng thời cho cùng 1 SKU.
    /// </summary>
    public interface IInventoryReservationService
    {
        /// <summary>Giữ mềm tồn kho cho các sản phẩm khi khách checkout. Ném Exception nếu bất kỳ SKU nào không đủ khả dụng.</summary>
        Task ReserveAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default);

        /// <summary>Giữ chắc tồn kho (khi Sales xác nhận đơn / xác nhận thanh toán thủ công). Ném Exception nếu không đủ khả dụng.</summary>
        Task AllocateAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default);

        /// <summary>Trả lại phần ReservedQuantity đã giữ (khi đơn bị hủy/hết hạn trước khi được xác nhận).</summary>
        Task ReleaseReservedAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default);

        /// <summary>Trả lại phần AllocatedQuantity đã giữ (khi đơn đã xác nhận nhưng sau đó bị hủy).</summary>
        Task ReleaseAllocatedAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default);

        /// <summary>
        /// Trừ thẳng OnHandQuantity (bán hàng trực tiếp tại quầy — hàng rời kho ngay, không qua Reserve/Allocate).
        /// Atomic guarded UPDATE giống các thao tác khác để tránh oversell khi nhiều giao dịch tranh chấp cùng SKU.
        /// Ném Exception nếu bất kỳ SKU nào không đủ khả dụng.
        /// </summary>
        Task DeductOnHandAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default);
    }
}
