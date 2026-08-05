# Đối chiếu doc L1 v2.3 ↔ code thật — bàn giao cho v2.4

Nguồn: `Report_5_2_L1-UnitTests_VietTien_v2_3.xlsx` (**390 case**).
Ngày đối chiếu: 01/08/2026. **Toàn bộ 390 Test ID đã có test tương ứng — 0 case Not Run.**

Chạy lại 2 suite:

```bash
dotnet test VietTien.Tests\VietTien.Tests.csproj --nologo -v q
```

```bash
npm --prefix D:\SEP490\SEP490_fe\frontend test
```

Trạng thái từng Test ID của lần chạy 01/08 nằm ở `VietTien.Tests/L1_status_v2.3_2026-08-01.csv`
(mở bằng Excel, copy cột Status dán sang sheet *TestCase List*).

> **Lưu ý khi đối chiếu bằng tay**: 1 Test ID có thể ứng với NHIỀU test chạy — `[Theory]`/`it.each`
> nhiều dòng đếm thành nhiều test nhưng chỉ là 1 case trong Excel. Chỉ cần 1 nhánh đỏ thì cả case
> ghi **Fail**. Ngoài ra `.trx` ghi test bị Skip là `NotExecuted` (không phải `Skipped`) — dễ đếm
> nhầm thành Pass.

---

## 1. Kết quả chạy test

**Cập nhật 01/08/2026** — sau rà soát trên bản pull mới (BE `a3005b2`, FE `11c6049`):

| Suite | Tổng | Pass | Fail | Skip |
|---|---|---|---|---|
| `dotnet test` (BE) | 400 | 360 | **28** | 12 (có từ trước) |
| `npx vitest run` (FE) | 63 | 53 | **10** | 0 |

Quy về **Test ID**:

| Nhóm | Tổng | Pass | Fail | Blocked | Not Run |
|---|---|---|---|---|---|
| 390 case của doc v2.3 | 390 | 336 | 31 | 23 | **0** |
| 8 case MỚI (đề xuất cho v2.4) | 8 | 7 | 1 | 0 | 0 |
| **Cộng** | **398** | **343** | **32** | **23** | **0** |

**32 test đỏ là CÓ CHỦ ĐÍCH** (assert theo SPEC, chờ sửa code) — xem mục 2.
**5 test đỏ còn lại của FE là LỖI CÓ TỪ TRƯỚC**, không do đợt này gây ra — xem mục 5.

### Đồng bộ với doc v2.3 (đợt 01/08)

v2.3 thêm 19 case so với v2.2. Đã xử lý trọn:

- **Viết mới 11 case**: `AUTH-29/30/31` (throttle + rate limit resend OTP email), `AUTH-32/33`
  (ForgotPassword), `ORD-76` (webhook khớp chính xác), `FEC-07/08` (gộp giỏ), `FES-07/08`
  (resendOtp FE), `FCMP-12` (allowGuest không rò sang route nhạy cảm).
- **Đổi số 5 nhóm ID** cho khớp v2.3 (trước đó tự đặt khi chưa có doc):

  | Cũ | Mới | Lý do |
  |---|---|---|
  | `ORD-74` (VAT) | `ORD-77` | v2.3 dùng ORD-74 cho webhook trả thiếu |
  | `ORD-74b` | `ORD-77b` | ↑ |
  | `ORD-75` (trả thiếu) | `ORD-74` | khớp v2.3 |
  | `ORD-75b` (trả thừa) | `ORD-75` | khớp v2.3, kỳ vọng đảo thành TỪ CHỐI |
  | `FES-07/08/09` | `FES-09/10/11` | v2.3 dùng FES-07/08 cho resendOtp |
  | `FCMP-12/12b` | `FCMP-13/13b` | v2.3 dùng FCMP-12 cho allowGuest leak |

- **Sửa 3 kỳ vọng theo spec** — cả 3 chuyển sang đỏ, xem mục 2.3.

### Thay đổi so với lần chạy 30/07

- `L1-FEC-04` **Fail → Pass**: FE đã cài xong giỏ hàng tạm cho khách vãng lai
  (`cartService.addGuestCartItem` + `CartContext.mergeGuestCartIntoServer`). Test cũ dò sai key
  (`pendingCartItem` thay vì `guestCart`) nên báo lỗi giả — đã sửa.
