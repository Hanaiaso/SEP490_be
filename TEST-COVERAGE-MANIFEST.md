# TEST COVERAGE MANIFEST — đợt bổ sung test code-driven

> File này gom đủ thông tin để ra prompt cập nhật 2 workbook:
> `Report_5_2_L1-UnitTests_VietTien_v2_4.xlsx` và `Report_5_2_L2-IntegrationTests_VietTien_v2_4.xlsx`.
> Ngày đo: 06/08/2026.

---

## PHẦN D — Số liệu coverage (đọc trước để điền ô tổng kết)

Mẫu số đã chốt (khai báo trong `coverlet.runsettings`): **13.809 dòng khả phủ** — gồm Services,
Controllers, Repositories, ScheduledJobs, Infrastructure, Data, Hubs. **Loại khỏi mẫu số**:
DTO/Model (auto-property), Migrations (code sinh tự động), `Program` (chỉ đăng ký DI, chạy trọn ở mọi
test host nên tính vào sẽ đẩy % lên giả tạo), và 6 adapter IO ngoài (`EmailService`,
`CloudinaryService`, `eSmsService`, `AiGeneratorService`, `MakeWebhookService`, `GoogleTokenValidator`
— 969 dòng, **không có seam để mock**, phải refactor `VietTien.API` mới test được).

| Chỉ số | Baseline | Đợt 1 | Đợt 2 | Đợt 3 | Đợt 4 (vá migration) | **Đợt 5 (nhắm branch)** |
|---|---|---|---|---|---|---|
| **Line coverage** | 57,8% | 60,6% | 68,9% | 77,6% | 78,3% | **80,6%** |
| Dòng được phủ | 7.987 | 8.382 | 9.528 | 10.721 | 10.826 | **11.137** |
| **Branch coverage** | 45,3% | 46,6% | 51,4% | 56,7% | 57,5% | **62,5%** |
| Test L1 (VietTien.Tests) | 411 | 491 | 734 | 1.130 | 1.130 | **1.253** |
| Test L2 **xanh** | 53 | 47 | 47 | 47 | 92 | 92 |

Tổng 5 đợt: **+22,8 điểm line · +17,2 điểm branch · +3.150 dòng · +842 case L1 · +45 test L2 được cứu.**

### Đợt 5 — vì sao branch tụt lại phía sau line, và cách kéo lên

**Line coverage** hỏi "dòng này có chạy không". **Branch coverage** hỏi "tại mỗi ngã rẽ (`if`, `? :`,
`&&`, `||`, `??`, `?.`, `switch`), **cả hai** hướng có cùng chạy không". Một lớp có thể line 100% mà
branch 58%.

Ví dụ có thật — khuôn lặp 6 lần trong `NotificationsController` (line 95,1% / branch 72%):

```csharp
var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
    return Unauthorized();
```

Bộ test cũ phủ "claim hợp lệ" và "không có claim". Nhưng khi không có claim thì `IsNullOrEmpty` trả
true và `||` **đoản mạch** — `Guid.TryParse` không bao giờ chạy. Nhánh thiếu là **"có claim nhưng
không phải Guid"**. Helper `WithMalformedUserId` trong `ControllerTestHelper` sinh ra để chạm nó.

Ba khuôn đã vá ở đợt 5 (`VietTien.Tests/Controllers/ControllerBranchGapTests.cs`, 45 case):
1. Claim rác (`WithMalformedUserId`) — 10 controller.
2. Claim phụ chưa từng set: `ClaimTypes.Email` và `RemoteIpAddress` luôn null nên nhánh audit log
   thật chưa chạy (`WithEmailClaim`, `WithRemoteIp`).
3. Fallback `?? User.FindFirst("sub")` — đường mà token Google đi qua (`WithSubClaimOnly`).
4. Biên phân trang + danh sách rỗng.

Kết quả từng lớp:

| Lớp | Branch trước | Branch sau |
|---|---|---|
| `InventoryService` | **17,7%** | **76,1%** (line 35,6% → 90%) |
| `SalesChangeRequestService` | 27,7% | 58,3% (line 45,8% → 64,2%) |
| `AdminSystemConfigController` | 83,3% | **100%** |
| `CustomerProfileController` | ~90% | **100%** |
| `VehiclesController` · `DiscountTiersController` · `AdminUsersController` | 58,3% | 83,3% |
| `NotificationsController` | 72% | 84% |

**Còn lại toàn ở tầng Service** (1.447 nhánh chưa phủ): `OrderService` 385 · `WarehouseService` 161
· `StockTransferService` 117 · `GoodsIssueService` 67 · `SalesChangeRequestService` 60 ·
`MarketingPostService` 56 · `InventoryService` 51 · `QuotationService` 48 · `AuthService` 45 ·
`ManualPaymentService` 41.

