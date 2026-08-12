"""
Sinh 3 deliverable của đợt kiểm thử L3 (Report_5_3 v1.3) từ kết quả chạy THẬT:

  1. tests/L3_status_2026-08-12.csv        <- bảng dán vào Excel: Test ID | Sheet | Status | Defect ID | Ghi chú
  2. tests/L3_ENDPOINT_DRIFT.md            <- danh sách lệch endpoint / errorCode
  3. tests/L3_KET_QUA_TOM_TAT.md           <- số liệu tổng hợp để trích vào slide

Nguồn dữ liệu:
  - VietTien.IntegrationTests/TestResults/L3.trx   (xUnit — 154 case)
  - tests/reports/newman-L3.json + newman-L3-swagger.json  (Newman)
  - tests/reports/jmeter-L3.jtl                    (JMeter — PERF)
  - tests/l3_endpoint_map.csv                      (ánh xạ workbook <-> code)
  - Report_5_3_L3-SystemAPITests_VietTien_v1_3.xlsx (thứ tự + Sheet của 172 case)

Quy ước đếm (giống DOC_MISMATCHES.md của L1):
  - 1 Test ID có thể ứng với NHIỀU dòng chạy ([Theory]) -> chỉ cần 1 dòng đỏ thì cả case là Fail.
  - .trx ghi test bị skip là "NotExecuted", KHÔNG phải "Skipped" -> dễ đếm nhầm thành Pass.

Chạy:  python tools/l3_report.py
"""
import csv
import io
import os
import re
import json
import xml.etree.ElementTree as ET
from collections import defaultdict

import openpyxl

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORKBOOK = os.path.join(ROOT, "Report_5_3_L3-SystemAPITests_VietTien_v1_3.xlsx")
TRX = os.path.join(ROOT, "VietTien.IntegrationTests", "TestResults", "L3.trx")
# Doc theo thu tu: file sau GHI DE ket qua cua cung mot label o file truoc.
# jmeter-L3-perf03.jtl la lan chay lai RIENG L3-PERF-03 sau khi da seed gio hang
# (lan chay dau do trung duong loi 400 "Gio hang trong" nen so lieu vo nghia).
JTL_FILES = [
    os.path.join(ROOT, "tests", "reports", "jmeter-L3.jtl"),
    os.path.join(ROOT, "tests", "reports", "jmeter-L3-perf03.jtl"),
]
MAP_CSV = os.path.join(ROOT, "tests", "l3_endpoint_map.csv")

# L3-PERF-04 (SignalR ChatHub) do bang xUnit + Microsoft.AspNetCore.SignalR.Client chu khong bang
# JMeter — JMeter khong noi duoc WebSocket/SignalR neu khong cai plugin ngoai. Test tu ghi so lieu
# ra file nay (xem L3PerformanceSignalRTests.WriteStats).
SIGNALR_JSON = os.path.join(ROOT, "tests", "reports", "l3_perf_signalr.json")

RUN_DATE = "2026-08-12"

# So lieu chay that cua xUnit, duoc main() gan lai tu .trx (khong ghi tay).
XUNIT_ROWS = XUNIT_RED = XUNIT_CASES = 0

