# Hướng dẫn thuyết trình — Kiểm thử L3 (System / API Test)

> Tài liệu này là kịch bản trình bày cho đợt chạy `Report_5_3_L3-SystemAPITests_VietTien_v1_3.xlsx`
> (172 test case), thực hiện ngày **12/08/2026** trên nhánh `main`.
>
> Thời lượng gợi ý: **12–15 phút**. Mỗi mục dưới đây tương ứng 1–2 slide.

---

## Slide 1 — L3 là gì và khác gì L1/L2

| Cấp | Kiểm cái gì | Công cụ | Ai chạy được |
|---|---|---|---|
| L1 — Unit | Logic bên trong 1 hàm/service | xUnit + Moq | Không cần DB |
| L2 — Integration | Service + DB thật, transaction, concurrency | xUnit + Testcontainers | Cần Docker |
| **L3 — System/API** | **Toàn bộ ứng dụng qua HTTP công khai**: middleware JWT, RBAC 7 vai trò, validation, mã lỗi, mã HTTP | xUnit + WebApplicationFactory, Postman/Newman, JMeter | Cần server + DB |

**Câu hỏi cốt lõi của L3**: *"Hợp đồng API có đúng như SRS quy định khi hệ thống chạy thật không?"*

---

## Slide 2 — Vì sao không thể chạy workbook "as-is"

Trước khi viết dòng test đầu tiên, nhóm đối chiếu tự động **139 tham chiếu endpoint** trong workbook
với **195 route thật** trích từ 31 controller. Kết quả:

> **93/139 tham chiếu endpoint trong workbook không tồn tại đúng tên trong code.**
> **0/195 endpoint trả về trường `errorCode`** — trong khi workbook kỳ vọng ~80 mã lỗi nghiệp vụ.

Ba dạng lệch:

| Nhóm | Ý nghĩa | Ví dụ | Xử lý |
|---|---|---|---|
| **A** | Có endpoint tương đương, chỉ khác tên | `POST /api/orders` → `POST /api/orders/place-order` | Test bắn vào endpoint thật |
| **B** | Không có endpoint, và **đó là đúng** | `DELETE /api/admin/audit-logs/{id}` không tồn tại → chính là cách BR-048 được bảo đảm | Test khẳng định 404/405 → **Pass** |
| **C** | Thiếu chức năng thật | Module `POST /api/delivery/trips` (chuyến giao / POD / thu COD) | **Fail**, gom thành 1 defect/module |

> **Điểm nhấn khi nói**: nhóm B là phát hiện đáng giá — "không có API" ở đây *là* biện pháp bảo mật,
> không phải thiếu sót. Nếu chỉ nhìn "endpoint không tồn tại → Fail" thì sẽ báo sai 6 case.

---

## Slide 3 — Chuẩn đánh giá đã chốt: **Hybrid**

Có 3 cách chấm, nhóm chọn cách thứ nhất:

1. ✅ **Hybrid** — map sang endpoint thật, assert **hành vi nghiệp vụ thật** (HTTP status + phân
   quyền + hệ quả trong DB). Việc thiếu `errorCode` và lệch mã HTTP gom thành **2 defect hệ thống**
   thay vì ~80 defect lẻ.
2. ❌ Theo SPEC nghiêm ngặt — mọi case đều đỏ, ~100 defect, báo cáo mất tín hiệu.
3. ❌ Chỉ test cái đang có — bỏ sót toàn bộ khoảng trống chức năng.

**Lợi ích của Hybrid**: bảng kết quả phân biệt được *"code sai"* với *"tài liệu lệch"* — hai loại việc
giao cho hai người khác nhau.

---

## Slide 4 — Cách chạy (demo trực tiếp được)

**Bước 0 — dựng môi trường** (một lần):

```bash
powershell -ExecutionPolicy Bypass -File tests\install-tools.ps1
```

Tải Apache JMeter 5.6.3 + cài Newman. *Workbook ghi k6; nhóm dùng JMeter vì máy đã có Java 21 —
**ngưỡng NFR giữ nguyên không đổi**.*

**Bước 1 — chạy 154 case hợp đồng API bằng xUnit** (nhanh nhất, ~40 giây, không cần server):

```bash
dotnet test VietTien.IntegrationTests\VietTien.IntegrationTests.csproj --filter "FullyQualifiedName~VietTien.IntegrationTests.L3" --logger "trx;LogFileName=L3.trx"
```