Kết quả chạy (`dotnet test VietTien.sln`) sau đợt 4:
- `VietTien.Tests`: **1.127 pass / 1 fail / 2 skip** — case đỏ duy nhất là `L1_ST_04_Dispatch_NonDraft_Rejected`,
  test cũ bị lệch chuỗi thông điệp sau commit `02b036a` (xem E2), **không phải** case mới.
- `VietTien.IntegrationTests`: **92 pass / 18 fail** (trước: 47/62). 18 case đỏ còn lại **đều là defect
  nghiệp vụ thật** đã ghi nhận (GH-01/06/08/09/10/12/13/14/15) — **không còn case nào đỏ vì schema**.

⚠ Vì sao vá migration chỉ thêm 0,7 điểm dù cứu được 45 test L2: phần code mà 45 test đó chạy qua
**phần lớn đã được 719 case L1 của đợt 2–3 phủ trước rồi**. Giá trị thật của đợt 4 không nằm ở con số
coverage mà ở chỗ **4 luồng nghiệp vụ trước đây không chạy được khi triển khai thì nay chạy được**.

### Coverage tầng Controller sau đợt 3 — **31/31 controller đều ≥ 93%**

| 100% (18 controller) | 93–99% (13 controller) |
|---|---|
| AdminSystemConfig, AuditLog, Auth, Cart, CustomerProfile, Delivery, GoodsIssue, Handover, Inventory, MarketingPost, Payments, Quotation, SalesChangeRequests, Sales, Suppliers, UserProfile, Warehouse, WarehouseShift | Order 99 · PurchaseOrder 98,6 · StockTransfer 98,2 · SystemHealth 98,2 · Dashboards 97,9 · AdminUsers 98,5 · Product 96,1 · SePay 96 · Material 95,3 · Notifications 95,1 · Vehicles 93,8 · DiscountTiers 93,4 · WarehouseManagement 93 |

**Tầng Controller coi như đã xong.** Hạ tầng cũng vậy:

| Thành phần | Trước | Sau |
|---|---|---|
| `ExceptionHandlingMiddleware` | 20,4% | **100%** |
| `ChatHub` / `NotificationHub` / `SalesHub` / `WarehouseHub` | 0% | **100%** cả 4 |
| `ScheduledJobRunnerBackgroundService` | 0% | **94,4%** |

**Cảnh báo kỹ thuật khi đo lại:** tuyệt đối **không** thêm `CompilerGeneratedAttribute` vào
`ExcludeByAttribute`. Trình biên dịch C# dịch thân của **mọi hàm `async`** thành state-machine class
mang attribute đó — loại nó đi thì `OrderService` chỉ còn 64/2.900 dòng được đo và báo "100%".
Lần đo đầu tiên đã mắc đúng lỗi này và ra con số ảo 82,9%.

---

## PHẦN A — Case mới cho workbook L1

Cột workbook L1: `Test ID | Class (Sheet) | Method / Block | SRS Reference | Priority | Negative? | Status | Defect ID`

Quy ước đã chốt: **chèn vào sheet sẵn có theo class**, tiếp nối đúng prefix + số thứ tự của sheet đó.
Cột `SRS Reference` ghi **`COVERAGE (code-driven)`** để phân biệt với case truy vết SRS.
**Tất cả 54 case dưới đây đều `Status = Pass`, `Defect ID` để trống.**

### Sheet `ManualPaymentService` — 10 case (tiếp từ L1-MP-05)
File: `VietTien.Tests/Controllers/PaymentsControllerTests.cs` — phủ `PaymentsController` 0% → **100%**

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-MP-06 | PaymentsController.GetSePayExceptions() — trả 200 kèm danh sách | P2 | No |
| L1-MP-07 | GetSePayExceptions() — service ném Exception → 400 INTERNAL_ERROR | P2 | Yes |
| L1-MP-08 | ManualConfirm() — thành công → 200 kèm response | P1 | No |
| L1-MP-09 | ManualConfirm() — ModelState hỏng → 400, không gọi service | P1 | Yes |
| L1-MP-10 | ManualConfirm() — map exception → status + tách mã lỗi [Theory: 404/400/409] | P1 | Yes |
| L1-MP-11 | ManualConfirm() — UnauthorizedAccessException → 401 | P1 | Yes |
| L1-MP-12 | ManualConfirm() — Exception ngoài dự kiến → 500 | P1 | Yes |
| L1-MP-13 | ManualConfirm() — thiếu claim user → 401, không gọi service | P1 | Yes |
| L1-MP-14 | RetryAllocation() — thành công, truyền đúng note xuống service | P2 | No |
| L1-MP-15 | RetryAllocation() — map exception → status [Theory: 404/409/401/500] | P1 | Yes |

