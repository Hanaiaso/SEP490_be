# L3 System/API Test — hướng dẫn chạy & thuyết trình

Đợt chạy 12/08/2026, nhánh `main`, workbook `Report_5_3_L3-SystemAPITests_VietTien_v1_3.xlsx` (172 case).

---

## 1. Chạy như thế nào

Mở PowerShell, vào thư mục dự án một lần:

```bash
cd D:\SEP_Deploy\SEP490_be
```

**Bước 0 — cài công cụ (chỉ 1 lần):**

```bash
powershell -ExecutionPolicy Bypass -File tests\install-tools.ps1
```

**Bước 1 — xUnit, 158 case (không cần bật server):**

```bash
dotnet test VietTien.IntegrationTests\VietTien.IntegrationTests.csproj --filter "FullyQualifiedName~VietTien.IntegrationTests.L3" --logger "trx;LogFileName=L3.trx"
```

**Bước 2 — bật server (mở cửa sổ PowerShell THỨ HAI, để chạy nền):**

```bash
powershell -ExecutionPolicy Bypass -File tests\run-local-api.ps1
```

**Bước 3 — Newman (quay lại cửa sổ thứ nhất):**

```bash
newman run tests\postman\VietTien-L3.postman_collection.json -e tests\postman\VietTien-L3.local.postman_environment.json -r "cli,htmlextra" --reporter-htmlextra-export tests\reports\newman-L3.html
```

**Bước 4 — JMeter:**

```bash
tools\apache-jmeter-5.6.3\bin\jmeter.bat -n -t tests\jmeter\L3-PERF.jmx -l tests\reports\jmeter-L3.jtl -e -o tests\reports\jmeter-L3
```

Chạy lại lần 2 phải xoá kết quả cũ trước (JMeter từ chối ghi đè):

```bash
Remove-Item -Recurse -Force tests\reports\jmeter-L3, tests\reports\jmeter-L3.jtl
```

**Bước 5 — 2 case kiểm ở mức database:**

```bash
powershell -ExecutionPolicy Bypass -File tests\sql\L3-SEC-06_13.ps1
```

**Bước 6 — sinh bảng kết quả dán vào Excel:**

```bash
python tools\l3_report.py
```

| Bước | Công cụ | Cần server? | Thời gian | Kết quả xem ở |
|---|---|---|---|---|
| 1 | dotnet test | Không | 40 giây | console + `TestResults\L3.trx` |
| 2 | run-local-api.ps1 | (là server) | chạy nền | `http://localhost:5080` |
| 3 | newman | **Có** | 2 giây | `tests\reports\newman-L3.html` |
| 4 | jmeter | **Có** | 10 phút | `tests\reports\jmeter-L3\index.html` |
| 5 | SQL script | Không | 2 giây | console |
| 6 | python | Không | 2 giây | `tests\L3_status_2026-08-12.csv` |

Xong bước 6 thì Ctrl+C tắt server ở cửa sổ thứ hai.

---

## 2. Tự kiểm chứng

**Chạy 1 case lẻ:**

```bash
dotnet test VietTien.IntegrationTests\VietTien.IntegrationTests.csproj --filter "FullyQualifiedName~L3_AUTH_02" -v n
```

**Chứng minh test không "xanh giả"** — mở `VietTien.IntegrationTests/L3/L3AuthApiTests.cs`, trong `L3_AUTH_02` đổi `HttpStatusCode.BadRequest` thành `HttpStatusCode.OK`, chạy lại → phải **đỏ**. Đổi về như cũ → xanh. Test có thật sự gọi API và assert.

**Tái hiện lỗi P1 DEF-L3-006 bằng 2 request Postman** (cần server ở bước 2):

1. `POST http://localhost:5080/api/auth/login` — body `{"email":"customer.test@viettien.com","password":"123456"}` → lấy `data.accessToken`. Đây là tài khoản **khách hàng**.
2. `GET http://localhost:5080/api/warehouse/orders?tabType=OnlinePending` — header `Authorization: Bearer <token>`.

→ Trả **200 OK kèm danh sách đơn trong kho**. Đáng lẽ phải 403.

---

## 3. L3 là gì

Kiểm **toàn bộ ứng dụng qua HTTP thật**: middleware JWT, RBAC 7 vai trò, validation, mã lỗi, mã HTTP.

Khác L1 (logic trong 1 hàm, không cần DB) và L2 (service + DB thật, không đi qua HTTP).

---

## 4. Vì sao không chạy workbook as-is

