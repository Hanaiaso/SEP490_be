# L3 - Danh sach lech giua workbook va code that

Nguon: `Report_5_3_L3-SystemAPITests_VietTien_v1_3.xlsx` doi chieu voi code ngay 2026-08-12.

Doi chieu tu dong: **139 tham chieu endpoint** trong workbook, **93** khong ton tai dung ten trong code.

---

## 1. Lech duong dan endpoint

### Nhom A - co endpoint tuong duong (chi khac ten/method) - 115 tham chieu

| Test ID | Workbook ghi | Code that | Ghi chu |
|---|---|---|---|
| L3-AUTH-01 | `POST /api/auth/register` | `POST /api/auth/register` | Trùng khớp |
| L3-AUTH-02 | `POST /api/auth/register` | `POST /api/auth/register` | Workbook chờ 409 DUPLICATE_IDENTITY; code trả 400 {message} |
| L3-AUTH-03 | `POST /api/auth/verify-email?token=` | `POST /api/auth/verify-otp` | Không có luồng verify-email bằng token; hệ thống dùng OTP 6 số qua email |
| L3-AUTH-04 | `POST /api/auth/verify-otp` | `POST /api/auth/verify-otp` | BVA 5/6/7 ký tự chặn bởi [StringLength(6,6)] trên VerifyOtpDto |
| L3-AUTH-05 | `POST /api/auth/verify-otp` | `POST /api/auth/verify-otp` | Hạn OTP 5 phút (AuthService.cs:85) |
| L3-AUTH-06 | `POST /api/auth/verify-otp` | `POST /api/auth/verify-phone-otp` | Chỉ luồng OTP điện thoại có bộ đếm sai (PhoneOtpMaxFailedAttempts=5); luồng email KHÔNG có |
| L3-AUTH-07 | `POST /api/auth/resend-otp` | `POST /api/auth/resend-otp` | Cooldown 60s có thật; trả 400 chứ không phải 429 |
| L3-AUTH-08 | `POST /api/auth/login` | `POST /api/auth/login` | Trùng khớp |
| L3-AUTH-09 | `POST /api/auth/login` | `POST /api/auth/login` | Trùng khớp - trả 401 thông điệp chung |
| L3-AUTH-10 | `POST /api/auth/refresh` | `POST /api/auth/refresh-token` | Khác tên |
| L3-AUTH-11 | `POST /api/auth/refresh` | `POST /api/auth/refresh-token` | Khác tên |
| L3-AUTH-12 | `GET /api/orders` | `GET /api/orders/my-history` | /api/orders trần không tồn tại; dùng endpoint đã [Authorize] bất kỳ |
| L3-AUTH-13 | `GET /api/orders` | `GET /api/orders/my-history` | Như trên |
| L3-ORD-01 | `POST /api/orders` | `POST /api/orders/place-order` | Khác tên |
| L3-ORD-02 | `POST /api/orders` | `POST /api/orders/place-order` | Khác tên |
| L3-ORD-03 | `POST /api/orders` | `POST /api/orders/place-order` | Khác tên |
| L3-ORD-04 | `POST /api/Cart/items` | `POST /api/Cart/items` | Trùng khớp |
| L3-ORD-05 | `POST /api/orders` | `POST /api/orders/place-order` | Khác tên |
| L3-ORD-06 | `GET /api/orders/checkout-summary` | `GET /api/orders/checkout-summary` | Trùng khớp; response KHÔNG có trường pricingSource |
| L3-ORD-07 | `POST /api/Quotation` | `POST /api/Quotation/from-cart` | Khác tên |
| L3-ORD-08 | `GET /api/orders/{id}` | `GET /api/orders/my-history/{orderId}` | Khác tên |
| L3-PAY-01 | `POST /api/webhooks/sepay` | `POST /api/webhooks/sepay-callback` | Khác tên; xác thực bằng header x-sepay-token chứ không phải HMAC |
| L3-PAY-02 | `POST /api/webhooks/sepay` | `POST /api/webhooks/sepay-callback` | Khác tên |
| L3-PAY-03 | `POST /api/webhooks/sepay` | `POST /api/webhooks/sepay-callback` | Khác tên |
| L3-PAY-04 | `POST /api/webhooks/sepay` | `POST /api/webhooks/sepay-callback` | Khác tên |
| L3-PAY-05 | `POST /api/webhooks/sepay` | `POST /api/webhooks/sepay-callback` | Khác tên |
| L3-PAY-06 | `POST /api/orders/{id}/manual-confirm` | `POST /api/orders/{orderId}/manual-confirm` | Trùng khớp |
| L3-PAY-07 | `POST /api/orders/{id}/manual-confirm` | `POST /api/orders/{orderId}/manual-confirm` | Trùng khớp |
| L3-PAY-08 | `POST /api/orders/{id}/confirm` | `POST /api/orders/sales/{id}/confirm` | Khác tên |
| L3-PAY-09 | `POST /api/orders/{id}/manual-confirm` | `POST /api/orders/{orderId}/manual-confirm` | Trùng khớp |
| L3-CUS-01 | `PUT /api/customers/me` | `PUT /api/customer-profile` | Khác tên |
| L3-CUS-02 | `POST /api/customers/me/addresses` | `POST /api/user/addresses` | Khác tên |
| L3-CUS-03 | `DELETE /api/customers/me/addresses/{id}` | `DELETE /api/user/addresses/{id}` | Khác tên |
| L3-CUS-04 | `POST /api/orders (đơn tạo tay)` | `POST /api/orders/place-direct-order` | Khác tên |
| L3-CUS-05 | `POST /api/orders (đơn tạo tay)` | `POST /api/orders/place-direct-order` | Khác tên |
| L3-QUO-01 | `POST /api/Quotation` | `POST /api/Quotation/from-cart` | Khác tên |
| L3-QUO-02 | `POST /api/Quotation/{id}/versions` | `POST /api/Quotation/{id}/versions` | Trùng khớp |
| L3-QUO-03 | `POST /api/Quotation/{id}/ceo-review` | `POST /api/Quotation/{id}/ceo-decision` | Khác tên |
| L3-QUO-04 | `POST /api/Quotation/{id}/customer-decision` | `POST /api/Quotation/{id}/customer-decision` | Trùng khớp |
| L3-QUO-05 | `POST /api/orders` | `POST /api/orders/place-order` | Khác tên |
| L3-QUO-07 | `GET /api/Quotation/{id}/messages` | `GET /api/Quotation/{id}/messages` | Trùng khớp |
| L3-QUO-08 | `POST /api/Quotation/{id}/customer-decision` | `POST /api/Quotation/{id}/customer-decision` | Trùng khớp |
| L3-FUL-01 | `GET /api/warehouse/orders/fulfillment-orders` | `GET /api/warehouse/orders` | Khác tên |
| L3-FUL-02 | `POST /api/goods-issues/{id}/post` | `POST /api/goods-issues/{id}/post` | Trùng khớp |
| L3-FUL-03 | `POST /api/warehouse/orders/pick-tasks/{id}/complete` | `POST /api/warehouse/orders/pick-tasks/{pickTaskId}/complete` | Trùng khớp |
| L3-FUL-04 | `POST /api/handover-records/{id}/confirm` | `POST /api/handover-records/{id}/warehouse-confirm + /sales-confirm` | Tách thành 2 endpoint dual-confirm |
| L3-FUL-05 | `POST /api/goods-issues/{id}/post` | `POST /api/goods-issues/{id}/post` | Trùng khớp |
| L3-FUL-06 | `POST /api/goods-issues/{id}/post` | `POST /api/goods-issues/{id}/post` | Trùng khớp |
| L3-FUL-07 | `POST /api/warehouse/orders/pick-tasks/{id}/complete` | `POST /api/warehouse/orders/pick-tasks/{pickTaskId}/complete` | Trùng khớp |
| L3-FUL-09 | `POST /api/orders/{id}/confirm` | `POST /api/orders/sales/{id}/confirm` | Khác tên |
| L3-DEL-08 | `POST /api/handover-records/{id}/confirm` | `POST /api/handover-records/{id}/warehouse-confirm + /sales-confirm` | Dual confirm |
| L3-DEL-09 | `GET /api/delivery/trips/{id}` | `GET /api/delivery/orders` | Khác tên - kiểm scope theo Sales |
| L3-PUR-01 | `POST /api/purchase-orders + /{id}/issue` | `POST /api/purchase-orders + POST /{id}/issue` | Trùng khớp |
| L3-PUR-02 | `POST /api/purchase-orders/{id}/issue` | `POST /api/purchase-orders/{id}/issue` | Trùng khớp |
| L3-PUR-04 | `POST /api/purchase-orders/import-excel` | `POST /api/purchase-orders/import/excel` | Khác tên |
| L3-PUR-05 | `POST /api/goods-receipts/{id}/post` | `POST /api/purchase-orders/{id}/receipts/{rId}/post` | Khác tên |
| L3-PUR-07 | `POST /api/goods-receipts/{id}/post` | `POST /api/purchase-orders/{id}/receipts/{rId}/post` | Khác tên |
| L3-PUR-08 | `POST /api/goods-receipts/{id}/post` | `POST /api/purchase-orders/{id}/receipts/{rId}/post` | Khác tên |
| L3-TRF-01 | `POST /api/stock-transfers` | `POST /api/stock-transfers` | Trùng khớp |
| L3-TRF-02 | `POST /api/stock-transfers/{id}/post` | `POST /api/stock-transfers/{id}/dispatch` | Khác tên |
| L3-TRF-03 | `POST /api/stock-transfers/{id}/receive` | `POST /api/stock-transfers/{id}/receive` | Trùng khớp |
| L3-TRF-04 | `POST /api/stock-transfers/{id}/receive` | `POST /api/stock-transfers/{id}/receive` | Trùng khớp |
| L3-TRF-05 | `POST /api/stock-transfers/{id}/post + /receive` | `POST /api/stock-transfers/{id}/dispatch + /receive` | Khác tên |
| L3-TRF-06 | `POST /api/stock-transfers/{id}/post` | `POST /api/stock-transfers/{id}/dispatch` | Khác tên |
| L3-TRF-07 | `POST /api/stock-transfers` | `POST /api/stock-transfers` | Trùng khớp |
| L3-INV-02 | `POST /api/inventory/count-sessions/{id}/lines` | `PUT /api/inventory/{inventoryId}/adjust` | Không có count-session; kiểm biên số lượng qua lệnh điều chỉnh tồn |
| L3-INV-03 | `POST /api/inventory/adjustments/{id}/post` | `PUT /api/inventory/{inventoryId}/adjust` | Khác tên |
| L3-INV-04 | `PUT /api/inventory/{id}` | `PUT /api/inventory/{inventoryId}/adjust` | Khác tên |
| L3-INV-07 | `POST /api/materials/production-issues` | `POST /api/goods-issues/{id}/post` | Kiểm xuất vượt tồn khả dụng qua phiếu xuất kho |
| L3-SA-01 | `POST /api/auth/register` | `POST /api/auth/register` | Round-robin chạy sau verify-otp |
| L3-SA-02 | `POST /api/auth/register` | `POST /api/auth/register` | Trùng khớp |
| L3-SA-03 | `POST /api/sales-change-requests` | `POST /api/sales-change-requests` | Trùng khớp |
| L3-SA-04 | `POST /api/sales-change-requests/{id}/approve` | `PUT /api/sales-change-requests/{id}/approve` | Khác method (POST vs PUT) |
| L3-SA-05 | `POST /api/sales-change-requests/{id}/approve` | `PUT /api/sales-change-requests/{id}/approve` | Khác method |
| L3-SA-06 | `POST /api/sales-change-requests/{id}/approve` | `PUT /api/sales-change-requests/{id}/approve` | Khác method |
| L3-AS-03 | `POST /api/orders (dùng credit)` | `POST /api/orders/place-order` | Khác tên |
| L3-AS-04 | `POST /api/orders (dùng credit)` | `POST /api/orders/place-order` | Khác tên |
| L3-AS-05 | `POST /api/warehouse/orders/quality-returns/{id}/release` | `POST /api/warehouse-management/quarantine/{id}/dispatch` | Khác tên |
| L3-AS-06 | `POST /api/orders (dùng credit)` | `POST /api/orders/place-order` | Khác tên |
| L3-AS-07 | `POST /api/payment-reallocations` | `POST /api/delivery/{id}/approve-cancel-replacement` | Khác tên - kiểm phân quyền duyệt huỷ/thay thế |
| L3-AS-08 | `POST /api/orders/{id}/replacement` | `POST /api/delivery/exchange/{requestId}/replacement` | Khác tên |
| L3-ADM-01 | `GET /api/dashboards/sales-manager` | `GET /api/dashboards/sales-manager` | Trùng khớp |
| L3-ADM-02 | `POST /api/Quotation/{id}/ceo-review` | `POST /api/Quotation/{id}/ceo-decision` | Khác tên |
| L3-ADM-04 | `PUT /api/admin/configurations/discount-tier` | `PUT /api/admin/discount-tiers/{id}` | Khác tên |
| L3-ADM-05 | `GET /api/Notifications` | `GET /api/Notifications` | Trùng khớp |
| L3-ADMC-01 | `POST /api/admin/users` | `POST /api/admin/users` | Trùng khớp |
| L3-ADMC-02 | `POST /api/admin/users` | `POST /api/admin/users` | Trùng khớp |
| L3-ADMC-03 | `PUT /api/admin/users/{id}/role` | `PUT /api/admin/users/{id}/role` | Trùng khớp |
| L3-ADMC-04 | `PUT /api/admin/system-configs/{key}` | `PUT /api/admin/system-configs/{key}` | Trùng khớp |
| L3-ADMC-05 | `GET /api/admin/system-configs/{key}/effective?at=` | `GET /api/admin/system-configs/{key}/history` | Không có tra cứu theo mốc thời gian; chỉ có lịch sử phiên bản |
| L3-ADMC-06 | `PUT /api/admin/system-configs/{key}` | `PUT /api/admin/system-configs/{key}` | Trùng khớp |
| L3-ADMC-08 | `GET /api/admin/audit-logs/export` | `GET /api/admin/audit-logs/export` | Trùng khớp |
| L3-ADMC-09 | `GET /api/vehicles?active=true` | `GET /api/vehicles` | Trùng khớp |
| L3-ADMC-10 | `POST /api/vehicles` | `POST /api/vehicles` | Trùng khớp |
| L3-ADMC-11 | `GET /api/admin/discount-tiers/applicable?subtotal=` | `GET /api/admin/discount-tiers` | Không có endpoint applicable; bậc áp dụng tính trong checkout-summary |
| L3-ADMC-12 | `POST /api/admin/discount-tiers` | `POST /api/admin/discount-tiers` | Trùng khớp |
| L3-ADMC-13 | `GET /api/dashboards/sales-staff?staffId=` | `GET /api/dashboards/sales-staff` | Không nhận tham số staffId - phạm vi luôn lấy từ JWT |
| L3-ADMC-14 | `GET /api/admin/system-health/jobs` | `GET /api/admin/system-health/job-runs` | Khác tên |
| L3-ADMC-15 | `POST /api/admin/system-health/webhook-logs/{id}/retry` | `POST /api/admin/system-health/webhook-logs/{id}/retry` | Trùng khớp |
| L3-ADMC-16 | `GET /api/admin/system-health/jobs` | `GET /api/admin/system-health/job-runs` | Khác tên |
| L3-ADMC-17 | `GET /swagger/v1/swagger.json` | `GET /swagger/v1/swagger.json` | Chỉ bật khi ASPNETCORE_ENVIRONMENT=Development (Program.cs:293) |
| L3-MKT-01 | `POST /api/marketing-posts` | `POST /api/marketing-posts` | Trùng khớp |
| L3-MKT-02 | `POST /api/marketing-posts/{id}/approve` | `POST /api/marketing-posts/{id}/decision` | Khác tên |
| L3-MKT-03 | `POST /api/marketing-posts/{id}/publish` | `POST /api/marketing-posts/{id}/publish-now` | Khác tên |
| L3-MKT-04 | `POST /api/marketing-posts/{id}/publish` | `POST /api/marketing-posts/{id}/publish-now` | Khác tên |
| L3-MKT-05 | `POST /api/marketing-posts/{id}/make-callback` | `POST /api/marketing-posts/{id}/webhook-callback` | Khác tên |
| L3-MKT-06 | `POST /api/marketing-posts/{id}/make-callback` | `POST /api/marketing-posts/{id}/webhook-callback` | Khác tên |
| L3-MKT-07 | `POST /api/marketing-posts/{id}/approve` | `POST /api/marketing-posts/{id}/decision` | Khác tên |
| L3-MKT-08 | `POST /api/marketing-posts/{id}/approve` | `POST /api/marketing-posts/{id}/decision` | Khác tên |
| L3-RET-01 | `POST /api/orders/{id}/return-exchange` | `POST /api/orders/{id}/exchange-request` | Khác tên |
| L3-RET-02 | `POST /api/orders/{id}/return-exchange` | `POST /api/orders/{id}/exchange-request` | Khác tên |
| L3-RET-03 | `POST /api/orders/return-exchanges/{id}/process` | `POST /api/orders/exchange-request/{requestId}/process` | Khác tên |
| L3-RET-04 | `POST /api/orders/{id}/cancel-paid` | `POST /api/delivery/{orderId}/request-cancel` | Khác tên |
| L3-RET-05 | `POST /api/orders/return-exchanges/{id}/confirm-pickup` | `POST /api/delivery/pickups/{requestId}/confirm` | Khác tên |
| L3-RET-06 | `POST /api/orders/return-exchanges/{id}/confirm-pickup` | `POST /api/delivery/pickups/{requestId}/confirm` | Khác tên |