### Sheet `SupplierService` — 7 case (tiếp từ L1-SUP-03)
File: `VietTien.Tests/Controllers/CrudControllersTests.cs` — phủ `SuppliersController` 0% → **100%**

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-SUP-04 | SuppliersController.GetAll() — 200 kèm danh sách | P2 | No |
| L1-SUP-05 | GetById() — tìm thấy → 200 | P2 | No |
| L1-SUP-06 | GetById() — map exception [Theory: 404/400] | P2 | Yes |
| L1-SUP-07 | Create() — thành công → 200 | P2 | No |
| L1-SUP-08 | Create() — map exception [Theory: 409/400] | P2 | Yes |
| L1-SUP-09 | Update() — thành công → 200 | P2 | No |
| L1-SUP-10 | Update() — map exception [Theory: 404/409/400] | P2 | Yes |

### Sheet `VehicleService` — 5 case (tiếp từ L1-VEH-05)
File: `CrudControllersTests.cs` — phủ `VehiclesController` 0% → **93,8%**

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-VEH-06 | VehiclesController.GetAll() — 200 | P2 | No |
| L1-VEH-07 | GetById() — map exception [Theory: 404/400] | P2 | Yes |
| L1-VEH-08 | Create() — thành công → 200 | P2 | No |
| L1-VEH-09 | Create() — map exception [Theory: 409/400] | P2 | Yes |
| L1-VEH-10 | Update() — map exception [Theory: 404/400] | P2 | Yes |

### Sheet `DiscountTierService` — 5 case (tiếp từ L1-DT-09)
File: `CrudControllersTests.cs` — phủ `DiscountTiersController` 0% → **93,4%**

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-DT-10 | DiscountTiersController.GetAll() — 200 | P2 | No |
| L1-DT-11 | GetById() — map exception [Theory: 404/400] | P2 | Yes |
| L1-DT-12 | Create() — thành công → 200 | P2 | No |
| L1-DT-13 | Create() — service ném Exception → 400 | P2 | Yes |
| L1-DT-14 | Update() — map exception [Theory: 404/400] | P2 | Yes |

### Sheet `MaterialService` — 8 case (tiếp từ L1-MAT-03)
File: `CrudControllersTests.cs` — phủ `MaterialController` 0% → **95,3%**

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-MAT-04 | MaterialController.GetAll() — truyền đúng tham số tìm kiếm xuống service | P2 | No |
| L1-MAT-05 | GetById() — map exception [Theory: 404/400] | P2 | Yes |
| L1-MAT-06 | Create() — ModelState hỏng → 400, không gọi service | P1 | Yes |
| L1-MAT-07 | Create() — map exception [Theory: 409/400] | P2 | Yes |
| L1-MAT-08 | Update() — ModelState hỏng → 400 | P1 | Yes |
| L1-MAT-09 | Update() — map exception [Theory: 404/409/400] | P2 | Yes |
| L1-MAT-10 | Delete() — thành công → 204 NoContent | P2 | No |
| L1-MAT-11 | Delete() — map exception [Theory: 404/409/400] | P2 | Yes |

### Sheet `StockTransferService` — 13 case (tiếp từ L1-ST-06)
File: `VietTien.Tests/Controllers/StockTransferAndSePayControllerTests.cs` — phủ `StockTransferController` 0% → **68,3%**

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-ST-07 | StockTransferController.GetAll() — 200 | P2 | No |
| L1-ST-08 | GetAll() — service ném Exception → 400 | P2 | Yes |
| L1-ST-09 | GetById() — map exception [Theory: 404/400] | P2 | Yes |
| L1-ST-10 | Create() — thiếu claim user → 401, không gọi service | P1 | Yes |
| L1-ST-11 | Create() — thành công → 201, gắn đúng người tạo từ JWT | P1 | No |
| L1-ST-12 | Create() — map exception [Theory: 404/400] | P2 | Yes |
| L1-ST-13 | Update() — **DbUpdateConcurrencyException → 409** | P1 | Yes |
| L1-ST-14 | Update() — map exception [Theory: 404/400] | P2 | Yes |
| L1-ST-15 | Dispatch() — thành công → 200 | P1 | No |
| L1-ST-16 | Dispatch() — map exception [Theory: 404/409/400] | P1 | Yes |
| L1-ST-17 | Cancel() — map exception [Theory: 404/400] | P2 | Yes |
| L1-ST-18 | Cancel() — thành công → 200 | P2 | No |
| L1-ST-19 | RequestTransport() — thành công → 200 | P2 | No |

> Ghi chú đáng đưa vào cột Notes của L1-ST-13/L1-ST-16: nhánh `DbUpdateConcurrencyException → 409`
> tồn tại ở 4 action nhưng **không bao giờ chạy được qua HTTP thật** vì bảng `Inventories` không có
> `RowVersion` (defect GH-04). Mock service là cách duy nhất chạm tới.

### Sheet `OrderService` — 6 case (tiếp từ L1-ORD-76)
File: `StockTransferAndSePayControllerTests.cs` — phủ `SePayController` 0% → **96%**
(SePayController uỷ quyền cho `IOrderService.ProcessSePayWebhookAsync`)

