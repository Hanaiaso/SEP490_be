# Hướng dẫn chạy Unit Test (L1) — Backend

Áp dụng cho `VietTien.Tests`, đối chiếu theo doc `Report_5_2_L1-UnitTests_VietTien_v2_3.xlsx` (390 case).
Hướng dẫn cho Frontend nằm ở `SEP490_fe/frontend/HUONG_DAN_CHAY_UNIT_TEST.md`.

---

## 1. Chuẩn bị (chỉ làm 1 lần)

### Yêu cầu
- .NET SDK 9.0 (`dotnet --version`)

### ⚠ KHÔNG cần SQL Server để chạy unit test

Toàn bộ test L1 chạy trên DB in-memory:
- **EF Core InMemory** (`TestHelpers/TestDbFactory.cs`) — dùng cho hầu hết test.
- **SQLite in-memory** (`TestHelpers/SqliteDbFactory.cs`) — chỉ dùng cho `InventoryReservationServiceTests`,
  vì service này gọi raw SQL (`ExecuteSqlInterpolatedAsync`) mà provider InMemory không hỗ trợ.

Nghĩa là **`dotnet test` chạy được ngay cả khi chưa dựng database**. Connection string trong
`appsettings.Development.json` chỉ cần cho việc **chạy app** (`dotnet run`), không cần cho test.

### File cấu hình bí mật (bắt buộc thêm THỦ CÔNG)