- `L1-FEC-03` từng **PASS GIẢ**: `addToCart` đổi chữ ký `(productId, qty)` → `(product, qty)` nhưng test
  vẫn xanh vì mock MSW trả cart cứng, không đọc body. Đã sửa để bắt và assert body thật.
- Thêm 2 test đỏ mới: `L1-ORD-74b` (tiền VND lẻ) và `L1-ORD-75` (webhook trả thiếu) — xem mục 2.3.

---

## 2. Test ĐỎ có chủ đích — assert theo SPEC, chờ sửa code

Mỗi test dưới đây có comment `🔴 DEFECT CANDIDATE` hoặc `🔴 SPEC GAP` ngay trên `[Fact]`/`it()`.

### 2.1. Hai defect doc đã đánh dấu sẵn

| Case | Vị trí code | Vấn đề |
|---|---|---|
| `L1-SJOB-11` | [LowStockAlertJob.cs:44](VietTien.API/Services/ScheduledJobs/LowStockAlertJob.cs:44) | Dùng `AvailableQuantity >= ReorderThreshold → continue` (tức chỉ cảnh báo khi `<`). SRS FT-12 AC-04 yêu cầu `<=`. **Bằng chứng bổ sung**: `Material.IsBelowSafetyThreshold()` ở cùng job dùng `CurrentStock <= SafetyThreshold` — hai nhánh của cùng 1 job đang KHÔNG nhất quán. |
| `L1-ORD-73` | [OrderService.cs:2077](VietTien.API/Services/Implementations/OrderService.cs:2077) | Hard-code `FailedDeliveryCount >= 3`, bỏ qua key `DELIVERY_FAILURE_MANAGER_THRESHOLD` (đã seed "3" ở `ApplicationDbContext.cs:1049`). |

### 2.2. Spec gap MỚI phát hiện trong lúc viết test (doc chưa đánh dấu)