**Bước 2 — bật server thật** (cho Newman + JMeter):

```bash
powershell -ExecutionPolicy Bypass -File tests\run-local-api.ps1
```

> **Nói rõ ở đây**: script cố ý chạy environment `L3Perf` **chứ không phải `Development`**, để
> KHÔNG nạp `appsettings.Development.json` — file đó trỏ vào **DB thật trên Azure** và chứa API key
> thật của eSMS/Gemini/Cloudinary. Toàn bộ đợt kiểm thử chạy trên SQL Server local (`VietTien22`).

**Bước 3 — Newman** (security header, SQLi, 401/403, hợp đồng Swagger):

```bash
newman run tests\postman\VietTien-L3.postman_collection.json -e tests\postman\VietTien-L3.local.postman_environment.json -r "cli,htmlextra" --reporter-htmlextra-export tests\reports\newman-L3.html
```

**Bước 4 — JMeter** (9 case hiệu năng):

```bash
tools\apache-jmeter-5.6.3\bin\jmeter.bat -n -t tests\jmeter\L3-PERF.jmx -l tests\reports\jmeter-L3.jtl -e -o tests\reports\jmeter-L3
```

**Bước 5 — 2 case chỉ kiểm được ở mức DB**:

```bash
powershell -ExecutionPolicy Bypass -File tests\sql\L3-SEC-06_13.ps1
```

**Bước 6 — sinh bảng kết quả để dán vào Excel**:

```bash
python tools\l3_report.py
```

---

## Slide 5 — Đọc bảng kết quả

File `tests/L3_status_2026-08-12.csv` có đúng **172 dòng, đúng thứ tự sheet `TestCase List`** →
mở bằng Excel, copy cột `Status` + `Defect ID` + `Ghi chú` dán thẳng sang workbook.

**Hai cái bẫy khi đếm** (đã gặp ở đợt L1, ghi trong `VietTien.Tests/DOC_MISMATCHES.md`):

1. Một Test ID có thể ứng với **nhiều dòng chạy** (`[Theory]` nhiều `InlineData`). Chỉ cần 1 nhánh đỏ
   thì cả case ghi **Fail**. Ví dụ `L3-AUTH-04` có 3 dòng nhưng chỉ là 1 case.
2. File `.trx` ghi test bị skip là **`NotExecuted`**, *không phải* `Skipped` — đếm nhầm rất dễ thành Pass.

---

## Slide 6 — 4 test đỏ là **CÓ CHỦ ĐÍCH**

Đây là phần đáng nói nhất: 4 test đỏ không phải test hỏng, mà là **4 lỗi thật của hệ thống**, mỗi
test assert đúng theo SRS và sẽ tự chuyển xanh khi code được sửa.

| Test | Defect | Điều gì đã xảy ra |
|---|---|---|
| `L3_QUO_05` | **DEF-L3-003 (P1)** | Khách được duyệt báo giá 110tr cho giỏ 1 món, rồi **đổi giỏ thành 2 món (240tr)** và vẫn đặt được đơn ở giá **110tr**. `CalculateDiscountAsync` chỉ tìm "báo giá đã duyệt bất kỳ còn hiệu lực của khách này" mà **không đối chiếu với giỏ hiện tại** — trường `Quotation.CartId` có trong model nhưng không được dùng. → Thiệt hại tài chính trực tiếp. |
| `L3_FUL_01` | **DEF-L3-006 (P1)** | `WarehouseController` có `[Authorize]` ở cấp class nhưng **4 endpoint ĐỌC quên gắn role** (mọi endpoint GHI đều có). Hệ quả: **tài khoản Customer đọc được toàn bộ hàng đợi xuất kho + chi tiết đơn của khách khác**. OWASP A01. |
| `L3_INV_04` | **DEF-L3-007 (P1)** | Điều chỉnh tồn kho về 0 trong khi đang giữ 1.000 đơn vị cho đơn khách → tồn khả dụng **thô = −2000**. Sai lệch bị che vì property `AvailableQuantity` có `Math.Max(0, …)`. *Chính workbook đã cảnh báo điều này ở L3-SEC-18: "kiểm tra biểu thức thô, không dùng thuộc tính đã floor về 0".* |
| `L3_SEC_14` | **DEF-L3-008 (P2)** | Upload ảnh chỉ kiểm **phần mở rộng tên file** — thứ do chính người gửi đặt. File PE/EXE đổi đuôi `.png` đi lọt và được lưu trữ. |

