namespace VietTien.API.Services.Interfaces
{
    /// <summary>
    /// Hàng rào phân quyền theo kho được phân công (User.AssignedWarehouseId) cho WarehouseStaff.
    ///
    /// SRS NAC-05: "operate outside the assigned warehouse/role -> HTTP 403 WAREHOUSE_ACTION_FORBIDDEN
    /// ... preserves audit history". Trước đây pattern này bị copy-paste ở 4 service và bỏ sót ở
    /// GoodsIssueService, InventoryCountSessionService (ghi số đếm/đóng phiên) và luồng Quarantine.
    /// Gom về một chỗ để không còn nơi nào ghi tồn kho mà quên kiểm tra.
    /// </summary>
    public interface IWarehouseAccessGuard
    {
        /// <summary>
        /// Ném <see cref="UnauthorizedAccessException"/> (middleware map -> 403) nếu caller là
        /// WarehouseStaff nhưng <c>warehouseId</c> không phải kho được phân công của họ.
        ///
        /// Luôn gọi TRƯỚC khi mở transaction và trước mọi thay đổi state: hàm này ghi AuditLog
        /// bằng một DbContext riêng nên không bị cuốn theo rollback của caller, nhưng đặt guard
        /// lên đầu vẫn là cách duy nhất đảm bảo không có side effect nào lọt qua.
        /// </summary>
        /// <param name="staffId">Id người gọi lấy từ JWT.</param>
        /// <param name="warehouseId">Kho mà thao tác sẽ tác động lên.</param>
        /// <param name="action">Mô tả thao tác, dùng cho thông điệp lỗi và AuditLog. VD: "xuất kho".</param>
        /// <param name="entityName">Loại tài nguyên ghi vào AuditLog. VD: "GoodsIssue".</param>
        /// <param name="entityId">Id tài nguyên ghi vào AuditLog (nếu đã biết).</param>
        Task EnsureWarehouseAccessAsync(
            Guid staffId,
            Guid warehouseId,
            string action,
            string entityName,
            string? entityId = null);

        /// <summary>
        /// Dùng để LỌC danh sách (thay vì chặn như <see cref="EnsureWarehouseAccessAsync"/>).
        /// Quy ước giá trị trả về:
        ///   - <c>null</c>: caller không bị giới hạn phạm vi (CEO/Admin/SalesManager) -> không thêm bộ lọc.
        ///   - một Guid: caller là WarehouseStaff -> chỉ lấy bản ghi thuộc kho này.
        ///   - <see cref="Guid.Empty"/>: WarehouseStaff chưa được gán kho -> không khớp bản ghi nào
        ///     (đóng mặc định, thay vì lộ toàn bộ dữ liệu như khi trả null).
        /// </summary>
        Task<Guid?> GetScopedWarehouseIdAsync(Guid callerId);
    }
}