| Case | Vấn đề | Ảnh hưởng |
|---|---|---|
| `L1-AUTH-17` | OTP SMS lưu **thô** `"123456:0912345678"` vào `User.PhoneOtpCode`, không băm | NFR-SEC04 |
| `L1-AUTH-18` | Không có throttle resend OTP (spec: ≥ 60 giây) | FT-01 BV-02 |
| `L1-AUTH-20` | Không có rate limit số lần gửi OTP (spec: 5 lần/30 phút, 10 lần/ngày) | FT-01 BV-02, BR-024 |
| `L1-AUTH-23` | Không đếm số lần nhập sai OTP → **brute-force được 10⁶ mã** | FT-01 NAC-02 |
| `L1-ORD-51` | `CreateReturnExchangeRequestAsync` không đối chiếu `requestedByUserId` với chủ đơn → **IDOR**, khách tạo được yêu cầu đổi/trả trên đơn của người khác | FT-08 NAC-05, NFR-SEC03 |
| `L1-ORD-67` | `TrackOrderPublicAsync` (endpoint CÔNG KHAI) trả nguyên `CustomerPhone` + `ShippingAddress` | NFR-SEC06 |
| `L1-REG-01` | `AdjustInventoryAsync` không kiểm tra nhân viên có thuộc kho của bản ghi tồn → **IDOR kho** | FT-05 NAC-05 |
| `L1-REG-06` | `RequestCancelOrderAsync` chỉ xét `OrderStatus`; đơn đang giao (`Processing` + `DeliveryStatus.InDelivery`) **vẫn huỷ được** | SRS 4.4.1 |
| `L1-REG-07` | `GenerateSePayQrAsync` không kiểm tra `PaymentStatus` → đơn đã Paid vẫn sinh QR mới, khách có thể **chuyển khoản lần 2** | FT-03 NAC-02, BR-016 |
| `L1-MKT-04` | `PublishNowAsync` không có state guard → bài **Draft** vẫn đăng thẳng lên Facebook | BR-046, 4.4.4 |
| `L1-MKT-12` | `HandleMakeWebhookCallbackAsync` không kiểm tra trạng thái nguồn → Draft nhảy thẳng sang Success | BR-049 |
| `L1-ADM-05` | `ChangeRoleAsync` không kiểm tra `CustomerProfile.AssignedSalesStaffId` → hạ vai trò Sales làm khách **mất người phụ trách trong im lặng** | FT-04 NAC-03 |
| `L1-ADM-07` | `SetActiveStatusAsync` không đếm Sales còn hoạt động → khoá người cuối cùng thì **không còn ai nhận khách mới** | FT-04 NAC-03 |
| `L1-SA-08` | `ResolveReferralStaffAsync` chỉ lọc `Role == SalesStaff`, **không xét `IsActive`** → gán khách cho nhân viên đã nghỉ | FT-04 NAC-02 |
| `L1-CFG-04` | `SetValueAsync` nhận thẳng `EffectiveDate` quá khứ, không chặn thay đổi hồi tố | FT-09 NAC-04, BR-050 |
| `L1-DT-07` | `DiscountTierService.Validate()` không kiểm tra **chồng lấn khoảng** giữa các bậc | BR-006, BR-050 |
| `L1-VEH-03` | `CreateAsync` chỉ kiểm tra trùng `VehicleNumber`, **không kiểm tra trùng `LicensePlate`** | BR-037 |
| `L1-AUD-04` | `SensitiveDataRedactor` chỉ mask theo tên field (`password/otp/token/secret/apikey/pin`); **SĐT và MST lưu nguyên văn** | NFR-SEC06 |
| `L1-FES-02` | `fetchWithToken` **không có silent refresh** — gặp 401 là xoá session và đá về `/login` ngay, dù refresh token còn hạn | NFR-SEC02 |
| `L1-FEC-04` | `CartContext.addToCart` chỉ ném lỗi khi chưa đăng nhập; **không lưu sản phẩm khách định mua** → sau khi đăng nhập khách phải tự tìm lại | FT-01 AC-05/NAC-05 |
| `L1-FCMP-05` | Cart.jsx **có** hiện khối "Yêu cầu báo giá đặc biệt" khi ≥ 100 triệu nhưng **vẫn giữ nút "Đặt Hàng & Xem Hóa Đơn"** bên cạnh → khách bấm đặt hàng thẳng được | FT-02 AC-03, BR-026 |
| `L1-FCMP-07` (×2) | `PhoneVerificationModal` chỉ kiểm tra rỗng, **không validate định dạng SĐT** | FT-01 NAC-02, BV-02 |

### 2.3. Spec gap phát hiện trong đợt rà soát 01/08/2026

| Case | Vấn đề | Ảnh hưởng |
|---|---|---|
| `L1-ORD-74` | `OrderService` — `if (payload.transferAmount < order.FinalPayment) return;` → khách chuyển **THIẾU** thì hàm **return im lặng**: không log, không tạo `PaymentException`, không báo ai. Tiền đã vào tài khoản công ty nhưng đơn vẫn Unpaid và không ai biết có giao dịch treo.<br>**Bằng chứng đây là thiếu sót chứ không phải thiết kế**: nhánh ngay bên dưới ("trả tiền sau khi đơn đã huỷ") **CÓ** tạo `PaymentException` mã `PAID_AFTER_CANCELLATION` | Tiền thật |
| `L1-ORD-75` | Trả **THỪA** vẫn được set Paid. SRS BV-01 yêu cầu đối soát **khớp chính xác** → chênh lệch thừa không được ghi nhận ở đâu, đối soát ngân hàng sẽ lệch | Tiền thật |
| `L1-ORD-77b` | `vatAmount = totalAfterDiscount * 0.10m` và `discountAmount = total * percent` **không hề làm tròn**. Giỏ hàng tổng lẻ → sinh số tiền lẻ tới hàng phần nghìn đồng, lưu thẳng vào `Order` và đẩy sang SePay. VND không có đơn vị nhỏ hơn 1 đồng | Tiền thật |
| `L1-AUTH-26` | `ResendEmailOtpAsync` trả thẳng *"Không tìm thấy tài khoản với email này."* → **user enumeration**: dò được email nào đã đăng ký.<br>Đối chiếu: `ForgotPasswordAsync` ở **cùng service** đã làm đúng — luôn trả cùng một câu cho cả 2 nhánh | Bảo mật |
| `L1-AUTH-28` | OTP email lưu **thô** vào `User.OtpCode`. Cùng loại lỗ hổng với `L1-AUTH-17` (OTP SMS) | Bảo mật |
| `L1-AUTH-29/30/31` | `ResendEmailOtpAsync` **không có throttle 60s, không có rate limit 5 lần/30 phút hay 10 lần/ngày** → bấm bao nhiêu lần cũng gửi, lạm dụng được endpoint công khai để spam mail và đốt chi phí gửi | Bảo mật / chi phí |
| `L1-FEC-08` | `mergeGuestCartIntoServer` bắt lỗi rồi chỉ `console.error(...)` + `break`, **không gọi `setError`** → khách không hề biết giỏ hàng chưa được gộp.<br>Phần giữ lại `guestCart` thì code làm **đúng** (chỉ xoá item sau khi `addItem` thành công) | Trải nghiệm |

