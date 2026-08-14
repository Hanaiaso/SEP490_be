-- Fix: đồng bộ __EFMigrationsHistory với migration 20260814060706_AddDeliveryScheduleConflicts
-- Chỉ áp dụng cho trường hợp bảng DeliveryScheduleConflicts đã tồn tại thật trên DB
-- nhưng __EFMigrationsHistory chưa ghi nhận migration này (giống lỗi đã gặp trên DB dev).
-- Nếu bảng CHƯA tồn tại, script không làm gì cả -> lần app khởi động kế tiếp,
-- EF Core Migrate() sẽ tự tạo bảng bình thường, không cần can thiệp.

IF OBJECT_ID('dbo.DeliveryScheduleConflicts', 'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM [__EFMigrationsHistory]
        WHERE [MigrationId] = '20260814060706_AddDeliveryScheduleConflicts'
   )
BEGIN
    PRINT 'Bang DeliveryScheduleConflicts da ton tai, nhung migration chua duoc ghi nhan. Dang chen dong danh dau...';

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20260814060706_AddDeliveryScheduleConflicts', '8.0.12');

    PRINT 'Da chen thanh cong.';
END
ELSE IF OBJECT_ID('dbo.DeliveryScheduleConflicts', 'U') IS NULL
BEGIN
    PRINT 'Bang DeliveryScheduleConflicts chua ton tai. Khong can lam gi - EF Migrate() se tu tao khi app khoi dong.';
END
ELSE
BEGIN
    PRINT 'Migration da duoc ghi nhan san roi. Khong can lam gi.';
END