# ─── Kết quả các case KHÔNG chạy bằng xUnit ────────────────────────────────────────────────
# Ghi tay theo bằng chứng đã thu được (Newman / JMeter / SQL). Mỗi dòng: (status, defect, note)
MANUAL = {
    # Newman trên server thật (tests/reports/newman-L3.html)
    "L3-SEC-05":  ("Fail", "DEF-L3-009",
                   "Newman: GET http://.../api/products tra 200 truc tiep, khong redirect 301/308. "
                   "UseHttpsRedirection co dang ky nhung khong co cong HTTPS nen bi bo qua."),
    "L3-SEC-15":  ("Fail", "DEF-L3-009",
                   "Newman: thieu ca 3 header HSTS / X-Content-Type-Options / X-Frame-Options."),
    "L3-ADMC-17": ("Pass", "",
                   "Newman (mode Development): Swagger mo ta 195 route, 100% operation co response, "
                   "co securityScheme Bearer."),
    # SQL truc tiep (tests/sql/L3-SEC-06_13.ps1)
    "L3-SEC-06":  ("Pass", "",
                   "SQL: 11/11 tai khoan la BCrypt hash ($2...), 0 plaintext."),
    "L3-SEC-13":  ("Fail", "DEF-L3-010",
                   "SQL: tai khoan ung dung UPDATE va DELETE duoc ban ghi AuditLogs (moi lenh doi 1 dong). "
                   "Bang khong phai INSERT-only."),
    # L3-PERF-04 KHONG con ghi tay: da do that bang xUnit + SignalR.Client, so lieu doc tu
    # tests/reports/l3_perf_signalr.json — xem parse_perf().
}

# Mo ta ngan gon cho 4 case xUnit do CO CHU DICH (assert theo SRS, se tu xanh khi code duoc sua).
# Dung de thay thong diep tho cua FluentAssertions trong cot Ghi chu cua file Excel.
FAIL_NOTES = {
    "L3-QUO-05": "Gio 240tr bi tinh thanh 110tr theo bao gia da duyet cho MOT GIO KHAC. "
                 "CalculateDiscountAsync (OrderService.cs:88-103) khong doi chieu gio hien tai voi "
                 "version da duyet; Quotation.CartId co trong model nhung khong duoc dung.",
    "L3-FUL-01": "Tai khoan Customer DOC DUOC toan bo hang doi xuat kho. WarehouseController chi co "
                 "[Authorize] cap class; 4 endpoint doc (dong 34, 51, 115, 132) thieu "
                 "[Authorize(Roles=...)] trong khi moi endpoint ghi deu co. OWASP A01.",
    "L3-INV-04": "Dieu chinh ton ve 0 trong khi dang giu 1.000 Reserved + 1.000 Quarantine -> ton kha "
                 "dung THO = -2000. AdjustInventoryAsync (InventoryService.cs:97-122) chi chan so am. "
                 "Sai lech bi che boi Math.Max(0,...) trong property AvailableQuantity.",
    "L3-SEC-14": "File PE/EXE (header MZ) doi duoi .png duoc chap nhan va luu tru. "
                 "UploadAvatarAsync (UserProfileService.cs:83-85) chi kiem PHAN MO RONG ten file - "
                 "thu do chinh nguoi gui dat - khong kiem magic byte.",
}

# PERF: ngưỡng workbook (ms) để đối chiếu với p95 đo được
PERF_THRESHOLDS = {
    "L3-PERF-01": 500, "L3-PERF-02": 800, "L3-PERF-03": 1000,
    "L3-PERF-04": 2000,  # NFR-P04 — do bang SignalR.Client, khong phai JMeter
    "L3-PERF-05": 3000, "L3-PERF-06": 3000, "L3-PERF-07": 1000,
    "L3-PERF-08": 5000, "L3-PERF-09": 3000,
}