### Nhom B - KHONG co endpoint, va do la DUNG (bat bien bao dam bang viec khong mo route) - 7 tham chieu

| Test ID | Workbook ghi | Code that | Ghi chu |
|---|---|---|---|
| L3-QUO-06 | `PUT /api/Quotation/{id}/messages/{msgId}` | `(không tồn tại)` | Bất biến bảo đảm bằng việc KHÔNG mở route sửa tin nhắn - đúng BR-020 |
| L3-PUR-03 | `POST /api/inventory/post-from-po` | `(không tồn tại)` | Đúng BR-014: không có đường tăng tồn thẳng từ PO |
| L3-PUR-06 | `PUT /api/goods-receipts/{id}` | `(không tồn tại)` | Đúng BR-021: không mở route sửa receipt |
| L3-SA-07 | `PUT /api/orders/{id}/assigned-sales` | `(không tồn tại)` | Đúng BR-031: không mở route ghi đè snapshot Sales trên đơn |
| L3-AS-01 | `POST /api/orders/{id}/refund` | `(không tồn tại)` | Đúng BR-017: hệ thống không hỗ trợ hoàn tiền |
| L3-ADM-03 | `DELETE /api/admin/audit-logs/{id}` | `(không tồn tại)` | Đúng BR-048: AuditLogController chỉ có GET |
| L3-ADMC-07 | `DELETE /api/admin/audit-logs/{id}` | `(không tồn tại)` | Như ADM-03 |