| Test ID | Method / Block | Priority | Negative? |
|---|---|---|---|
| L1-ORD-77 | SePayController.Webhook() — token ở header `x-sepay-token` → 200, log Processed | P1 | No |
| L1-ORD-78 | Webhook() — đọc token từ header `Authorization` có tiền tố `Bearer` | P1 | No |
| L1-ORD-79 | Webhook() — đọc token từ header `Authorization` không tiền tố | P1 | No |
| L1-ORD-80 | Webhook() — đọc token từ query `?token=` khi không có header | P1 | No |
| L1-ORD-81 | Webhook() — token sai → 401 và **KHÔNG** đánh dấu log Failed (không cho retry) | P1 | Yes |
| L1-ORD-82 | Webhook() — lỗi xử lý → 500 và đánh dấu log Failed để job retry nhặt | P1 | Yes |

> Ghi chú cho cột Notes: **cố ý không** viết case "thiếu token" ở L1 vì kết quả phụ thuộc biến môi
> trường tiến trình `ASPNETCORE_ENVIRONMENT` (nhánh bypass GH-01) nên khác nhau giữa các máy.
> Trường hợp đó đã được L2-PAY-10/11 phủ trong môi trường có kiểm soát.

---

## PHẦN A2+A3 — 638 case mới cho workbook L1

### ⚠ Đọc mục này trước

Danh sách đầy đủ **không nằm trong file markdown này** mà ở **`TEST-CASES-L1-NEW.csv`** (cùng thư mục
gốc repo) — 638 dòng, đúng thứ tự cột của workbook L1:

```
Test ID | Class (Sheet) | Method / Block | SRS Reference | Priority | Negative? | Status | Defect ID
```

CSV được **sinh tự động từ chính mã test trong repo** (`scratchpad/gen-cases.sh`), nên tên
`Method / Block` tra ngược 1-1 với test thật, không có nguy cơ chép tay sai. Mở bằng Excel
(Data → From Text/CSV, encoding UTF-8) rồi lọc cột `Class (Sheet)` để dán vào từng sheet.

Toàn bộ 638 case: `SRS Reference = COVERAGE (code-driven)`, `Status = Pass`, `Defect ID` để trống.

**Về cột Priority và Negative?:** hai cột này được **suy ra bằng quy tắc máy**, cần soát lại:
- `Negative? = Yes` khi tên test chứa mã lỗi (400/401/403/404/409/500) hoặc từ khoá lỗi
  (Throws/Invalid/Missing/Wrong/Unknown/Fails/Expired/Insufficient/Concurrent…).
- `Priority = P1` cho case chặn quyền (401/403/Unauthorized/Forbid/Owner/Another/Scope/Secret),
  tranh chấp đồng thời (409/Concurrent), chặn validate (ModelState), và happy-path của hành động
  đổi trạng thái. Còn lại `P2`.

### Dải Test ID theo sheet (đã kiểm: **0 ID trùng**, các dải liền mạch)

| Sheet L1 | Dải Test ID | Số case | Nguồn |
|---|---|---|---|
| OrderService | L1-ORD-83 → 202 | 120 | DeliveryController (32) + OrderController (88) |
| QuotationService | L1-QUO-18 → 67 | 50 | QuotationController |
| WarehouseService | L1-WH-13 → 61 | 49 | HandoverController (4) + WarehouseController (45) |
| PurchaseOrderService | L1-PO-08 → 50 | 43 | PurchaseOrderController (phần PO) |
| AuthService | L1-AUTH-34 → 72 | 39 | AuthController |
| SalesChangeRequestService | L1-SCR-08 → 45 | 38 | SalesChangeRequestsController |
| GoodsIssueService | L1-GI-10 → 42 | 33 | GoodsIssueController |
| UserProfileService | L1-UP-06 → 35 | 30 | UserProfileController (hồ sơ) + CustomerProfileController |
| WarehouseManagementService | L1-WM-04 → 31 | 28 | WarehouseShiftController (2) + WarehouseManagementController (26) |
| MarketingPostService | L1-MKT-15 → 42 | 28 | MarketingPostController |
| InventoryService | L1-INV-06 → 28 | 23 | InventoryController |
| **Infrastructure** ⭐ | L1-INF-01 → 22 | 22 | ExceptionHandlingMiddleware (12) + 4 Hub (10) |
| GoodsReceiptService | L1-GR-08 → 27 | 20 | PurchaseOrderController (phần phiếu nhận) |
| AddressService | L1-ADDR-05 → 21 | 17 | UserProfileController (phần địa chỉ) |
| AdminUserService | L1-ADM-10 → 25 | 16 | AdminUsersController |
| CartService | L1-CART-11 → 23 | 13 | CartController |
| StockTransferService | L1-ST-20 → 30 | 11 | StockTransferController (bổ sung) |
| SalesAllocationService | L1-SA-14 → 23 | 10 | SalesController |
| NotificationService | L1-NOTI-04 → 13 | 10 | NotificationsController |
| JobRunService | L1-JOB-08 → 17 | 10 | SystemHealthController |
| SystemConfigService | L1-CFG-07 → 14 | 8 | AdminSystemConfigController |
| DashboardKpiServices | L1-DASH-09 → 15 | 7 | DashboardsController |
| ScheduledJobs | L1-SJOB-17 → 22 | 6 | ScheduledJobRunnerBackgroundService |
| ProductService | L1-PROD-06 → 10 | 5 | ProductController |
| AuditLogService | L1-AUD-08 → 09 | 2 | AuditLogController |
| **Tổng** | | **638** | |