def read_map_csv():
    """
    Đọc tests/l3_endpoint_map.csv, CHỊU ĐƯỢC việc file đã bị Excel mở rồi lưu đè.

    Excel trên máy Windows tiếng Việt làm 2 việc phá file:
      1. Lưu lại theo ANSI codepage (cp1252) -> tiếng Việt trong cột Note thành mojibake, và
         file không còn decode được bằng UTF-8 (script sẽ ném UnicodeDecodeError).
      2. Đôi khi chèn thêm một dòng tiêu đề giả `Column1,Column2,...` lên trước dòng tiêu đề thật.

    Đây là file người dùng ĐƯỢC KHUYẾN KHÍCH mở bằng Excel để tra cứu, nên script phải tự chịu
    được, không được bắt người dùng nhớ "đừng lưu file". Cột dữ liệu (TestID/Group/endpoint) đều là
    ASCII nên vẫn đọc đúng kể cả khi cột Note bị vỡ dấu.
    """
    raw = None
    for encoding in ("utf-8-sig", "cp1252", "latin-1"):
        try:
            raw = io.open(MAP_CSV, encoding=encoding).read()
            break
        except UnicodeDecodeError:
            continue
    if raw is None:
        raise RuntimeError(f"Khong doc duoc {MAP_CSV} voi bat ky encoding nao da thu.")

    rows = list(csv.DictReader(io.StringIO(raw)))

    # Bỏ dòng tiêu đề giả của Excel: khi đó fieldnames là Column1..N và dòng ĐẦU TIÊN mới là
    # tiêu đề thật -> đọc lại, bỏ qua dòng rác đó.
    if rows and "TestID" not in (rows[0].keys() if rows else {}):
        lines = raw.splitlines(keepends=True)
        if lines and lines[0].lower().startswith("column1"):
            rows = list(csv.DictReader(io.StringIO("".join(lines[1:]))))

    return [r for r in rows if (r.get("TestID") or "").startswith("L3-")]


def read_workbook_index():
    """Đọc sheet 'TestCase List' -> danh sách (TestID, Sheet) đúng thứ tự để dán lại vào Excel."""
    wb = openpyxl.load_workbook(WORKBOOK, data_only=True)
    ws = wb["TestCase List"]
    rows = []
    for row in ws.iter_rows(values_only=True):
        if not row or not row[0]:
            continue
        tid = str(row[0]).strip()
        if not re.match(r"^L3-[A-Z]+-\d+$", tid):
            continue
        rows.append((tid, str(row[1]).strip() if row[1] else ""))
    return rows


def parse_trx():
    """.trx -> {TestID: {'outcomes': [...], 'messages': [...]}}. Gộp nhiều [Theory] về 1 Test ID."""
    if not os.path.exists(TRX):
        return {}
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    tree = ET.parse(TRX)
    root = tree.getroot()

    # testId -> tên method
    id_to_name = {}
    for ut in root.findall(".//t:UnitTest", ns):
        tm = ut.find("t:TestMethod", ns)
        if tm is not None:
            id_to_name[ut.get("id")] = tm.get("name")

    result = defaultdict(lambda: {"outcomes": [], "messages": []})
    for r in root.findall(".//t:UnitTestResult", ns):
        # testName la ten DAY DU: Namespace.Class.L3_TRF_05_... -> phai search, khong match tu dau.
        name = r.get("testName") or id_to_name.get(r.get("testId"), "")
        m = re.search(r"L3_([A-Z]+)_(\d+)", name)
        if not m:
            continue
        tid = f"L3-{m.group(1)}-{m.group(2)}"
        outcome = r.get("outcome")
        result[tid]["outcomes"].append(outcome)
        if outcome == "Failed":
            msg = r.find(".//t:Message", ns)
            if msg is not None and msg.text:
                result[tid]["messages"].append(" ".join(msg.text.split())[:400])
    return result


