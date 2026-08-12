# L3-SEC-06 va L3-SEC-13 - hai case cua sheet L3-Security phai kiem O MUC DATABASE,
# khong kiem duoc qua HTTP.
#
#   SEC-06 (A02, NFR-SEC02): mat khau phai luu dang salted hash, khong plaintext.
#   SEC-13 (A08, NFR-SEC08/BR-048): thu UPDATE/DELETE tren bang AuditLogs bang chinh tai khoan
#          ung dung dung de ket noi. Ky vong cua SRS: bi tu choi o muc DB (bang INSERT-only).
#
# Chay tren DB local VietTien22 - KHONG bao gio chay tren DB Azure.
#
# LUU Y: file nay co y viet THUAN ASCII. Windows PowerShell 5.1 doc file .ps1 theo ANSI codepage
# neu khong co BOM, nen ky tu tieng Viet/em-dash se bi vo va lam hong ca script.
#
#   powershell -ExecutionPolicy Bypass -File tests\sql\L3-SEC-06_13.ps1

$ErrorActionPreference = 'Stop'
$connectionString = 'Server=localhost;Database=VietTien22;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=15'

function Invoke-Scalar([string]$sql) {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
        return $cmd.ExecuteScalar()
    } finally { $conn.Close() }
}

function Invoke-NonQuery([string]$sql) {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
        return @{ Rows = $cmd.ExecuteNonQuery(); Error = $null }
    } catch {
        return @{ Rows = -1; Error = $_.Exception.Message }
    } finally { $conn.Close() }
}

Write-Host "==============================================================="
Write-Host " L3-SEC-06 - Mat khau phai luu dang salted hash (NFR-SEC02)"
Write-Host "==============================================================="

$total  = Invoke-Scalar "SELECT COUNT(*) FROM Users"
$bcrypt = Invoke-Scalar "SELECT COUNT(*) FROM Users WHERE PasswordHash LIKE '`$2%'"

$plaintextSql = @'
SELECT COUNT(*) FROM Users
WHERE PasswordHash IN ('123456','password','Passw0rd!') OR LEN(PasswordHash) < 20
'@
$plaintext = Invoke-Scalar $plaintextSql

$distinctHashes = Invoke-Scalar "SELECT COUNT(DISTINCT PasswordHash) FROM Users"

Write-Host "  Tong so tai khoan       : $total"
Write-Host "  Hash BCrypt (tien to 2) : $bcrypt"
Write-Host "  Nghi van plaintext/yeu  : $plaintext"
Write-Host "  So hash phan biet       : $distinctHashes"

if ($plaintext -eq 0 -and $bcrypt -eq $total) {
    Write-Host "  KET QUA SEC-06: PASS - 100% mat khau la BCrypt hash, khong co plaintext." -ForegroundColor Green
} else {
    Write-Host "  KET QUA SEC-06: FAIL - con $plaintext ban ghi kha nghi." -ForegroundColor Red
}

Write-Host ""
Write-Host "==============================================================="
Write-Host " L3-SEC-13 - AuditLogs phai bat bien o muc DB (BR-048/NFR-SEC08)"
Write-Host "==============================================================="

# Tao 1 ban ghi audit rieng de thu nghiem (khong dung ban ghi that).
$probeId = [guid]::NewGuid()
$insertSql = @"
INSERT INTO AuditLogs (Id, EntityName, EntityId, Action, ActorEmail, ActorRole, Reason, CreatedAt)
VALUES ('$probeId', 'L3ProbeEntity', 'probe', 'CREATE', 'l3@test.local', 'Admin', 'ban ghi thu nghiem SEC-13', GETUTCDATE())
"@
Invoke-NonQuery $insertSql | Out-Null

$before = Invoke-Scalar "SELECT COUNT(*) FROM AuditLogs"
Write-Host "  So ban ghi audit truoc thu : $before"

$update = Invoke-NonQuery "UPDATE AuditLogs SET Reason = 'DA BI SUA' WHERE Id = '$probeId'"
$delete = Invoke-NonQuery "DELETE FROM AuditLogs WHERE Id = '$probeId'"

$updateNote = ''; if ($update.Error) { $updateNote = '| Loi: ' + $update.Error }
$deleteNote = ''; if ($delete.Error) { $deleteNote = '| Loi: ' + $delete.Error }
Write-Host "  UPDATE -> so dong bi doi   : $($update.Rows)  $updateNote"
Write-Host "  DELETE -> so dong bi xoa   : $($delete.Rows)  $deleteNote"

if ($update.Rows -le 0 -and $delete.Rows -le 0) {
    Write-Host "  KET QUA SEC-13: PASS - DB tu choi sua/xoa audit log." -ForegroundColor Green
} else {
    Write-Host "  KET QUA SEC-13: FAIL - tai khoan ung dung SUA/XOA duoc audit log." -ForegroundColor Red
    Write-Host "    -> Bang AuditLogs khong phai INSERT-only: khong co trigger INSTEAD OF UPDATE/DELETE," -ForegroundColor Red
    Write-Host "       cung khong co DENY UPDATE/DELETE tren tai khoan ma ung dung dung de ket noi." -ForegroundColor Red
}

# Don ban ghi thu nghiem neu no van con (truong hop DELETE bi tu choi).
Invoke-NonQuery "DELETE FROM AuditLogs WHERE EntityName = 'L3ProbeEntity'" | Out-Null
