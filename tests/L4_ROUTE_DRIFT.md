# L4 — lech giua workbook va code that

Nguon: `Report_5_4_L4-E2ETests_VietTien_v1_3.xlsx` doi chieu voi code ngay 2026-08-12,
phat hien trong luc chay Playwright chu khong phai doc code thuan tuy.

## 1. Lech route / endpoint

| Test ID | Workbook ghi | Thuc te trong code | Ghi chu |
|---|---|---|---|
| L4-CP-02 | `POST /api/webhooks/sepay + HMAC` | `POST /api/webhooks/sepay-callback + header x-sepay-token` | Khac ten va khac cach xac thuc |
| L4-CP-04 | `POST /api/handover-records/{id}/confirm` | `warehouse-confirm + sales-confirm` | Tach thanh 2 endpoint dual-confirm |
| L4-CP-05 | `/sales/delivery/arrangement + /api/delivery/trips` | `Man hinh CO, endpoint KHONG` | Module Delivery Trip chua trien khai |
| L4-UJ-03 | `CEOPortal tab 'price-negotiation'` | `/ceo (tab noi bo, khong co URL rieng)` | CEOPortal dung state tab, khong dieu huong bang URL |
| L4-UJ-06 | `/api/delivery/collections` | `(khong ton tai)` | Thu COD chua co endpoint |
| L4-UJ-08 | `/warehouse/inv-management/quarantine` | `/warehouse/inv-management/quarantine` | Trung khop (con co bi danh /warehouse/quarantine) |
| L4-UJ-11 | `/api/inventory/count-sessions` | `(khong ton tai)` | Phien kiem ke chua trien khai |
| L4-UJ-13 | `/api/inventory/low-stock-alerts` | `(khong ton tai)` | Canh bao ton thap di qua Notifications |
| L4-AM-05 | `/admin/ai-marketing` | `/sales/ai-content-studio` | Route workbook la stub ComingSoon (AdminPortal.tsx:205-207) |
| L4-AM-07 | `/sales/pickup-arrangement` | `(khong ton tai)` | Roi vao catch-all ve SalesDashboard (SalesPortal.tsx:425) |
| L4-AM-09 | `/admin/ai-marketing (duyet)` | `/sales-manager/ai-marketing-approval` | Duyet bai nam o portal Sales Manager |
| L4-AM-10 | `POST /api/marketing-posts/{id}/media` | `(khong ton tai)` | Upload media chua trien khai |
| L4-AM-11 | `/admin/marketing-history + /metrics` | `stub ComingSoon; endpoint metrics khong ton tai` | Chi so tuong tac chua trien khai |

## 2. Lech ten du lieu

| Workbook ghi | Du lieu that | Ghi chu |
|---|---|---|
| `customer1@viettien.test` | `customer.test@viettien.com` | Tai khoan seed, mat khau 123456 |
| `warehouse1@viettien.test` | `warehousestaff.test@viettien.com` | Gan kho WH-DEFAULT |
| `SKU-001` | `WRAP-BB-1M2` | 250.000d, ton 10.000 tai WH-DEFAULT — chon vi de dat moc 10tr/100tr |
| `SKU-002` | `TOOL-CUT-5F` | 25.000d |
| `WH-PROD` | `WH-PE` | KHONG co kho ten WH-PROD; he thong co WH-DEFAULT / WH-TRADE / WH-PE |
| `SUP-01` | `SUP-01 (do seed tao)` | Bang Suppliers ban dau rong |
| `ORD-SMOKE` | `VT<yyyyMMddHHmmssfff>` | Bang Orders ban dau rong; moi don deu do test tao |

## 3. Vai tro khai trong controller nhung KHONG co trong enum SystemRole

`SystemRole` (Models/User.cs:60-70) chi co: Guest, Customer, SalesStaff, SalesManager, WarehouseStaff, AccountingStaff, CEO, Admin.

| Gia tri khai trong `[Authorize(Roles=...)]` | So cho xuat hien | Hau qua |
|---|---:|---|
| `Sales` | 2 (HandoverController) | **sales-confirm khong ai goi duoc -> BR-034 be gay** (DEF-L4-001) |
| `WarehouseManager` | 13 | Thua, khong cap quyen cho ai; chua thay chan nham luong nao |
| `SaleStaff` / `SaleManager` | 5 | Ban sao go nham, vo hai vi da co ten dung di kem |