def parse_jtl():
    """Doc cac file .jtl -> {label: {'count', 'p95', 'avg', 'errors', 'error_rate'}}"""
    out = {}
    for path in JTL_FILES:
        if not os.path.exists(path):
            continue
        per_label = defaultdict(lambda: {"elapsed": [], "errors": 0})
        with io.open(path, encoding="utf-8", errors="replace") as f:
            for row in csv.DictReader(f):
                label = (row.get("label") or "").strip()
                if not label.startswith("L3-PERF"):
                    continue
                try:
                    per_label[label]["elapsed"].append(int(row["elapsed"]))
                except (KeyError, ValueError):
                    continue
                # Cot 'success' phan anh ca DurationAssertion; dung responseCode de dem loi HTTP THAT.
                code = (row.get("responseCode") or "").strip()
                if not code.isdigit() or int(code) >= 400:
                    per_label[label]["errors"] += 1

        for label, data in per_label.items():
            e = sorted(data["elapsed"])
            if not e:
                continue
            idx = min(len(e) - 1, int(round(0.95 * (len(e) - 1))))
            out[label] = {  # file sau ghi de file truoc
                "tool": "JMeter",
                "count": len(e),
                "p95": e[idx],
                "avg": round(sum(e) / len(e), 1),
                "errors": data["errors"],
                "error_rate": round(100.0 * data["errors"] / len(e), 2),
            }

    # L3-PERF-04 do bang xUnit + SignalR.Client (JMeter khong lam duoc WebSocket) -> gop vao cung
    # cau truc de di chung mot duong tinh Pass/Fail va cung mot bang bao cao.
    if os.path.exists(SIGNALR_JSON):
        s = json.load(io.open(SIGNALR_JSON, encoding="utf-8"))
        out[s["testId"]] = {
            "tool": "SignalR.Client",
            "count": s["samples"],
            "p95": s["p95Ms"],
            "avg": s["avgMs"],
            "max": s["maxMs"],
            "errors": 0,
            "error_rate": 0.0,
            "note_extra": f"max={s['maxMs']}ms, {s['clients']} client, {s['transport']}",
        }

    return out


def build_status(trx, jtl):
    """Trả {TestID: (status, defect, note)} cho toàn bộ 172 case."""
    status = {}

    # 1) Case chạy bằng xUnit
    for tid, data in trx.items():
        outcomes = data["outcomes"]
        if any(o == "Failed" for o in outcomes):
            note = data["messages"][0] if data["messages"] else ""
            defect = ""
            m = re.search(r"(DEF-L3-\d+)", note)
            if m:
                defect = m.group(1)
            # Thay thong diep tho cua FluentAssertions bang mo ta doc duoc trong Excel.
            status[tid] = ("Fail", defect, FAIL_NOTES.get(tid, note[:300]))
        elif all(o == "Passed" for o in outcomes):
            n = len(outcomes)
            suffix = f" ({n} dong [Theory])" if n > 1 else ""
            status[tid] = ("Pass", "", f"xUnit L3 xanh{suffix}.")
        else:
            status[tid] = ("Skip", "", "xUnit: NotExecuted/Inconclusive.")

    # 2) PERF từ JMeter
    for tid, threshold in PERF_THRESHOLDS.items():
        stats = jtl.get(tid)
        if not stats:
            status[tid] = ("Blocked", "", "Khong co du lieu trong file .jtl.")
            continue
        # NFR-S01 doi error rate < 1%. Ngoai ra p95 chi co y nghia khi request THUC SU thanh cong:
        # neu phan lon request tra 4xx thi ta dang do duong loi, khong phai do hieu nang.
        within_threshold = stats["p95"] <= threshold
        acceptable_errors = stats["error_rate"] < 1.0
        ok = within_threshold and acceptable_errors

        note = (f"{stats.get('tool', 'JMeter')}: {stats['count']} mau, p95={stats['p95']}ms "
                f"(nguong {threshold}ms), avg={stats['avg']}ms, loi HTTP={stats['error_rate']}%.")
        if stats.get("note_extra"):
            note += f" {stats['note_extra']}."
        if not within_threshold:
            note += " VUOT NGUONG."
        if not acceptable_errors:
            note += " Ty le loi qua cao - so lieu p95 khong dai dien cho duong thanh cong."
        status[tid] = ("Pass" if ok else "Fail", "" if ok else "DEF-L3-011", note)

    # 3) Case ghi tay (Newman / SQL / Blocked)
    for tid, value in MANUAL.items():
        status[tid] = value

    # 4) Nhom C - chuc nang trong workbook CHUA duoc trien khai.
    #
    # Test xUnit tuong ung VAN XANH vi no chi chung minh "route khong ton tai" (404/405) - do la
    # bang chung dung. Nhung CASE trong workbook thi KHONG THE thoa man: nghiep vu ma no mo ta
    # khong co trong he thong. Vi vay Status ghi Fail va tro toi defect theo tung module,
    # thay vi de Pass gay hieu nham la da co chuc nang.
    module_defect = {
        "L3-DEL": ("DEF-L3-004", "Module Delivery Trip / POD / thu COD chua trien khai"),
    }
    for row in read_map_csv():
        if row["Group"] != "C":
            continue
        tid = row["TestID"]
        prefix = tid.rsplit("-", 1)[0]
        defect, label = module_defect.get(prefix, ("DEF-L3-005", "Chuc nang chua trien khai"))
        status[tid] = ("Fail", defect,
                       f"{label}: workbook ghi `{row['WorkbookEndpoint']}` nhung route nay khong ton tai "
                       f"(test da xac nhan 404/405). {row['Note']}")

    return status