### Nhom C - thieu chuc nang that (endpoint khong ton tai va can co) - 15 tham chieu

| Test ID | Workbook ghi | Code that | Ghi chu |
|---|---|---|---|
| L3-FUL-08 | `POST /api/warehouse/orders/multi-pick` | `(không tồn tại)` | Chưa triển khai multi-pick |
| L3-DEL-01 | `POST /api/delivery/trips` | `(không tồn tại)` | Module Delivery Trip chưa triển khai - gần nhất là POST /api/delivery/schedule |
| L3-DEL-02 | `POST /api/delivery/trips` | `(không tồn tại)` | Như trên |
| L3-DEL-03 | `POST /api/delivery/trips/{id}/start` | `(không tồn tại)` | Như trên |
| L3-DEL-04 | `POST /api/delivery/attempts` | `(không tồn tại)` | POD chưa có endpoint riêng - gần nhất POST /api/delivery/{orderId}/complete |
| L3-DEL-05 | `POST /api/delivery/collections` | `(không tồn tại)` | Thu COD chưa có endpoint riêng |
| L3-DEL-06 | `POST /api/delivery/collections` | `(không tồn tại)` | Như trên |
| L3-DEL-07 | `POST /api/delivery/attempts` | `(không tồn tại)` | Như trên |
| L3-INV-01 | `PUT /api/inventory/count-sessions/{id}/theoretical` | `(không tồn tại)` | Phiên kiểm kê chưa có; gần nhất POST /api/inventory/shift-count |
| L3-INV-05 | `POST /api/materials/production-issues` | `(không tồn tại)` | Xuất vật tư sản xuất chưa triển khai |
| L3-INV-06 | `GET /api/inventory/low-stock-alerts` | `(không tồn tại)` | Cảnh báo tồn thấp không có endpoint riêng; đi qua Notifications |
| L3-AS-02 | `POST /api/payment-reallocations` | `(không tồn tại)` | Phân bổ lại tiền chưa có API riêng; gần nhất POST /api/delivery/{id}/approve-cancel-replacement |
| L3-MKT-09 | `POST /api/marketing-posts/{id}/media` | `(không tồn tại)` | Upload media riêng chưa có |
| L3-MKT-10 | `POST /api/marketing-posts/{id}/media` | `(không tồn tại)` | Như MKT-09 |
| L3-MKT-11 | `GET /api/marketing-posts/{id}/metrics` | `(không tồn tại)` | Chỉ số reach/reaction chưa triển khai |