> Doc `L1-ORD-16` mới chỉ ghi nhận việc đối soát dùng `>=` thay vì khớp chính xác — **chưa ai soi nhánh
> trả thiếu**. v2.3 đã tách thành bộ 3 mốc `ORD-74/75/76` (−1 / khớp / +1).

### 2.3b. Case ĐÃ XANH sau khi kiểm chứng (không phải gap)

- `L1-AUTH-32/33` — `ForgotPasswordAsync` **đã** chống enumeration đúng (luôn trả cùng một thông điệp
  cho cả email tồn tại lẫn không tồn tại) và sinh reset token đủ dài, có hạn, link chứa đúng token.
- `L1-FEC-07` — request gộp giỏ **chỉ** gửi `{ productId, quantity }`, không gửi giá từ client
  → server tự tính giá, không có lỗ hổng "khách tự đặt giá".
- `L1-ORD-76` — webhook khớp chính xác → Paid + đúng 1 `PaymentTransaction`.

### 2.4. Ghi nhận thêm (chưa tạo test đỏ)

`AuthService.ResendEmailOtpAsync` **nuốt lỗi gửi mail**: nếu SMTP hỏng thì `catch` chỉ ghi log rồi
vẫn `return (true, "Mã OTP mới đã được gửi...")`. Người dùng được báo đã gửi nhưng không bao giờ nhận
được mail và không có cách nào biết. Đề xuất nhóm chốt hướng xử lý trước khi viết test.

---

## 3. Case đánh BLOCKED — không test được ở tầng L1 (11 case)

### 3.1. Phân quyền nằm ở Controller, không phải service → chuyển sang **L3**

| Case | Lý do |
|---|---|
| `L1-ORD-53` | `ProcessReturnExchangeRequestAsync(requestId, managerId, dto)` không nhận vai trò; chặn ở `[Authorize(Roles="SalesManager,Admin")]` trên `OrderController` |
| `L1-ORD-71` | `GetSalesOrderDetailAsync(Guid orderId)` — không có bất kỳ tham số caller nào |
| `L1-MKT-03` | `MakeDecisionAsync` không nhận `userRole`; chặn ở `[Authorize(Roles="SalesManager,SaleManager,Admin,CEO")]` |
| `L1-DASH-02` | `GetDashboardAsync(callerId, from, to)` — `callerId` luôn lấy từ JWT, Sales Staff không có đường truyền id người khác vào |
| `L1-DASH-05` | `ISalesManagerDashboardService.GetDashboardAsync(from, to)` — không có khái niệm caller |
| `L1-AUD-07` | `ExportCsvAsync(query)` — không có tham số `callerRole` |
| `L1-REG-03` | `StockTransferService.ReceiveAsync(id, dto)` — không có tham số caller/kho |

### 3.2. Tính năng chưa tồn tại → không có gì để unit test