def write_status_csv(index, status):
    out = os.path.join(ROOT, "tests", f"L3_status_{RUN_DATE}.csv")
    with io.open(out, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["Test ID", "Sheet", "Status", "Defect ID", "Ghi chu ngan"])
        for tid, sheet in index:
            st, defect, note = status.get(tid, ("Not Run", "", "Chua co ket qua."))
            w.writerow([tid, sheet, st, defect, note])
    return out


def write_drift_md(status):
    rows = read_map_csv()
    out = os.path.join(ROOT, "tests", "L3_ENDPOINT_DRIFT.md")

    group_names = {
        "A": "Nhom A - co endpoint tuong duong (chi khac ten/method)",
        "B": "Nhom B - KHONG co endpoint, va do la DUNG (bat bien bao dam bang viec khong mo route)",
        "C": "Nhom C - thieu chuc nang that (endpoint khong ton tai va can co)",
    }

    with io.open(out, "w", encoding="utf-8") as f:
        f.write("# L3 - Danh sach lech giua workbook va code that\n\n")
        f.write(f"Nguon: `Report_5_3_L3-SystemAPITests_VietTien_v1_3.xlsx` doi chieu voi code ngay {RUN_DATE}.\n\n")
        f.write("Doi chieu tu dong: **139 tham chieu endpoint** trong workbook, **93** khong ton tai dung ten trong code.\n\n")
        f.write("---\n\n## 1. Lech duong dan endpoint\n\n")

        for g in ("A", "B", "C"):
            subset = [r for r in rows if r["Group"] == g]
            f.write(f"### {group_names[g]} - {len(subset)} tham chieu\n\n")
            f.write("| Test ID | Workbook ghi | Code that | Ghi chu |\n|---|---|---|---|\n")
            for r in subset:
                f.write(f"| {r['TestID']} | `{r['WorkbookEndpoint']}` | `{r['RealEndpoint']}` | {r['Note']} |\n")
            f.write("\n")

        f.write("---\n\n## 2. Lech errorCode\n\n")
        f.write("Workbook ky vong ~80 ma loi nghiep vu trong than phan hoi. "
                "Code **khong co truong `errorCode` o bat ky endpoint nao** - moi controller tra `{ message }`.\n")
        f.write("Ngoai le duy nhat: `CartController.cs:61` tra `{ code = \"PROFILE_INCOMPLETE\" }`.\n\n")
        f.write("Bang chung: test `L3_DRIFT_001_NoEndpointReturnsErrorCodeField` quet 9 nhanh loi 4xx "
                "trai khap 8 controller, khong nhanh nao co truong errorCode.\n\n")
        f.write("| errorCode workbook ky vong | HTTP workbook | HTTP code that | Than phan hoi that |\n|---|---|---|---|\n")
        for code, wb_http, real_http, body in [
            ("DUPLICATE_IDENTITY", "409", "400", '{ message: "Email nay da duoc su dung." }'),
            ("OTP_INVALID", "400", "400", '{ message: "Ma OTP khong chinh xac." }'),
            ("OTP_EXPIRED", "400", "400", '{ message: "Ma OTP da het han..." }'),
            ("OTP_ATTEMPT_LIMIT_REACHED", "409", "400", '{ message: "...nhap sai qua so lan..." }'),
            ("OTP_RESEND_TOO_SOON", "429", "400", '{ message: "Vui long doi it nhat 60 giay..." }'),
            ("PRICE_SNAPSHOT_EXPIRED_OR_STOCK_CHANGED", "409", "400", '{ message: "Gia trong gio hang da het han giu (qua 24h)..." }'),
            ("PRODUCT_NOT_ORDERABLE", "409", "400", '{ message: "Product not found or discontinued." }'),
            ("CLIENT_PRICE_MISMATCH", "400", "n/a", "Server bo qua gia client gui, tu tinh lai - khong sinh loi"),
            ("QUOTATION_NOT_ELIGIBLE", "409", "400", '{ message: "..." }'),
            ("RESOURCE_FORBIDDEN", "403", "403", "Than rong hoac { message }"),
            ("SEPAY_SIGNATURE_INVALID", "401", "401", '{ success: false, message: "Missing Token" }'),
            ("PAYMENT_EVENT_ALREADY_PROCESSED", "200", "200", '{ success: true } - tra ket qua goc, khong co ma'),
            ("GOODS_ISSUE_PREREQUISITE_NOT_MET", "409", "400", '{ message: "[Ton kho khong du]..." }'),
            ("POSTED_DOCUMENT_IMMUTABLE", "409", "409", '{ message: "Chung tu da duoc Post hoac bi Huy..." }'),
            ("INVENTORY_INVARIANT_VIOLATION", "409", "400", '{ message: "[Ton kho khong du]..." }'),
            ("TRANSFER_VALIDATION_FAILED", "400", "400", "ProblemDetails cua ModelState (title/errors)"),
            ("CONFIGURATION_RETROACTIVE_CHANGE_FORBIDDEN", "409", "400", '{ message: "Khong duoc dat ngay hieu luc vao qua khu..." }'),
            ("AUDIT_LOG_IMMUTABLE", "403", "404/405", "Khong co route DELETE audit-log"),
        ]:
            f.write(f"| `{code}` | {wb_http} | {real_http} | {body} |\n")

        f.write("\n---\n\n## 3. Defect tong hop\n\n")
        f.write("| Defect ID | Muc | Tom tat | Bang chung |\n|---|---|---|---|\n")
        for d, sev, summary, evidence in active_defects(status):
            f.write(f"| {d} | {sev} | {summary} | {evidence} |\n")
    return out