⭐ **Cần quyết định:** sheet `Infrastructure` **chưa tồn tại** trong workbook. 22 case này phủ
`ExceptionHandlingMiddleware` và 4 SignalR Hub — không thuộc service nào. Hai lựa chọn:
1. **Tạo sheet mới `Infrastructure`** với prefix `L1-INF` (CSV đang theo phương án này) — khuyến nghị.
2. Gộp vào sheet `RegressionFixes` sẵn có, đổi `L1-INF-01…22` thành `L1-REG-13…34`.

Cộng với 54 case đợt 1 (Phần A, đã liệt kê chi tiết ở trên) → **692 dòng L1 mới** cần chèn vào workbook.

### File test nguồn

| File | Controller / thành phần được phủ |
|---|---|
| `VietTien.Tests/Controllers/SmallControllersTests.cs` | Cart, Product, Notifications, SystemHealth, Handover, WarehouseShift |
| `VietTien.Tests/Controllers/ProfileAndSalesControllersTests.cs` | UserProfile, CustomerProfile, Sales, SalesChangeRequests |
| `VietTien.Tests/Controllers/WarehouseControllersTests.cs` | Warehouse, GoodsIssue, WarehouseManagement |
| `VietTien.Tests/Controllers/DeliveryInventoryAdminControllerTests.cs` | Delivery, Inventory, AdminUsers, MarketingPost |
| `VietTien.Tests/Controllers/AuthQuotationControllerTests.cs` | Auth, Quotation |
| `VietTien.Tests/Controllers/OrderPurchaseOrderControllerTests.cs` | Order, PurchaseOrder |
| `VietTien.Tests/Controllers/AdminDashboardStockTransferTests.cs` | AdminSystemConfig, AuditLog, Dashboards, StockTransfer (bổ sung) |
| `VietTien.Tests/Infrastructure/InfrastructureAndHubTests.cs` | ExceptionHandlingMiddleware, 4 Hub, ScheduledJobRunnerBackgroundService |

### Ghi chú nghiệp vụ đáng đưa vào cột Notes của workbook

**Nhóm chống rò rỉ dữ liệu (nên đánh P1 và giữ nguyên khi refactor):**
- `L1-NOTI-05/06` — seed thông báo của user khác rồi khẳng định không lọt vào kết quả.
- `L1-UP-35` — seed giao dịch tín dụng của khách khác rồi khẳng định không lọt.
- `L1-SA-16` — khách của Sale khác trả **404**, cùng lớp lỗ hổng với IDOR `L1-ORD-71` đã vá.
- `L1-ORD` nhóm `GetSalesOrders_AsSalesStaff_ScopesToOwnCustomers` / `GetSalesOrderDetail_*` — vá IDOR.
- `L1-WM-31` (`GetStaff_ReturnsOnlyWarehouseRoles`) — không lộ tài khoản khách hàng.
- `L1-INF` nhóm `ChatHub_JoinQuotationChat_WhenNotParticipant_DoesNotJoinGroup` — chặn nghe lén nhóm chat.
- `L1-MKT` nhóm `MakeWebhookCallback_WithoutSecretHeader/WithWrongSecret_Returns401` — chặn giả mạo
  kết quả đăng bài.

**Nhóm `*_WhenConcurrent_Returns409`** (`L1-GI-24/29/35/41`, `L1-QUO`, `L1-PO`, `L1-ST`, `L1-WH`):
nhánh `DbUpdateConcurrencyException → 409` **không chạy được qua HTTP thật** khi bảng thiếu
`RowVersion` (defect GH-04) — mock service là cách duy nhất chạm tới.

**Sự thiếu nhất quán CÓ THẬT giữa các controller khi thiếu claim user** (đã ghi vào tên test, không
phải lỗi test — team nên thống nhất lại):

| Controller / action | Thiếu claim → | Vì sao |
|---|---|---|
| `GoodsIssue`, `UserProfile`, `Auth`, `MarketingPost`, `StockTransfer.Create`, `Notifications` | **401** | Kiểm tra claim tường minh trước khi gọi service |
| `Cart`, `Warehouse.AcceptOrder`, `Order.GetCheckoutSummary`, `Delivery`, `AdminSystemConfig`, `WarehouseManagement` | **400** | `GetUserId()` ném `UnauthorizedAccessException` nhưng action chỉ có `catch (Exception)` |
| `Inventory.SubmitShiftCount`, `StockTransfer.ReceiveTransfer` | **403** | Action có `catch (UnauthorizedAccessException) → Forbid()` |