| Case | Lý do |
|---|---|
| `L1-VEH-05` | `VehicleService` không hề biết tới lịch chuyến (không có quan hệ với Order/DeliverySchedule) |
| `L1-JOB-03` | `JobRunService` **không có cơ chế retry** — nó chỉ bọc đúng 1 lần chạy. Retry là chuyện riêng của từng job (đã phủ ở `L1-WHL-07`) |
| `L1-FCMP-04` | Không tồn tại khái niệm `isPriceExpired` / banner cảnh báo giá trong toàn bộ FE |
| `L1-FCMP-06` | Màn QR SePay **không có đếm ngược 15 phút**. Biến `countdown` ở `Checkout.jsx:379` là đếm 5 giây tự về trang chủ SAU khi thanh toán thành công |

---

## 4. Doc lệch signature/cơ chế thật — cần sửa trong v2.3

### 4.1. Sai tên method / tham số

| Doc ghi | Code thật |
|---|---|
| `salesStaffDashboardService.GetAsync(S1, period)` | `GetDashboardAsync(Guid callerId, DateTime from, DateTime to)` |
| `salesManagerDashboardService.GetAsync(managerId, period)` | `GetDashboardAsync(DateTime from, DateTime to)` |
| `ceoDashboardService.GetAsync(period)` | `GetDashboardAsync(DateTime from, DateTime to)` |
| `kpiService.GetSnapshotAsync(from, to)` | `GetSnapshotAsync(Guid? salesStaffId, DateTime from, DateTime to)` |
| `jobRunService.RunTrackedAsync('JobA', () => ok)` | `RunTrackedAsync(IScheduledJob job, JobTriggerType, Guid? actorUserId, ct)` |
| `jobRunService.GetHealthSummaryAsync()` | `GetHealthSummaryAsync(IEnumerable<string> knownJobNames)` |
| `auditLogService.LogAsync(entity, action, before, after, actorId)` | 10 tham số: `(entityName, entityId, action, actorUserId, actorEmail, actorRole, before, after, reason, ipAddress)` |
| `RequestPhoneVerificationAsync(U1, dto{phone, purpose})` | `RequestPhoneVerificationAsync(Guid userId, string phoneNumber)` — **không có `purpose`** |
| `VerifyPhoneOtpAsync(U1, dto{otp})` | `VerifyPhoneOtpAsync(Guid userId, string otpCode, string phoneNumber)` |
| `ChangeUserRoleRequest{Role: '...'}` | field thật là **`NewRole`**, và **bắt buộc có `Reason`** |
| `EnsureCustomerProfileAsync(U1)` | `EnsureCustomerProfileAsync(Guid userId, string? taxCode)` — **2 tham số** |
| `orderService.ConfirmPickupAsync(R1, staffId, dto{qty})` | `ConfirmPickupAsync(Guid requestId, Guid userId)` — **không có dto/qty** |
| `orderService.UploadInvoicePdfAsync(O1, file, ...)` | `UploadInvoicePdfAsync(orderId, string pdfBase64, callerUserId, callerRole)` — nhận **base64 string** |
| `vehicleService.GetAllAsync(query{active:true})` | `GetAllAsync()` — **không nhận filter** |
| `eSmsService.SendAsync(phone, message)` | `ISmsService.SendSmsAsync(phone, message)` trả **tuple** `(bool Success, string ErrorMessage)` |
| `cloudinaryService.UploadImageAsync(file)` trả `{URL, publicId}` | `UploadImageAsync(IFormFile file, string folder)` trả **string URL**; publicId lấy lại bằng `ExtractPublicId(url)` |
| `aiGeneratorService.GenerateAsync(prompt)` | `GenerateMarketingOptionsAsync(GenerateAiContentRequestDto request)` |
| `dto{Decision = Approve}` (enum) | `MarketingPostDecisionDto.Action` là **string**: `"Approve" \| "ApproveNow" \| "Rework" \| "Reject"` |

### 4.2. Sai về CƠ CHẾ (quan trọng — ảnh hưởng cách viết test)

