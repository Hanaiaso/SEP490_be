# L3 System/API Test - ket qua tong hop (2026-08-12)

Tong so case trong workbook: **172**

| Trang thai | So case | Ty le |
|---|---:|---:|
| Pass | 149 | 86.6% |
| Fail | 22 | 12.8% |
| Blocked | 1 | 0.6% |

## Cong cu da dung

| Cong cu | Pham vi | Ket qua |
|---|---|---|
| xUnit + WebApplicationFactory + SQL Server local | 158 case hop dong API | 198 dong chay, 194 xanh, 4 do co chu dich |
| Newman (Postman CLI) | Security header, SQLi, 401/403, Swagger | 13 request / 25 assertion (3 do: SEC-15) + 1 request / 4 assertion cho Swagger |
| Apache JMeter 5.6.3 (thay k6) | 8 case hieu nang | xem bang duoi |
| SQL truc tiep | SEC-06, SEC-13 | 1 Pass, 1 Fail |

## So lieu hieu nang do duoc

| Test ID | So mau | p95 (ms) | Nguong (ms) | Ket qua | Loi HTTP |
|---|---:|---:|---:|---|---:|
| L3-PERF-01 | 136903 | 12 | 500 | PASS | 0.0% |
| L3-PERF-02 | 166590 | 11 | 800 | PASS | 0.0% |
| L3-PERF-03 | 90578 | 8 | 1000 | PASS | 0.0% |
| L3-PERF-05 | 100999 | 28 | 3000 | PASS | 0.0% |
| L3-PERF-06 | 83569 | 10 | 3000 | PASS | 0.0% |
| L3-PERF-07 | 117345 | 4 | 1000 | PASS | 0.0% |
| L3-PERF-08 | 149847 | 120 | 5000 | PASS | 0.0% |
| L3-PERF-09 | 78131 | 13 | 3000 | PASS | 0.0% |

## Defect

| Defect ID | Muc | Tom tat |
|---|---|---|
| DEF-L3-001 | P2 | Khong co error registry: 0/195 endpoint tra truong errorCode, trong khi SRS/workbook dinh nghia ~80 ma loi nghiep vu. |
| DEF-L3-002 | P3 | Ma HTTP lech SRS: nhieu nhanh xung dot trang thai tra 400 thay vi 409, gioi han tan suat tra 400 thay vi 429. |
| DEF-L3-003 | P1 | Bao gia da duyet duoc ap cho GIO HANG KHAC: gio 240tr bi tinh thanh 110tr vi CalculateDiscountAsync khong doi chieu gio hien tai voi version da duyet (Quotation.CartId khong duoc dung). |
| DEF-L3-004 | P1 | Module Delivery Trip / POD / thu COD chua trien khai: khong co /api/delivery/trips, /attempts, /collections. |
| DEF-L3-005 | P2 | Thieu chuc nang: multi-pick, phien kiem ke (count-session), xuat NVL san xuat, canh bao ton thap, upload media + chi so bai marketing. |
| DEF-L3-006 | P1 | Broken Access Control (OWASP A01): 4 endpoint DOC cua WarehouseController chi co [Authorize] cap class, khong gioi han vai tro -> Customer doc duoc toan bo hang doi xuat kho, chi tiet don khach khac va pick task. |
| DEF-L3-007 | P1 | Dieu chinh ton kho khong kiem rang buoc: AdjustInventoryAsync chi chan so am, cho phep dat OnHand=0 khi dang co 1.000 Reserved + 1.000 Quarantine -> ton kha dung tho = -2000, bi che boi Math.Max(0,...). |
| DEF-L3-008 | P2 | Upload file chi kiem PHAN MO RONG ten file (do nguoi gui dat), khong kiem magic byte -> file PE/EXE doi duoi .png di lot va duoc luu tru. |
| DEF-L3-009 | P2 | Chua cung hoa lop van chuyen: khong redirect HTTP->HTTPS va thieu ca 3 header HSTS / X-Content-Type-Options / X-Frame-Options. |
| DEF-L3-010 | P1 | AuditLogs KHONG bat bien o muc DB: tai khoan ung dung UPDATE/DELETE duoc ban ghi audit (khong co trigger INSTEAD OF, khong co DENY). |
