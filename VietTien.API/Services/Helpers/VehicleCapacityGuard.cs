using VietTien.API.Exceptions;
using VietTien.API.Models;

namespace VietTien.API.Services.Helpers
{
    // Dùng chung cho mọi luồng xếp xe (chuyến giao hàng, điều chuyển nội bộ, thu hồi hàng trả về) —
    // 1 nguồn chân lý cho message + ngưỡng chặn vượt tải, tránh lệch logic giữa các luồng.
    public static class VehicleCapacityGuard
    {
        // Chặn cứng: không cho gán thêm hàng vào xe nếu tổng trọng lượng vượt Vehicle.Capacity.
        // Hàng chưa có trọng lượng (chưa đóng gói / sản phẩm chưa cấu hình WeightKg) tính là 0kg —
        // không chặn, chỉ đơn giản chưa đóng góp vào tổng (an toàn, không suy diễn).
        public static void EnsureWithinCapacity(Vehicle vehicle, decimal currentWeightKg, decimal addingWeightKg, List<string> addingCodes)
        {
            if (vehicle.Capacity == null) return;

            var newTotal = currentWeightKg + addingWeightKg;
            if (newTotal > vehicle.Capacity.Value)
            {
                throw new VehicleOverweightException(
                    $"Xe {vehicle.VehicleNumber} chỉ chở tối đa {vehicle.Capacity.Value:N0}kg, hiện đã có {currentWeightKg:N0}kg. " +
                    $"Thêm {string.Join(", ", addingCodes)} ({addingWeightKg:N0}kg) sẽ vượt tải trọng. Vui lòng chọn xe/chuyến khác.");
            }
        }
    }
}