| Doc ghi | Thực tế |
|---|---|
| `L1-SJOB-05/06/07`: OrderSlaJob đọc `COD_WARNING/ESCALATION/RESERVATION_MINUTES` từ SystemConfig | **OrderSlaJob HARD-CODE 25/30/35 phút.** Giá trị trùng mặc định nên hành vi vẫn đúng SRS, nhưng đổi config sẽ KHÔNG có tác dụng. Đây là **cùng loại khiếm khuyết với `L1-ORD-73`** nhưng doc chưa đánh dấu 🔴 |
| `L1-MKT-14`: ngưỡng đọc qua `GetEffectiveValueAsync('MAX_SCHEDULED_MARKETING_POSTS')` | Là **hằng số** `MarketingPostService.MAX_SCHEDULED_POSTS = 30`, không đọc config |
| `L1-SA-01..03`: "AutoAssignCustomerAsync / AssignUnassignedCustomersAsync no longer on ISalesAllocationService" | Ghi chú **đã lỗi thời** — cả 2 method vẫn còn trên interface |
| `L1-SA-07/09`: "Trả null / **báo lỗi**", "**Báo mơ hồ**" | `ResolveReferralStaffAsync` trả **tuple** `(Guid?, string?)`, KHÔNG ném exception → test phải assert trên `Error` |
| `L1-SA-12`: "danh sách rỗng → **từ chối**" | Service **bỏ qua im lặng** (`Participants is { Count: > 0 }`), không báo lỗi. Kết quả bảo vệ vẫn đạt. ⚠ **Rủi ro còn lại**: truyền `[{S1,false},{S2,false},{S3,false}]` sẽ tắt HẾT participant mà không bị chặn |
| `L1-MKT-05`: Approve → `Status = Approved (2)` | Code chuyển thẳng `Submitted → Scheduled (5)` |
| `L1-RES-01/02`: "hạn 15 phút / 35 phút" | Đúng như v2.2 đã ghi chú — thời hạn KHÔNG thuộc `InventoryReservationService` |
| Sheet Introduction: "Mock `IClock`/`ITimeProvider` cho mọi test phụ thuộc thời gian" | **Không tồn tại `IClock`/`TimeProvider` trong codebase** — mọi job dùng `DateTime.UtcNow` trực tiếp. Test phải điều khiển thời gian bằng cách **back-date `CreatedAt`/`ExpiryDate`** trên entity seed |
| Sheet Introduction: "frontend TypeScript" | FE là **JavaScript/JSX** (một vài file lẻ dùng `.tsx`). File test dùng `.jsx` |

### 4.2b. Thay đổi hành vi ở bản pull 01/08/2026 (doc chưa phản ánh)

| Thành phần | Thay đổi | Test tương ứng |
|---|---|---|
| `CartContext.addToCart` | Đổi chữ ký: `(productId, qty)` → **`(product, qty)`** nhận object `{ id, name, imageUrl, price }` | `L1-FEC-03` (đã cập nhật) |
| `cartService` | Thêm giỏ hàng tạm cho khách vãng lai — key `localStorage['guestCart']`, tự gộp lên server sau đăng nhập | `L1-FEC-04`, `L1-FEC-06` (mới) |
| `ProtectedRoute` | Thêm prop **`allowGuest`**; sai vai trò nay đá về **trang chủ của vai trò đó** (SalesStaff→`/sales`, Admin→`/admin`…) thay vì luôn về `/` | `L1-FCMP-11`, `11b`, `12`, `12b` (mới) |
| `AuthService` | Thêm `ResendEmailOtpAsync` + endpoint `POST /auth/resend-otp` | `L1-AUTH-26..28` (mới) |
| `authService.resendOtp` (FE) | Bỏ hack gọi lại `/auth/register`, nay gọi endpoint thật | gián tiếp qua `L1-AUTH-26..28` |
| `AuthService.LoginAsync` | Chỉ tính `IsProfileCompleted` cho role Customer (tối ưu 2 query) | `L1-AUTH-07` (vẫn xanh) |

### 4.3. Case cần bổ sung vào doc v2.3

Sheet `PurchaseOrderService` hiện chỉ có PO-01..07. Trong code có thêm 2 test chưa được cấp ID
(hiện đặt tên `PO_Extra_*` trong `PurchaseOrderServiceTests.cs`) — đề nghị cấp mã PO-08/PO-09:

- Close PO đã `FullyReceived` → `Closed` (terminal).
- Close PO đã `Closed` → reject, không đóng lặp lại.