**`L1-WH-13…16` (HandoverController):** ⚠ chỉ khoá hành vi hiện tại của một **stub chưa cài đặt** —
xem E4. Không được hiểu là bằng chứng luồng bàn giao hoạt động.

**`L1-INF-01…12` (ExceptionHandlingMiddleware):** phủ trọn bảng ánh xạ exception → status
(409 PROFILE_INCOMPLETE / 404 / 403 / 409 / 409 concurrency / 400 / 400 / **400 cho `Exception` gốc**
theo quy ước nghiệp vụ sẵn có / **500 che chi tiết** cho mọi subclass lạ), cộng nhánh
"response đã bắt đầu ghi thì ném lại thay vì làm hỏng response".

**`L1-SJOB-17…22` (ScheduledJobRunnerBackgroundService):** phủ đủ 4 nhánh quyết định "đến hạn chưa"
— chưa chạy lần nào, chưa đủ Interval, đủ Interval, và **job treo quá 3× Interval thì cho chạy lại**
(chống treo vĩnh viễn khi tiến trình cũ crash) — cộng nhánh "một job lỗi không làm sập vòng lặp nền".


## PHẦN B — Workbook L2: không thêm case, chỉ đổi Status

Coverage nên dồn vào L1 (nhanh, không cần container). L2 giữ nguyên 77 case.

**Cần đổi Status sau khi có migration ở Phần E:** hiện **62/109 execution L2 đang Fail** vì GH-03d,
trong đó nhiều case trước đó đã Pass. Sau khi migration được merge, chạy lại
`dotnet test VietTien.IntegrationTests` rồi cập nhật Status hàng loạt. Các case Blocked bởi
GH-03a/b/c cũng sẽ chuyển được sang Pass/Fail thật.

Ô tổng kết L2 hiện tại (`Pass 53 / Fail 11 / Blocked 13`) **chưa phản ánh** GH-03d — cần đo lại
trước khi nộp.

---

## PHẦN C — Bảng ánh xạ controller → sheet L1

| Controller | Sheet L1 đích | Trạng thái |
|---|---|---|
| PaymentsController | ManualPaymentService | ✅ đã viết |
| SuppliersController | SupplierService | ✅ đã viết |
| VehiclesController | VehicleService | ✅ đã viết |
| DiscountTiersController | DiscountTierService | ✅ đã viết |
| MaterialController | MaterialService | ✅ đã viết |
| StockTransferController | StockTransferService | ✅ đã viết |
| SePayController | OrderService | ✅ đã viết |
| CartController | CartService | ✅ đợt 2 — 100% |
| ProductController | ProductService | ✅ đợt 2 — 96,1% |
| UserProfileController (phần hồ sơ), CustomerProfileController | UserProfileService | ✅ đợt 2 — 100% |
| UserProfileController (phần địa chỉ) | AddressService | ✅ đợt 2 |
| WarehouseController, HandoverController | WarehouseService | ✅ đợt 2 — 100% |
| WarehouseManagementController (93%), WarehouseShiftController (100%) | WarehouseManagementService | ✅ đợt 2 |
| SalesChangeRequestsController | SalesChangeRequestService | ✅ đợt 2 — 100% |
| NotificationsController | NotificationService | ✅ đợt 2 — 95,1% |
| SalesController | SalesAllocationService | ✅ đợt 2 — 100% |
| SystemHealthController | JobRunService | ✅ đợt 2 — 98,2% |
| GoodsIssueController | GoodsIssueService | ✅ đợt 2 — 100% |
| OrderController (99%), DeliveryController (100%) | OrderService | ✅ đợt 3 |
| PurchaseOrderController — phần PO (98,6%) | PurchaseOrderService | ✅ đợt 3 |
| PurchaseOrderController — phần phiếu nhận | GoodsReceiptService | ✅ đợt 3 |
| QuotationController (100%) | QuotationService | ✅ đợt 3 |
| AuthController (100%) | AuthService | ✅ đợt 3 |
| AdminUsersController (98,5%) | AdminUserService | ✅ đợt 3 |
| AdminSystemConfigController (100%) | SystemConfigService | ✅ đợt 3 |
| InventoryController (100%) | InventoryService | ✅ đợt 3 |
| MarketingPostController (100%) | MarketingPostService | ✅ đợt 3 |
| AuditLogController (100%) | AuditLogService | ✅ đợt 3 |
| DashboardsController (97,9%) | DashboardKpiServices | ✅ đợt 3 |
| ExceptionHandlingMiddleware, 4 SignalR Hub | **Infrastructure** (sheet mới) | ✅ đợt 3 |
| ScheduledJobRunnerBackgroundService | ScheduledJobs | ✅ đợt 3 |