Đối chiếu tự động 139 tham chiếu endpoint trong workbook với 195 route thật:

- **93/139 endpoint không tồn tại đúng tên** trong code.
- **0/195 endpoint trả trường `errorCode`** — workbook kỳ vọng ~80 mã lỗi nghiệp vụ.

| Nhóm | Nghĩa | Ví dụ | Xử lý |
|---|---|---|---|
| A (115) | Có endpoint tương đương, khác tên | `POST /api/orders` → `/api/orders/place-order` | Test bắn vào endpoint thật |
| B (7) | Không có endpoint, **và đó là đúng** | `DELETE /api/admin/audit-logs/{id}` không tồn tại → chính là cách BR-048 được bảo đảm | Assert 404/405 → **Pass** |
| C (15) | Thiếu chức năng thật | Module `/api/delivery/trips` | **Fail**, gom 1 defect/module |

**Chuẩn đánh giá: Hybrid** — assert hành vi nghiệp vụ thật; thiếu `errorCode` và lệch mã HTTP gom thành 2 defect hệ thống thay vì ~80 defect lẻ.

---

## 5. Kết quả

| Trạng thái | Số case |
|---|---:|
| Pass | 149 (86.6%) |
| Fail | 22 (12.8%) |
| Blocked | 1 (PERF-04 SignalR) |
| **Not Run** | **0** |

xUnit: 198 dòng chạy, 194 xanh, **4 đỏ có chủ đích** (assert theo SRS, tự xanh khi code được sửa).

| Defect | Mức | Tóm tắt |
|---|---|---|
| DEF-L3-003 | P1 | Báo giá đã duyệt được áp cho **giỏ hàng khác**: giỏ 240tr tính thành 110tr |
| DEF-L3-004 | P1 | Module Delivery Trip / POD / thu COD chưa triển khai |
| DEF-L3-006 | P1 | Customer đọc được toàn bộ hàng đợi xuất kho (OWASP A01) |
| DEF-L3-007 | P1 | Điều chỉnh tồn kho làm tồn khả dụng thô = −2000, bị che bởi `Math.Max(0,…)` |
| DEF-L3-010 | P1 | `AuditLogs` UPDATE/DELETE được ở mức DB |
| DEF-L3-005 | P2 | Thiếu multi-pick, kiểm kê, xuất NVL, cảnh báo tồn thấp, media/metrics marketing |
| DEF-L3-008 | P2 | Upload chỉ kiểm phần mở rộng tên file → `.exe` đổi đuôi `.png` đi lọt |
| DEF-L3-009 | P2 | Không ép HTTPS, thiếu 3 security header |
| DEF-L3-001 | P2 | Không có error registry (0/195 endpoint có `errorCode`) |
| DEF-L3-002 | P3 | Mã HTTP lệch SRS: trả 400 thay vì 409/429 |

Chi tiết: `L3_ENDPOINT_DRIFT.md` (bản lệch đầy đủ) · `L3_status_2026-08-12.csv` (172 dòng dán vào Excel) · `L3_KET_QUA_TOM_TAT.md` (số liệu hiệu năng).

---

## 6. Hỏi đáp nhanh

**Sao dùng JMeter mà không phải k6?** Máy có sẵn Java 21, JMeter chạy ngay; ngưỡng NFR giữ nguyên 100%, chỉ đổi công cụ đo.

**Sao thời lượng chạy ngắn hơn workbook?** Số luồng và ngưỡng đúng workbook, chỉ rút thời lượng để 8 case chạy gọn; p95 vẫn tính trên hàng trăm nghìn mẫu.

**Sao không dùng EF InMemory cho nhanh?** `OrderService.PlaceOrderAsync` mở transaction thật; InMemory không hỗ trợ transaction nên mọi case đặt hàng/xuất kho sẽ hỏng vì hạ tầng chứ không phải vì nghiệp vụ.

**Chạy test có hỏng dữ liệu không?** Không — DB riêng `VietTien22_L3`, mỗi test Respawn xoá sạch rồi nạp lại seed. DB dev và DB Azure không bị đụng.

**Có gọi ra dịch vụ ngoài thật không?** Không — fixture thay email/SMS/AI/Cloudinary/Make bằng fake, `appsettings.Test.json` ép mọi API key về rỗng; test `L3_FLOW_07` assert tường minh.

**4 test đỏ là lỗi test hay lỗi code?** Lỗi code — mỗi test assert đúng theo SRS và sẽ tự chuyển xanh khi sửa.
