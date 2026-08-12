# L4 E2E Test — huong dan chay & thuyet trinh

Dot chay 12/08/2026, nhanh `main`, workbook `Report_5_4_L4-E2ETests_VietTien_v1_3.xlsx` (47 case).
Ket qua: **Pass 35 · Fail 4 · Blocked 8**.

---

## 1. Chay nhu the nao

**Buoc 0 — cai cong cu (chi 1 lan):**

```bash
cd D:\SEP_Deploy\SEP490_fe\frontend\e2e && npm ci && npx playwright install chromium
```

Neu `SEP490_fe/frontend/node_modules` chua co thi cai them cho app (dung `npm ci` de KHONG
sua `package-lock.json`):

```bash
cd D:\SEP_Deploy\SEP490_fe\frontend && npm ci
```

**Buoc 1 — bat backend (cua so PowerShell rieng, de chay nen):**

```bash
powershell -ExecutionPolicy Bypass -File D:\SEP_Deploy\SEP490_be\tests\run-l4-api.ps1
```

**Buoc 2 — chay 47 case (Playwright tu bat Vite dev server):**

```bash
cd D:\SEP_Deploy\SEP490_fe\frontend\e2e && npx playwright test
```

**Buoc 3 — dien ket qua vao workbook + sinh bao cao:**

```bash
cd D:\SEP_Deploy\SEP490_be && python tools\l4_report.py
```

**Xem bao cao co anh/video/trace cua tung case do:**

```bash
cd D:\SEP_Deploy\SEP490_fe\frontend\e2e && npx playwright show-report
```

| Buoc | Cong cu | Can server? | Thoi gian | Ket qua xem o |
|---|---|---|---|---|
| 0 | npm + playwright install | Khong | 2 phut | — |
| 1 | run-l4-api.ps1 | (la server) | chay nen | `http://localhost:5050/swagger` |
| 2 | npx playwright test | **Co** | ~3,5 phut | console + `e2e/playwright-report/` |
| 3 | python l4_report.py | Khong | 2 giay | workbook + `tests/L4_*.md` |

---

## 2. Vi sao chay tren DB local chu khong phai Azure

Ke hoach ban dau la chay thang tren Azure SQL that (`viettien.database.windows.net` /
`VietTien2`). **Khong thuc hien duoc**: firewall cua Azure SQL chan IP cua may chay test —

```
Cannot open server 'viettien' requested by the login.
Client with IP address '123.27.14.137' is not allowed to access the server.
```

Backend cung se chet dung loi nay neu tro vao Azure. Vi vay dot nay chay tren
**SQL Server local `VietTien22_L3`**.

Ngoai firewall con mot rui ro thu hai da tranh duoc: `Program.cs:284-291` goi
`db.Database.Migrate()` **vo dieu kien** moi lan khoi dong. Neu ket noi duoc Azure thi chi
viec *bat* backend da lam ALTER schema production truoc khi chay bat ky test nao.

`tests/run-l4-api.ps1` ghi de `ConnectionStrings__DefaultConnection` bang bien moi truong
(thang appsettings trong thu tu cau hinh .NET) nen **khong phai sua file cau hinh nao**.

---

## 3. Bo test nay khong dung vao code ung dung

- `SEP490_fe/frontend/e2e/` co `package.json` + `node_modules` RIENG. `package.json`,
  `package-lock.json` va `vite.config.ts` cua app khong bi sua mot dong nao.
- Khong them `data-testid` vao FE. Selector chi dung `getByRole` / `getByText` / `getByLabel`
  tren DOM san co.
- Khong sua `Program.cs`, `appsettings*.json`, migration, hay `.gitignore` goc.
- Xoa thu muc `e2e/` la repo ve nguyen trang.

---

## 4. Doc ket qua the nao

Bao cao phan biet 3 trang thai, khac nhau ve **y nghia**:

| Trang thai | Nghia | Xu ly |
|---|---|---|
| **Pass** | Chay duoc va dung ky vong workbook | — |
| **Fail** | Co man hinh / co endpoint nhung hanh vi SAI | Phai sua code |
| **Blocked** | Khong co man hinh / endpoint de kiem | Phai lam tinh nang, hoac sua workbook |

8 case Blocked KHONG phai la loi cua dot kiem thu — do la phan chuc nang chua ton tai
(trung phan lon voi DEF-L3-004/005 da bao cao o dot L3) hoac workbook ghi sai route.

Chi tiet tung defect: `tests/L4_KET_QUA_TOM_TAT.md`.
Danh sach lech route/endpoint/du lieu: `tests/L4_ROUTE_DRIFT.md`.

---

## 5. Du lieu do test tao ra

Moi ban ghi test tao ra deu mang dau nhan de tim lai:

- Email: `e2e.l4.<case>.<timestamp>@viettien.test`
- Ten / ghi chu: bat dau bang `E2E-L4`
- Don hang: ghi chu `E2E-L4 don do kiem thu tu dong tao`

Moi lenh SQL GHI truc tiep deu duoc luu vao `tests/L4_mutations_2026-08-12.sql` kem thoi diem
chay, de doi chieu ve sau. Trang thai DB truoc dot chay: `tests/L4_baseline_2026-08-12.csv`.
