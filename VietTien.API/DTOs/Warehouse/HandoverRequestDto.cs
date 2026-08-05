using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class HandoverRequestDto
    {
        // Có thể là base64 data URI ("data:image/...") mới upload hoặc URL đã upload từ trước
        // (xem WarehouseController.ConfirmHandover) -> chỉ giới hạn độ dài, không ép định dạng cụ thể.
        [MaxLength(5_000_000, ErrorMessage = "Chữ ký điện tử vượt quá kích thước cho phép.")]
        public string? WarehouseSignature { get; set; }

        [MaxLength(5_000_000, ErrorMessage = "Chữ ký điện tử vượt quá kích thước cho phép.")]
        public string? SalesSignature { get; set; }
    }
}