**Toàn bộ 31 controller + tầng hạ tầng đã xong.** Phần chưa phủ còn lại nằm hết ở **tầng Service**,
nơi mỗi dòng đắt hơn nhiều vì phải seed dữ liệu EF thay vì mock một interface.

**Hàng đợi tiếp theo, xếp theo số dòng chưa phủ** (đo ngày 07/08/2026):

| Lớp | Dòng chưa phủ | Hiện tại |
|---|---|---|
| `OrderService` | 640 | 72,5% |
| `SalesChangeRequestService` | 301 | 45,8% |
| `WarehouseService` | 293 | 49,8% |
| `InventoryService` | 265 | 35,6% |
| `StockTransferService` | 208 | 49,8% |
| `SalesAllocationService` | 144 | 68,4% |
| `PurchaseOrderService` | 97 | 61,9% |
| `MarketingPostService` | 73 | 71,4% |
| `GoodsIssueService` | 72 | 78,3% |
| `WarehouseManagementService` | 70 | 47,7% |
| `GoodsReceiptService` | 65 | 75% |
| `QuotationRepository` | 61 | 44,5% |

Số trên **đã đo lại sau khi vá migration** (đợt 4), nên không còn bị GH-03d làm nhiễu — dùng trực tiếp
để chọn mục tiêu tiếp theo mà không sợ viết trùng phần L2 vốn đã phủ.

---

## PHẦN E — Việc cần team làm (không thuộc phạm vi test)

### E1. ~~Một migration gom 4 ổ drift~~ — ✅ **ĐÃ VÁ XONG (đợt 4)**

Migration `20260806232730_FixSchemaDrift` đã vá toàn bộ. Kết quả: **62 test L2 đỏ → 18**.

Delta **được đo bằng máy**, không liệt kê tay: dựng song song một DB bằng `Database.Migrate()` và một
DB bằng `Database.EnsureCreated()` trên cùng container rồi diff `INFORMATION_SCHEMA`. Cách này lộ ra
**2 ổ drift chưa ai biết** ngoài 4 ổ đã ghi nhận:

| Mã | Thiếu gì | Hậu quả nếu không vá |
|---|---|---|
| GH-03a | `GoodsIssues` thiếu 8 cột (`Department`, `ExternalRecipientName`, `IsReversal`, `PaperDocumentNumber`, `ReceivedAt`, `ReversalForIssueId`, `ReversalReason`, `UsagePurpose`) | Không xuất kho được |
| GH-03b | 4 bảng chưa từng có `CreateTable`: `MarketingPosts`, `ReturnExchangeRequests`, `ReturnExchangeRequestItems`, `ReturnExchangeRequestNewItems` | Không đổi/trả, không marketing |
| GH-03c | `GoodsReceipts` thiếu `ImageProofUrl` | Không lập phiếu nhập kho |
| GH-03d | `Order.ShippingAddress` (từ commit `02b036a`) | `Invalid column name 'ShippingAddress'` → 62/109 test L2 chết |
| **GH-03e** 🆕 | `StockTransactions.Note` | Không ghi được ghi chú biến động tồn |
| **GH-03f** 🆕 | `StockTransfers` thiếu `DeliveryShift`, `DeliveryVehicleId`, `ScheduledDeliveryDate` | Không xếp lịch xe cho phiếu điều chuyển |

**Vì sao `dotnet ef migrations add` không tự phát hiện:** nó diff *model* với *ModelSnapshot*, mà
snapshot chính là thứ đã sai — snapshot ghi nhận schema mà không migration nào dựng nổi. Chạy
`migrations add` ở trạng thái này ra **migration rỗng**; chính điều đó là bằng chứng của drift.
Vì vậy `Up()` phải viết tay theo delta đo được, còn `ModelSnapshot` gốc giữ nguyên (vốn đã đúng).

**Chốt chặn chống tái diễn:** `VietTien.IntegrationTests/SchemaDriftGuardTests.cs` — test khẳng định
schema dựng từ migrations **trùng khít** schema mà model cần; đỏ thì thông điệp liệt kê sẵn thứ còn
thiếu. Chạy ~25 giây (cần Docker). Đây là thứ duy nhất bắt được loại lỗi này trước khi merge.

### E1b. 18 test L2 còn đỏ — đều là defect nghiệp vụ thật, không phải schema

`L2_RET_01/02/03/04/06/07` · `L2_TRF_01/04` · `L2_FUL_03/06` · `L2_PAY_09/10` · `L2_PUR_04/05`
· `L2_SJOB_10/11` · `L2_FLOW_02/06` — tương ứng GH-01, GH-06, GH-08, GH-09, GH-10, GH-12, GH-13,
GH-14, GH-15. Đây là các case cố ý để đỏ làm bằng chứng defect (theo quy ước R2), **không hạ assertion**.