**16 case MỚI đã viết trong đợt 01/08/2026, cần thêm vào doc v2.3** (chi tiết trong
`L1_status_2026-08-01.csv`, cột "Trong doc v2.2?" = `MOI - them vao v2.3`):

| Sheet | Case | Nội dung |
|---|---|---|
| AuthService | `L1-AUTH-26..28` | ResendEmailOtp: email lạ / đã verify / hợp lệ |
| OrderService | `L1-ORD-74`, `74b` | VAT 10% SAU chiết khấu — chốt con số; tiền phải là số nguyên đồng 🔴 |
| OrderService | `L1-ORD-75`, `75b` | Webhook trả thiếu 🔴 / trả thừa |
| ExternalIntegrations | `L1-EXT-08b` | AiGenerator từ chối sản phẩm ngừng bán trước khi tốn quota |
| FE-Services | `L1-FES-07..09` | `api.js` wrapper, `discountTierService`, `vehicleService` |
| FE-Contexts | `L1-FEC-06` | Gộp giỏ tạm chỉ 1 lần dưới StrictMode double-invoke |
| FE-Components | `L1-FCMP-11`, `11b`, `12`, `12b` | `allowGuest` + bảng điều hướng theo role |

**Lỗ hổng của chính doc**: header sheet `AuthService` liệt kê `ForgotPasswordAsync` trong danh sách
"methods" nhưng **không có case nào** trong 25 case AUTH. Đề nghị bổ sung hoặc gỡ khỏi header.

---

## 5. Lỗi CÓ TỪ TRƯỚC, không do đợt này gây ra

### 5.1. FE — 4 file test cũ đang đỏ (5 test)

`src/pages/__tests__/`: `Cart.test.jsx`, `Checkout.test.jsx`, `OrderDetail.test.jsx`, `Profile.test.jsx`.

Nguyên nhân chính: các test này render page **mà không bọc `CartProvider`/`AuthProvider`**
(`Error: useCart must be used within a CartProvider`).

Đã xác minh bằng cách stash `src/test/setup.js` về bản gốc rồi chạy lại: **vẫn đỏ y hệt 5 test**
→ không phải do việc thêm MSW trong đợt này.

### 5.2. BE — 1 assertion lỗi thời (đã sửa)

`L1-GI-04` assert message tiếng Anh `"Goods Issue cannot be posted in its current status."` trong khi
code đã đổi sang tiếng Việt `"Chứng từ đã được Post hoặc bị Hủy trước đó, không thể thao tác lại."`
→ đã cập nhật assertion.

---

## 6. Hạ tầng test đã thêm

| File | Mục đích |
|---|---|
| `VietTien.Tests/TestHelpers/SqliteDbFactory.cs` | `ApplicationDbContext` trên SQLite in-memory cho service dùng raw SQL (`InventoryReservationService`). Kèm `SqliteCompatibleModelCustomizer` gỡ `NEWSEQUENTIALID()` (SQL-Server-only) để `EnsureCreated()` sinh được DDL hợp lệ |
| `VietTien.Tests/TestHelpers/FakeHttpMessageHandler.cs` | Ghi lại request + trả response cấu hình sẵn, cho sheet ExternalIntegrations |
| `VietTien.Tests/TestHelpers/NoOpAuditLogService.cs` | `IAuditLogService` no-op khi test cần service thật nhưng audit không phải đối tượng kiểm tra |
| `VietTien.Tests/TestHelpers/TestData.cs` | Bổ sung factory: `SeedConfig`, `DiscountTier`, `Vehicle`, `WebhookLog`, `AuditLog`, `JobRun`, `MarketingPost` |
| `frontend/src/test/msw/{handlers,server}.js` | MSW server dùng chung; `setup.js` đăng ký `listen`/`resetHandlers`/`close` + polyfill `IntersectionObserver`/`ResizeObserver`/`matchMedia` |

**Lưu ý về `L1-RES-06` (concurrency)**: SQLite in-memory dùng chung 1 connection nên ghi được
serialise hoàn toàn — test này KHÔNG chứng minh được tính atomic dưới row-lock thật của SQL Server.
Nó vẫn bắt được lỗi logic (Available âm / cả hai cùng thành công); kiểm chứng đầy đủ thuộc về **L2**.