`VietTien.API/appsettings.Development.json` chứa API key thật nên **đã được `.gitignore`**
(dòng 34) và **không bao giờ được commit**. Sau khi clone/pull, tự tạo file này theo mẫu:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=VietTien22;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "JwtSettings":        { "SecretKey": "<xin trong nhóm>" },
  "EmailSettings":      { "SenderPassword": "<xin trong nhóm>" },
  "SePaySettings":      { "ApiToken": "<xin trong nhóm>" },
  "CloudinarySettings": { "ApiKey": "<xin trong nhóm>", "ApiSecret": "<xin trong nhóm>" },
  "eSMS":               { "ApiKey": "<xin trong nhóm>", "SecretKey": "<xin trong nhóm>" },
  "GeminiSettings":     { "ApiKey": "<xin trong nhóm>" },
  "MakeCom":            { "WebhookUrl": "<xin trong nhóm>" }
}
```

Kiểm tra file không bị lọt vào git:

```bash
git check-ignore -v VietTien.API/appsettings.Development.json
```

Lệnh trên phải in ra `.gitignore:34:appsettings.Development.json`. Nếu không in gì → **dừng lại**,
file đang bị git theo dõi và sẽ bị push kèm key.

---

## 2. Chạy test

Chạy toàn bộ:

```bash
dotnet test D:\SEP490\SEP490_be\VietTien.Tests\VietTien.Tests.csproj
```

Chạy gọn (chỉ in kết quả, bỏ log build):

```bash
dotnet test D:\SEP490\SEP490_be\VietTien.Tests\VietTien.Tests.csproj --nologo -v q
```

Chạy 1 nhóm test (theo tên class):

```bash
dotnet test D:\SEP490\SEP490_be\VietTien.Tests\VietTien.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests"
```

Chạy đúng 1 case theo Test ID trong doc:

```bash
dotnet test D:\SEP490\SEP490_be\VietTien.Tests\VietTien.Tests.csproj --filter "FullyQualifiedName~L1_ORD_73"
```

Xem chi tiết vì sao 1 test đỏ (in cả Expected/Actual):

```bash
dotnet test D:\SEP490\SEP490_be\VietTien.Tests\VietTien.Tests.csproj --filter "FullyQualifiedName~L1_ORD_73" -v n
```

---

## 3. ⚠ KẾT QUẢ MONG ĐỢI — 28 test ĐỎ là ĐÚNG, đừng "sửa" chúng

```
Failed!  -  Failed: 28,  Passed: 360,  Skipped: 12,  Total: 400
```

**28 test đỏ này cố tình assert theo SPEC (SRS v2), trong khi code hiện tại chưa đáp ứng.**
Chúng là "phiếu báo lỗi sống": khi ai đó sửa code cho đúng spec, test sẽ tự chuyển xanh.

Nhận diện: mỗi test đỏ đều có comment `🔴 DEFECT CANDIDATE` hoặc `🔴 SPEC GAP` ngay phía trên
`[Fact]`/`[Theory]`, kèm giải thích và mã SRS liên quan.

| Test ID | Vấn đề tóm tắt |
|---|---|
| `L1-SJOB-11` | LowStockAlertJob dùng `<` thay vì `<=` ở ngưỡng tồn kho |
| `L1-ORD-73` | OrderService hard-code `>= 3`, bỏ qua `DELIVERY_FAILURE_MANAGER_THRESHOLD` |
| `L1-AUTH-17` | OTP SMS lưu thô, không băm |
| `L1-AUTH-18` | Không chặn resend OTP trước 60 giây |
| `L1-AUTH-20` | Không có rate limit số lần gửi OTP |
| `L1-AUTH-23` | Không đếm số lần nhập sai OTP → brute-force được |
| `L1-ORD-51` | IDOR: tạo được yêu cầu đổi/trả trên đơn của khách khác |
| `L1-ORD-67` | Tra cứu công khai lộ SĐT + địa chỉ đầy đủ |
| `L1-REG-01` | IDOR: sửa được tồn kho ngoài kho được gán |
| `L1-REG-06` | Huỷ được đơn đang trên đường giao |
| `L1-REG-07` | Đơn đã Paid vẫn sinh QR mới → khách chuyển khoản lần 2 |
| `L1-MKT-04` | Bài marketing Draft vẫn đăng thẳng lên Facebook |
| `L1-MKT-12` | Callback đẩy bài Draft nhảy thẳng sang Success |
| `L1-ADM-05` | Hạ vai trò Sales làm khách mất người phụ trách |
| `L1-ADM-07` | Khoá Sales cuối cùng → không còn ai nhận khách mới |
| `L1-SA-08` | Mã giới thiệu của Sales đã nghỉ vẫn gán được khách |
| `L1-CFG-04` | Đổi cấu hình hồi tố không bị chặn |
| `L1-DT-07` | Bậc chiết khấu chồng lấn không bị chặn |
| `L1-VEH-03` | Trùng biển số xe không bị chặn |
| `L1-AUD-04` | Audit log không mask SĐT / mã số thuế |
| `L1-AUTH-26` | ResendOtp lộ email nào đã đăng ký (user enumeration) |
| `L1-AUTH-28` | OTP email lưu thô, không băm |
| `L1-AUTH-29` | Không chặn resend OTP email trước 60 giây |
| `L1-AUTH-30` | Không có rate limit 5 lần/30 phút |
| `L1-AUTH-31` | Không có rate limit 10 lần/ngày |
| `L1-ORD-74` | Webhook trả THIẾU → `return` im lặng, không ai biết có giao dịch treo |
| `L1-ORD-75` | Webhook trả THỪA vẫn set Paid (SRS yêu cầu khớp chính xác) |
| `L1-ORD-77b` | Tiền VAT/chiết khấu không làm tròn → sinh số lẻ nhỏ hơn 1 đồng |

Chi tiết đầy đủ (vị trí code, mã SRS, cách sửa): **`VietTien.Tests/DOC_MISMATCHES.md`**.

> **Nếu cần suite XANH hoàn toàn cho CI**: đổi 28 chỗ đó thành `[Fact(Skip = "🔴 chờ sửa code — xem DOC_MISMATCHES.md")]`.
> Chỉ làm khi cả nhóm đã chốt, vì khi Skip thì bug tái xuất sẽ không còn ai báo.

**12 test Skipped** là các case đã bị đánh Skip từ trước đợt này (không liên quan).

---

## 4. Cấu trúc thư mục test

```
VietTien.Tests/
├── Services/              # 1 file / 1 service. OrderServiceTests tách thành 5 file partial
│   ├── OrderServiceTests.cs                  (ORD-01..47)
│   ├── OrderServiceTests.ReturnExchange.cs   (ORD-48..60)
│   ├── OrderServiceTests.Scoping.cs          (ORD-61..70)
│   ├── OrderServiceTests.DeliveryFailure.cs  (ORD-72..73)
│   ├── OrderServiceTests.Money.cs            (ORD-74..76 webhook, ORD-77 VAT)
│   └── OrderServiceTests.Regression.cs       (REG-06, REG-07)
├── ScheduledJobs/         # 1 file / 1 job  (SJOB-01..16)
├── Integrations/          # ExternalIntegrationsTests  (EXT-01..08)
├── Regression/            # RegressionFixesTests  (REG-01..12)
├── TestHelpers/           # TestDbFactory, SqliteDbFactory, TestData, FakeHttpMessageHandler...
└── DOC_MISMATCHES.md # Bảng đối chiếu doc ↔ code
```

---

## 5. Quy ước khi viết test mới

1. **Mỗi test phải mang Test ID của doc** trong comment ngay trên `[Fact]`/`[Theory]`:
   ```csharp
   // L1-ORD-48 | EP-Valid | Mô tả ngắn điều đang kiểm chứng
   [Fact]
   public async Task L1_ORD_48_TenNgan() { ... }
   ```
   Mã này là thứ duy nhất nối test với case trong Excel — thiếu là coi như case chưa làm.
   Tên method cũng phải mang mã (`L1_ORD_48_...`) để `--filter` chạy được đúng 1 case.

2. **Không mock clock**: codebase **không có** `IClock`/`ITimeProvider`, job dùng `DateTime.UtcNow`
   trực tiếp. Muốn test mốc thời gian thì **back-date** `CreatedAt` / `ExpiryDate` của entity seed:
   ```csharp
   o.CreatedAt = DateTime.UtcNow.AddMinutes(-14);   // đúng, mô phỏng "14 phút trước"
   ```

3. **Ngưỡng cấu hình phải seed qua SystemConfig**, không hard-code:
   ```csharp
   TestData.SeedConfig(_db, "SEPAY_RESERVATION_MINUTES", "15");
   ```

4. **Service gọi raw SQL** → dùng `SqliteDbFactory.Create()` và nhớ dispose cả connection
   (xem `InventoryReservationServiceTests` làm mẫu). Còn lại dùng `TestDbFactory.Create()`.

5. **Case không test được ở tầng L1** (phân quyền nằm ở `[Authorize]` của Controller, hoặc tính năng
   chưa tồn tại) → **đừng viết test yếu**. Ghi chú Blocked trong `<summary>` của class và bổ sung vào
   `DOC_MISMATCHES.md`. Hiện có 11 case như vậy.

---

## 6. Đối chiếu Test ID doc ↔ code

Kết quả lần đối chiếu 01/08/2026 với doc **v2.3**: **390/390 case đã có test, 0 case Not Run.**
Trạng thái từng case xem `VietTien.Tests/L1_status_v2.3_2026-08-01.csv`.

Khi cần đối chiếu lại bằng tay, lưu ý 3 điểm dễ đếm sai:

1. **1 Test ID ≠ 1 test chạy.** `[Theory]`/`it.each` nhiều dòng đếm thành nhiều test nhưng chỉ là
   1 case trong Excel. Chỉ cần 1 nhánh đỏ thì cả case ghi **Fail**.
   (Vd `L1-SJOB-11` là 1 case nhưng chạy ra 3 test cho 3 mốc BVA.)
2. **`.trx` ghi test bị Skip là `NotExecuted`**, không phải `Skipped` — dễ đếm nhầm thành Pass.
3. **Test ID có hậu tố chữ** (`L1-ORD-77b`) là case RIÊNG, đừng gộp vào `L1-ORD-77`.

Đối chiếu nhanh xem case nào chưa có test — tìm mã trong comment/tên test:

```bash
grep -rn "L1-AUTH-29" D:\SEP490\SEP490_be\VietTien.Tests D:\SEP490\SEP490_fe\frontend\src
```

---

## 7. Xem Test Coverage (độ phủ code)

### Cài một lần

```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
```

`coverlet.collector` đã có sẵn trong `VietTien.Tests.csproj`, không cần cài thêm.

### Chạy

Thu thập dữ liệu (chạy 1 lần, dùng cho cả 2 báo cáo):

```bash
dotnet test D:\SEP490\SEP490_be\VietTien.Tests\VietTien.Tests.csproj --collect:"XPlat Code Coverage" --results-directory D:\SEP490\SEP490_be\VietTien.Tests\TestResults
```

> `dotnet test` trả exit code ≠ 0 vì có 28 test đỏ — **file coverage vẫn được sinh bình thường**, cứ chạy tiếp.
> ⚠ Thư mục `TestResults` tích luỹ file cũ từ các lần chạy trước. **Phải lấy file MỚI NHẤT**,
> nếu dùng wildcard `**\coverage.cobertura.xml` sẽ trộn nhầm dữ liệu cũ.

Sinh báo cáo (PowerShell — tự lấy file mới nhất):

```powershell
$cov = Get-ChildItem "D:\SEP490\SEP490_be\VietTien.Tests\TestResults" -Recurse -Filter "coverage.cobertura.xml" | Sort-Object LastWriteTime -Descending | Select-Object -First 1; reportgenerator "-reports:$($cov.FullName)" "-targetdir:D:\SEP490\SEP490_be\VietTien.Tests\TestResults\cov-l1" "-reporttypes:Html;TextSummary" "-filefilters:-*\Migrations\*;-*\DTOs\*;-*\Controllers\*;-*\Hubs\*;-*Program.cs;-*ApplicationDbContext.cs"
```

Mở báo cáo:

```bash
start D:\SEP490\SEP490_be\VietTien.Tests\TestResults\cov-l1\index.html
```

### Kết quả đo 02/08/2026

| Phạm vi | Line coverage | Branch coverage | Dòng đo được |
|---|---|---|---|
| **Toàn bộ `VietTien.API`** (số thô) | **16,2%** | 43,1% | 54.978 |
| **Phạm vi L1** (đã lọc) | **69,0%** | 50,2% | 10.389 |

### Vì sao chênh lệch — phải giải thích khi báo cáo

Bỏ wildcard lọc thì `Migrations/` (**40.984 dòng, chiếm 61% code BE**) bị tính vào mẫu số.
Đó là code EF **auto-generated**, không ai viết tay và không có logic để test.

| Loại trừ khỏi số L1 | Dòng | Lý do |
|---|---|---|
| `Migrations/` | 40.984 | Auto-generated |
| `Controllers/` | 5.303 | Phạm vi **L3 System/API Test** — cũng là lý do 7 case bị đánh Blocked |
| `DTOs/` | 3.240 | Thuần property, không có nhánh rẽ |
| `Hubs/`, `Program.cs`, `ApplicationDbContext.cs` | ~1.300 | Hạ tầng/cấu hình, kiểm chứng ở L2 |

**Phần được tính**: `Services` + `Repositories` + `Infrastructure` + `Models`.

### Cách đọc cho đúng

1. **Coverage cao ≠ test tốt.** Bản cũ của `L1-EXT-08` chạy qua code nên được tính "đã phủ",
   nhưng assertion luôn đúng → test vô giá trị. Coverage chỉ trả lời *code nào chưa từng chạy*.
2. **28 test đỏ vẫn tính vào coverage** vì chúng vẫn thực thi code trước khi assert thất bại.
3. **Giá trị thật nằm ở trang chi tiết**: mở `index.html` → bấm cột *Line coverage* để sắp tăng dần
   → file 0% là chỗ chưa ai test. Chính cách này từng phát hiện `ForgotPasswordAsync` không có test nào.
4. **Branch coverage (50,2%) đáng quan tâm hơn line coverage.** Nó nói rằng gần một nửa nhánh
   if/else chưa được đi qua — đó mới là chỗ bug hay nấp.

---

## 8. Lỗi thường gặp

| Triệu chứng | Nguyên nhân & cách xử lý |
|---|---|
| `Failed: 28` | **Bình thường** — xem mục 3, đừng sửa |
| `NU1902 ... MailKit/MimeKit vulnerability` | Cảnh báo package, không ảnh hưởng test. Bỏ qua |
| Test `InventoryReservationServiceTests` báo lỗi SQL | Thiếu package `Microsoft.EntityFrameworkCore.Sqlite` → chạy `dotnet restore` |
| `Could not find appsettings.Development.json` khi `dotnet run` | Chưa tạo file cấu hình → xem mục 1. (Không ảnh hưởng `dotnet test`) |
| Test đỏ ngoài danh sách ở mục 3 | Đây là **lỗi thật do code mới**. Chạy `-v n` để xem Expected/Actual |