GH-03d đến từ commit `02b036a` ("update service for inventory and stock transfer, fix logic about
address order…"). Trên DB dựng từ migrations, **cả 4 luồng nghiệp vụ trên đều không chạy được** —
đây là blocker triển khai, không chỉ là vấn đề của test.

### E2. `L1_ST_04` cần cập nhật theo tính năng mới

Cùng commit `02b036a` đổi thông điệp `StockTransferService` từ
`"Chỉ có thể xuất kho cho phiếu ở trạng thái Nháp."` thành `"…Nháp hoặc Đã xếp xe."`
Test `L1_ST_04_Dispatch_NonDraft_Rejected` vẫn assert chuỗi cũ → **đang đỏ**.
Đây là test cần cập nhật theo tính năng, **không phải defect** — nên do người làm tính năng sửa.

### E3. GH-06 vẫn còn nguyên sau commit mới

`StockTransferService` vẫn có **0** tham chiếu `StockTransaction` → điều chuyển kho không để lại vết
biến động tồn (BR-035). Cùng nhóm với GH-12 (`InventoryService.AdjustInventoryAsync` cũng không ghi).

### E3b. GH-16 🆕 — guard chống trùng đơn trong `ApproveAsync` là DEAD CODE

`VietTien.API/Services/Implementations/SalesChangeRequestService.cs`, trong `ApproveAsync`:

```csharp
var decisionByOrderId = dto.OrderDecisions.ToDictionary(d => d.OrderId);   // ném ArgumentException tại đây
if (decisionByOrderId.Count != dto.OrderDecisions.Count)
    throw new InvalidOperationException("Danh sách quyết định có đơn hàng bị trùng.");   // KHÔNG BAO GIỜ CHẠY
```

`ToDictionary` đã ném `ArgumentException("An item with the same key has already been added")` ở dòng
trên, nên dòng `throw` phía dưới không bao giờ tới được. Hậu quả: Manager gửi trùng đơn trong danh
sách quyết định sẽ nhận thông điệp .NET thô bằng tiếng Anh thay vì câu tiếng Việt đã soạn sẵn.

Sửa gợi ý: đổi sang `GroupBy(...).Any(g => g.Count() > 1)` kiểm tra **trước**, hoặc dùng
`ToDictionary` sau khi đã validate. Test `Approve_WhenDecisionListHasDuplicateOrder_Rejected` hiện
khẳng định **hành vi thật** (ArgumentException) — sửa code xong thì đổi lại assertion.

### E3c. Ghi chú đính chính: "Sale chưa được mở giải trình" trả 403 chứ không phải 400

`SubmitExplanationAsync` ném `UnauthorizedAccessException` (không phải `InvalidOperationException`)
khi Manager chưa bấm "Yêu cầu giải trình", nên controller map thành **403 Forbid**. Case controller
tương ứng đã được đổi tên thành `SubmitExplanation_WhenServiceThrowsInvalidOperation_Returns400` để
không mô tả sai tình huống — nó chỉ kiểm phép ánh xạ, không kiểm gate.

### E4. `HandoverController` là stub chưa cài đặt — phát hiện khi viết test đợt 2

`VietTien.API/Controllers/HandoverController.cs` (39 dòng) **không có dependency nào** trong
constructor và cả 4 action (`GetHandoverById`, `CreateHandover`, `WarehouseConfirm`, `SalesConfirm`)
đều `return Ok(...)` với **giá trị cứng** — không đọc, không ghi DB. `GetHandoverById` luôn trả
`status = "PENDING"` bất kể id truyền vào.

4 case L1-WH-13…16 chỉ khoá hành vi hiện tại để tránh sửa nhầm; **chúng không chứng minh luồng bàn
giao hoạt động**. Luồng bàn giao thật đang nằm ở `WarehouseController.HandoverOrder`
(`POST api/warehouse/orders/{orderId}/handover`). Team cần quyết định: xoá controller stub này,
hoặc cài đặt nó cho đúng — nếu FE đang gọi nhầm vào đây thì màn hình bàn giao đang chạy trên dữ
liệu giả.

### E5. Muốn phủ 969 dòng của 6 adapter IO thì phải refactor

`EmailService` (`new SmtpClient()` inline), `CloudinaryService` (`new Cloudinary()` trong ctor),
`GoogleTokenValidator` (gọi static `GoogleJsonWebSignature`) — không có seam để mock. Hiện đang được
**loại khỏi mẫu số** và ghi rõ lý do. Nếu muốn tính vào, cần tiêm factory qua constructor.

---

## Cách chạy lại phép đo

```bash
dotnet test VietTien.sln --nologo --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```
```bash
reportgenerator -reports:"**/TestResults/**/coverage.cobertura.xml" -targetdir:cov -reporttypes:TextSummary
```
Đọc `cov/Summary.txt`. Thư mục `cov/`, `coverage-report/` và `*.cobertura.xml` đã được thêm vào
`.gitignore` — **không commit artifact đo**.