---

## 2. Lech errorCode

Workbook ky vong ~80 ma loi nghiep vu trong than phan hoi. Code **khong co truong `errorCode` o bat ky endpoint nao** - moi controller tra `{ message }`.
Ngoai le duy nhat: `CartController.cs:61` tra `{ code = "PROFILE_INCOMPLETE" }`.

Bang chung: test `L3_DRIFT_001_NoEndpointReturnsErrorCodeField` quet 9 nhanh loi 4xx trai khap 8 controller, khong nhanh nao co truong errorCode.

| errorCode workbook ky vong | HTTP workbook | HTTP code that | Than phan hoi that |
|---|---|---|---|
| `DUPLICATE_IDENTITY` | 409 | 400 | { message: "Email nay da duoc su dung." } |
| `OTP_INVALID` | 400 | 400 | { message: "Ma OTP khong chinh xac." } |
| `OTP_EXPIRED` | 400 | 400 | { message: "Ma OTP da het han..." } |
| `OTP_ATTEMPT_LIMIT_REACHED` | 409 | 400 | { message: "...nhap sai qua so lan..." } |
| `OTP_RESEND_TOO_SOON` | 429 | 400 | { message: "Vui long doi it nhat 60 giay..." } |
| `PRICE_SNAPSHOT_EXPIRED_OR_STOCK_CHANGED` | 409 | 400 | { message: "Gia trong gio hang da het han giu (qua 24h)..." } |
| `PRODUCT_NOT_ORDERABLE` | 409 | 400 | { message: "Product not found or discontinued." } |
| `CLIENT_PRICE_MISMATCH` | 400 | n/a | Server bo qua gia client gui, tu tinh lai - khong sinh loi |
| `QUOTATION_NOT_ELIGIBLE` | 409 | 400 | { message: "..." } |
| `RESOURCE_FORBIDDEN` | 403 | 403 | Than rong hoac { message } |
| `SEPAY_SIGNATURE_INVALID` | 401 | 401 | { success: false, message: "Missing Token" } |
| `PAYMENT_EVENT_ALREADY_PROCESSED` | 200 | 200 | { success: true } - tra ket qua goc, khong co ma |
| `GOODS_ISSUE_PREREQUISITE_NOT_MET` | 409 | 400 | { message: "[Ton kho khong du]..." } |
| `POSTED_DOCUMENT_IMMUTABLE` | 409 | 409 | { message: "Chung tu da duoc Post hoac bi Huy..." } |
| `INVENTORY_INVARIANT_VIOLATION` | 409 | 400 | { message: "[Ton kho khong du]..." } |
| `TRANSFER_VALIDATION_FAILED` | 400 | 400 | ProblemDetails cua ModelState (title/errors) |
| `CONFIGURATION_RETROACTIVE_CHANGE_FORBIDDEN` | 409 | 400 | { message: "Khong duoc dat ngay hieu luc vao qua khu..." } |
| `AUDIT_LOG_IMMUTABLE` | 403 | 404/405 | Khong co route DELETE audit-log |