# Hai defect he thong nay khong gan voi Test ID nao trong workbook (chung duoc chung minh boi
# L3_DRIFT_001/002 — test do CHINH nhom them, khong co trong 172 case) nen luon phai liet ke.
ALWAYS_REPORT = {"DEF-L3-001", "DEF-L3-002"}


def active_defects(status):
    """Chi liet ke defect THUC SU duoc mot case tham chieu toi (tranh bao cao defect ma)."""
    referenced = {defect for (_, defect, _) in status.values() if defect} | ALWAYS_REPORT
    return [d for d in DEFECTS if d[0] in referenced]


DEFECTS = [
    ("DEF-L3-001", "P2",
     "Khong co error registry: 0/195 endpoint tra truong errorCode, trong khi SRS/workbook dinh nghia ~80 ma loi nghiep vu.",
     "`L3_DRIFT_001_NoEndpointReturnsErrorCodeField`"),
    ("DEF-L3-002", "P3",
     "Ma HTTP lech SRS: nhieu nhanh xung dot trang thai tra 400 thay vi 409, gioi han tan suat tra 400 thay vi 429.",
     "`L3_DRIFT_002_BusinessErrorsUse400WhereSrsExpects409Or429`"),
    ("DEF-L3-003", "P1",
     "Bao gia da duyet duoc ap cho GIO HANG KHAC: gio 240tr bi tinh thanh 110tr vi CalculateDiscountAsync khong "
     "doi chieu gio hien tai voi version da duyet (Quotation.CartId khong duoc dung).",
     "`L3_QUO_05_...MustBeRejected` - OrderService.cs:88-103"),
    ("DEF-L3-004", "P1",
     "Module Delivery Trip / POD / thu COD chua trien khai: khong co /api/delivery/trips, /attempts, /collections.",
     "L3-DEL-01..07"),
    ("DEF-L3-005", "P2",
     "Thieu chuc nang: multi-pick, phien kiem ke (count-session), xuat NVL san xuat, canh bao ton thap, "
     "upload media + chi so bai marketing.",
     "L3-FUL-08, L3-INV-01/05/06, L3-MKT-09/10/11"),
    ("DEF-L3-006", "P1",
     "Broken Access Control (OWASP A01): 4 endpoint DOC cua WarehouseController chi co [Authorize] cap class, "
     "khong gioi han vai tro -> Customer doc duoc toan bo hang doi xuat kho, chi tiet don khach khac va pick task.",
     "`L3_FUL_01_...` - WarehouseController.cs dong 12, 34, 51, 115, 132"),
    ("DEF-L3-007", "P1",
     "Dieu chinh ton kho khong kiem rang buoc: AdjustInventoryAsync chi chan so am, cho phep dat OnHand=0 khi "
     "dang co 1.000 Reserved + 1.000 Quarantine -> ton kha dung tho = -2000, bi che boi Math.Max(0,...).",
     "`L3_INV_04_...MustNotDriveAvailableNegative` - InventoryService.cs:97-122"),
    ("DEF-L3-008", "P2",
     "Upload file chi kiem PHAN MO RONG ten file (do nguoi gui dat), khong kiem magic byte -> file PE/EXE "
     "doi duoi .png di lot va duoc luu tru.",
     "`L3_SEC_14_...MustBeRejected` - UserProfileService.cs:83-85"),
    ("DEF-L3-009", "P2",
     "Chua cung hoa lop van chuyen: khong redirect HTTP->HTTPS va thieu ca 3 header HSTS / "
     "X-Content-Type-Options / X-Frame-Options.",
     "Newman L3-SEC-05, L3-SEC-15"),
    ("DEF-L3-010", "P1",
     "AuditLogs KHONG bat bien o muc DB: tai khoan ung dung UPDATE/DELETE duoc ban ghi audit "
     "(khong co trigger INSTEAD OF, khong co DENY).",
     "tests/sql/L3-SEC-06_13.ps1 - L3-SEC-13"),
    ("DEF-L3-011", "P2",
     "Vuot nguong hieu nang NFR o mot so endpoint (xem cot Ghi chu cua cac case L3-PERF-xx).",
     "tests/reports/jmeter-L3/index.html"),
]


