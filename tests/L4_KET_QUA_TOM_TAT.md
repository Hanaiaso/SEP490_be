# L4 E2E Test — ket qua tong hop (2026-08-12)

Tong so case trong workbook: **47**

| Trang thai | So case | Ty le |
|---|---:|---:|
| Pass | 35 | 74.5% |
| Fail | 4 | 8.5% |
| Blocked | 8 | 17.0% |

## Cong cu da dung

| Cong cu | Pham vi | Ket qua |
|---|---|---|
| Playwright 1.x + Chromium that | 47 case E2E tren 2 viewport (1280x800 va 375x812) | 35 xanh / 4 do / 8 bi chan |
| sqlcmd (SQL Server local) | Doi chieu side-effect va dung trang thai khong tao duoc qua API | Xem L4_mutations_2026-08-12.sql |
| API that (khong mock) | Seed tien dieu kien + kiem tang API cho cac case Permission | — |

## Ket qua theo sheet

| Sheet | Pass | Fail | Blocked |
|---|---:|---:|---:|
| L4-CriticalPaths | 4 | 1 | 1 |
| L4-UserJourneys | 11 | 0 | 3 |
| L4-Permissions | 6 | 1 | 0 |
| L4-SessionManagement | 3 | 2 | 0 |
| L4-Responsive (Optional) | 4 | 0 | 0 |
| L4-AdminMarketing | 7 | 0 | 4 |

## Defect

| Defect ID | Muc | Tom tat | Case lien quan |
|---|---|---|---|
| DEF-L4-001 | P1 | Ban giao 2 chu ky KHONG THE hoan tat: HandoverController.cs:35-36 khai [Authorize(Roles="Sales")] cho sales-confirm, nhung enum SystemRole (Models/User.cs:60-70) khong co gia tri "Sales" (vai tro that la SalesStaff). Moi tai khoan Sales deu bi 403 -> BR-034 khong bao gio du 2 chu ky -> khong xuat kho duoc. Cung loi nay con o "WarehouseManager", "SaleStaff", "SaleManager" trong nhieu controller khac. | L4-CP-04 |
| DEF-L4-002 | P1 | Module Delivery Trip / POD / thu COD chua trien khai: /api/delivery/trips, /api/delivery/collections deu 404. Trung voi DEF-L3-004. | L4-CP-05, L4-UJ-06 |
| DEF-L4-003 | P2 | Thieu chuc nang: phien kiem ke (count-sessions), canh bao ton thap (low-stock-alerts), upload media va chi so bai marketing. Trung voi DEF-L3-005. | L4-AM-10, L4-AM-11, L4-UJ-11, L4-UJ-13 |
| DEF-L4-004 | P2 | Workbook ghi sai route: /admin/ai-marketing va /admin/marketing-history la stub ComingSoon (AdminPortal.tsx:205-207); /sales/pickup-arrangement khong ton tai. Chuc nang marketing that nam o /sales/ai-content-studio va /sales-manager/ai-marketing-approval. | L4-AM-05, L4-AM-07 |
| DEF-L4-005 | P1 | Phan quyen UI khong khop API: API chan Admin phe duyet nghiep vu dung (ceo-decision va inventory adjust deu 403), NHUNG ProtectedRoute (App.tsx:76,79) cho Admin mo ca /ceo/* va /warehouse/* -> Admin van vao duoc man duyet bao gia va dieu chinh ton kho, bam nut roi moi bao loi. Vi pham NFR-SEC03/FT-09 NAC-02. | L4-PM-04 |
| DEF-L4-006 | P2 | **[DA SUA - 2026-08-13]** Khong co mutex khi refresh token: POST /api/auth/refresh-token XOAY VONG refresh token (token cu lap tuc 401 — da do bang probe). authService.fetchWithToken (src/services/authService.js:65-91) khong dong bo, nen khi mot trang ban nhieu request can auth cung luc va access token het han, cac request cham hon dung token da bi xoay -> 401 -> xoa phien va chuyen ve /login. Phu: dong 85 xoa khoa 'user' trong khi AuthContext luu o 'authUser'. Fix: tryRefreshAccessToken dung chung 1 promise in-flight (mutex/dedupe) de moi request 401 dong thoi cho chung 1 lan refresh; khoa localStorage doi thanh 'authUser'. Test moi L1-FES-04 xac nhan refresh-token chi goi 1 lan khi co nhieu request 401 song song. Xem authService.js, authService.test.jsx; commit 6b91723 (SEP490_fe). | L4-SM-01 |
| DEF-L4-007 | P2 | Khong co co che het han snapshot gia gio hang 24 gio (BR-025): sau khi day Carts.UpdatedAt lui 24:00:01, gio van cho thanh toan binh thuong, khong canh bao 'gia da het han' va khong bat lam moi gia. | L4-SM-05 |

## Moi truong chay

- Backend: ASP.NET Core 8, `http://localhost:5050`, `ASPNETCORE_ENVIRONMENT=Development`.
- Database: **SQL Server local `VietTien22_L3`** (KHONG phai Azure — firewall Azure SQL chan IP may chay test, xem L4_HUONG_DAN_THUYET_TRINH.md).
- Frontend: Vite dev server `http://localhost:5173`, proxy `/api` + `/hubs` -> `127.0.0.1:5050`.
- Tai khoan: 11 tai khoan seed `*.test@viettien.com`, mat khau `123456`.