---

## 3. Defect tong hop

| Defect ID | Muc | Tom tat | Bang chung |
|---|---|---|---|
| DEF-L3-001 | P2 | Khong co error registry: 0/195 endpoint tra truong errorCode, trong khi SRS/workbook dinh nghia ~80 ma loi nghiep vu. | `L3_DRIFT_001_NoEndpointReturnsErrorCodeField` |
| DEF-L3-002 | P3 | Ma HTTP lech SRS: nhieu nhanh xung dot trang thai tra 400 thay vi 409, gioi han tan suat tra 400 thay vi 429. | `L3_DRIFT_002_BusinessErrorsUse400WhereSrsExpects409Or429` |
| DEF-L3-003 | P1 | Bao gia da duyet duoc ap cho GIO HANG KHAC: gio 240tr bi tinh thanh 110tr vi CalculateDiscountAsync khong doi chieu gio hien tai voi version da duyet (Quotation.CartId khong duoc dung). | `L3_QUO_05_...MustBeRejected` - OrderService.cs:88-103 |
| DEF-L3-004 | P1 | Module Delivery Trip / POD / thu COD chua trien khai: khong co /api/delivery/trips, /attempts, /collections. | L3-DEL-01..07 |
| DEF-L3-005 | P2 | Thieu chuc nang: multi-pick, phien kiem ke (count-session), xuat NVL san xuat, canh bao ton thap, upload media + chi so bai marketing. | L3-FUL-08, L3-INV-01/05/06, L3-MKT-09/10/11 |
| DEF-L3-006 | P1 | Broken Access Control (OWASP A01): 4 endpoint DOC cua WarehouseController chi co [Authorize] cap class, khong gioi han vai tro -> Customer doc duoc toan bo hang doi xuat kho, chi tiet don khach khac va pick task. | `L3_FUL_01_...` - WarehouseController.cs dong 12, 34, 51, 115, 132 |
| DEF-L3-007 | P1 | Dieu chinh ton kho khong kiem rang buoc: AdjustInventoryAsync chi chan so am, cho phep dat OnHand=0 khi dang co 1.000 Reserved + 1.000 Quarantine -> ton kha dung tho = -2000, bi che boi Math.Max(0,...). | `L3_INV_04_...MustNotDriveAvailableNegative` - InventoryService.cs:97-122 |
| DEF-L3-008 | P2 | Upload file chi kiem PHAN MO RONG ten file (do nguoi gui dat), khong kiem magic byte -> file PE/EXE doi duoi .png di lot va duoc luu tru. | `L3_SEC_14_...MustBeRejected` - UserProfileService.cs:83-85 |
| DEF-L3-009 | P2 | Chua cung hoa lop van chuyen: khong redirect HTTP->HTTPS va thieu ca 3 header HSTS / X-Content-Type-Options / X-Frame-Options. | Newman L3-SEC-05, L3-SEC-15 |
| DEF-L3-010 | P1 | AuditLogs KHONG bat bien o muc DB: tai khoan ung dung UPDATE/DELETE duoc ban ghi audit (khong co trigger INSTEAD OF, khong co DENY). | tests/sql/L3-SEC-06_13.ps1 - L3-SEC-13 |