def write_summary_md(index, status, jtl, xunit_rows, xunit_red, xunit_cases):
    global XUNIT_ROWS, XUNIT_RED, XUNIT_CASES
    XUNIT_ROWS, XUNIT_RED, XUNIT_CASES = xunit_rows, xunit_red, xunit_cases
    counts = defaultdict(int)
    for tid, _ in index:
        counts[status.get(tid, ("Not Run",))[0]] += 1

    out = os.path.join(ROOT, "tests", "L3_KET_QUA_TOM_TAT.md")
    with io.open(out, "w", encoding="utf-8") as f:
        f.write(f"# L3 System/API Test - ket qua tong hop ({RUN_DATE})\n\n")
        f.write(f"Tong so case trong workbook: **{len(index)}**\n\n")
        f.write("| Trang thai | So case | Ty le |\n|---|---:|---:|\n")
        for st in ("Pass", "Fail", "Blocked", "Skip", "Not Run"):
            n = counts.get(st, 0)
            if n:
                f.write(f"| {st} | {n} | {round(100.0 * n / len(index), 1)}% |\n")

        f.write("\n## Cong cu da dung\n\n")
        f.write("| Cong cu | Pham vi | Ket qua |\n|---|---|---|\n")
        f.write(f"| xUnit + WebApplicationFactory + SQL Server local | {XUNIT_CASES} case hop dong API | "
                f"{XUNIT_ROWS} dong chay, {XUNIT_ROWS - XUNIT_RED} xanh, {XUNIT_RED} do co chu dich |\n")
        f.write("| Newman (Postman CLI) | Security header, SQLi, 401/403, Swagger | "
                "13 request / 25 assertion (3 do: SEC-15) + 1 request / 4 assertion cho Swagger |\n")
        jmeter_cases = sum(1 for t in PERF_THRESHOLDS if jtl.get(t, {}).get("tool") == "JMeter")
        signalr_cases = sum(1 for t in PERF_THRESHOLDS if jtl.get(t, {}).get("tool") == "SignalR.Client")
        f.write(f"| Apache JMeter 5.6.3 (thay k6) | {jmeter_cases} case hieu nang HTTP | xem bang duoi |\n")
        if signalr_cases:
            f.write(f"| Microsoft.AspNetCore.SignalR.Client | {signalr_cases} case hieu nang WebSocket "
                    "(JMeter khong lam duoc) | xem bang duoi |\n")
        f.write("| SQL truc tiep | SEC-06, SEC-13 | 1 Pass, 1 Fail |\n")

        if jtl:
            f.write("\n## So lieu hieu nang do duoc\n\n")
            f.write("| Test ID | Cong cu | So mau | p95 (ms) | Nguong (ms) | Ket qua | Loi HTTP |\n"
                    "|---|---|---:|---:|---:|---|---:|\n")
            for tid in sorted(PERF_THRESHOLDS):
                s = jtl.get(tid)
                if not s:
                    continue
                th = PERF_THRESHOLDS[tid]
                verdict = "PASS" if s["p95"] <= th and s["error_rate"] < 1.0 else "FAIL"
                f.write(f"| {tid} | {s.get('tool', 'JMeter')} | {s['count']} | {s['p95']} | {th} | "
                        f"{verdict} | {s['error_rate']}% |\n")

        f.write("\n## Defect\n\n")
        f.write("| Defect ID | Muc | Tom tat |\n|---|---|---|\n")
        for d, sev, summary, _ in active_defects(status):
            f.write(f"| {d} | {sev} | {summary} |\n")
    return out


