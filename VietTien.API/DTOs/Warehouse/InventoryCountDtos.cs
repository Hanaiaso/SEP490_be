namespace VietTien.API.DTOs.Warehouse
{
    // INV-01: DTO còn lại cho endpoint tương thích ngược `POST /api/inventory/count-sessions/{id}/lines`
    // (giữ đúng route/DTO cũ để L3-INV-01/02 không vỡ) sau khi hợp nhất về InventoryCountSessionService
    // (DEF-L4-003) — xem InventoryController.RecordCountLine.
    public class RecordCountLineRequestDto
    {
        public Guid InventoryId { get; set; }

        // Không dùng [Range] ở đây: âm phải trả cùng mã lỗi COUNT_LINE_INVALID qua controller,
        // giống cách COD_AMOUNT_INVALID được xử lý ở luồng DeliveryTrip.
        public int ActualQuantity { get; set; }
    }
}