Cộng thêm 2 lỗi tìm được ngoài xUnit:

| Nguồn | Defect | Nội dung |
|---|---|---|
| SQL trực tiếp | **DEF-L3-010 (P1)** | Bảng `AuditLogs` **không bất biến**: tài khoản ứng dụng UPDATE/DELETE được bản ghi audit. Vi phạm BR-048/NFR-SEC08. |
| Newman | **DEF-L3-009 (P2)** | Không redirect HTTP→HTTPS và thiếu cả 3 header `HSTS` / `X-Content-Type-Options` / `X-Frame-Options`. |

---

## Slide 7 — Câu hỏi hay gặp & cách trả lời

**"Sao không dùng k6 như workbook ghi?"**
→ Máy chạy kiểm thử có sẵn Java 21 nên JMeter chạy được ngay, không phải cài thêm runtime.
**Ngưỡng NFR giữ nguyên 100%**, chỉ đổi công cụ đo. Đã ghi rõ ở cột Notes.

**"Sao thời lượng chạy tải ngắn hơn workbook?"**
→ Số luồng (VUs) và ngưỡng giữ **đúng** workbook; chỉ rút ngắn thời lượng để 8 case chạy gọn trong
một cửa sổ kiểm thử. p95 vẫn tính trên hàng trăm nghìn mẫu nên có ý nghĩa thống kê.

**"Vì sao dùng SQL Server local mà không phải EF InMemory cho nhanh?"**
→ `OrderService.PlaceOrderAsync` mở transaction thật (`BeginTransactionAsync`). Provider InMemory
không hỗ trợ transaction và sẽ ném lỗi — mọi case đặt hàng/xuất kho/chuyển kho sẽ hỏng **vì lý do hạ
tầng chứ không phải vì nghiệp vụ sai**. L3 phải chạy trên stack thật.

**"Vì sao không dùng hạ tầng L2 có sẵn (Testcontainers)?"**
→ Máy chạy đợt này không bật Docker. Nhóm viết `L3SqlFixture` dùng SQL Server local, **tái sử dụng
nguyên logic reseed của L2** (đã tách ra `SeedDataReplayer` để hai bên dùng chung, không chép lần hai).

**"Chạy test có làm hỏng dữ liệu không?"**
→ Không. L3 dùng DB **riêng** `VietTien22_L3`, mỗi test gọi Respawn xoá sạch rồi nạp lại seed.
DB dev (`VietTienDB`) và DB Azure không bị đụng tới.

**"Có chắc không gọi ra dịch vụ ngoài thật không?"**
→ Có 2 lớp chặn: (1) fixture thay `IEmailService`/`ISmsService`/`IAiGeneratorService`/
`ICloudinaryService`/`IMakeWebhookService` bằng fake; (2) `appsettings.Test.json` ép mọi API key về
rỗng. Test `L3_FLOW_07` còn assert tường minh là không có lệnh gọi ra ngoài nào.

---

## Slide 8 — Kết luận & việc tiếp theo

**Đã làm**: 172/172 case có kết quả, 0 case `Not Run`.

**Ưu tiên sửa (P1, theo thứ tự tác động)**:

1. `DEF-L3-006` — gắn role cho 4 endpoint đọc của `WarehouseController` *(sửa 4 dòng, chặn rò dữ liệu)*.
2. `DEF-L3-003` — đối chiếu giỏ hiện tại với version báo giá đã duyệt trước khi áp giá.
3. `DEF-L3-007` — chặn điều chỉnh tồn làm tồn khả dụng xuống âm.
4. `DEF-L3-010` — chuyển `AuditLogs` sang INSERT-only (trigger hoặc DENY quyền).
5. `DEF-L3-004` — quyết định: triển khai module chuyến giao/POD/COD, hay cập nhật SRS bỏ phạm vi đó.

**Việc cho tài liệu** (không phải việc của lập trình):
cập nhật workbook theo `tests/L3_ENDPOINT_DRIFT.md` — sửa 93 tham chiếu endpoint và quyết định có
làm error registry (`DEF-L3-001`) hay bỏ cột `Expected Error Code` khỏi workbook.