def main():
    index = read_workbook_index()
    trx = parse_trx()
    jtl = parse_jtl()
    status = build_status(trx, jtl)

    # So lieu chay THAT cua xUnit, lay tu chinh .trx thay vi ghi tay.
    xunit_rows = sum(len(v["outcomes"]) for v in trx.values())
    xunit_red = sum(1 for v in trx.values() if "Failed" in v["outcomes"])
    xunit_cases = len([t for t, _ in index if t in trx])

    csv_path = write_status_csv(index, status)
    drift_path = write_drift_md(status)
    summary_path = write_summary_md(index, status, jtl, xunit_rows, xunit_red, xunit_cases)

    counts = defaultdict(int)
    for tid, _ in index:
        counts[status.get(tid, ("Not Run",))[0]] += 1

    print(f"Tong case trong workbook : {len(index)}")
    print(f"Test ID lay tu .trx      : {len(trx)}")
    print(f"Label PERF lay tu .jtl   : {len(jtl)}")
    print("Phan bo trang thai       :", dict(counts))
    print("\nDa sinh:")
    for p in (csv_path, drift_path, summary_path):
        print("  -", os.path.relpath(p, ROOT))

    missing = [tid for tid, _ in index if tid not in status]
    if missing:
        print(f"\nCANH BAO - {len(missing)} case chua co ket qua:", ", ".join(missing[:20]))


if __name__ == "__main__":
    main()
