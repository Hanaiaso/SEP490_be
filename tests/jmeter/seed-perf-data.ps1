# Seed du lieu toi thieu cho cac case JMeter can trang thai nghiep vu THAT.
#
# Ly do: L3-PERF-03 do GET /api/orders/checkout-summary. Neu tai khoan customer.test khong co
# dia chi + gio hang thi MOI request tra 400 "Gio hang trong" -> p95 do duoc la cua ĐƯỜNG LOI,
# khong co y nghia gi ve hieu nang. Phai seed truoc khi chay.
#
# Chay tren DB local VietTien22 - KHONG bao gio chay tren DB Azure.
#
#   powershell -ExecutionPolicy Bypass -File tests\jmeter\seed-perf-data.ps1

$ErrorActionPreference = 'Stop'
$connectionString = 'Server=localhost;Database=VietTien22;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=15'

function Invoke-Sql([string]$sql) {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
        return $cmd.ExecuteNonQuery()
    } finally { $conn.Close() }
}

function Get-Scalar([string]$sql) {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
        return $cmd.ExecuteScalar()
    } finally { $conn.Close() }
}

# Tai khoan + san pham seed san trong ApplicationDbContext.OnModelCreating
$customerUserId = '77777777-7777-7777-7777-777777777777'
$profileId      = '88888888-8888-8888-8888-888888888888'
$productId      = 'e24b1960-21d2-4385-8155-17557c0ce8b9'  # Mang chit PE, 120.000d

Write-Host "Seed du lieu cho L3-PERF-03 tren DB VietTien22 ..."

# 1. Dia chi giao hang (CartService chan them gio khi ho so chua co dia chi nao)
$hasAddress = Get-Scalar "SELECT COUNT(*) FROM Addresses WHERE CustomerProfileId = '$profileId'"
if ($hasAddress -eq 0) {
    Invoke-Sql @"
INSERT INTO Addresses (Id, CustomerProfileId, ReceiverName, ReceiverPhone, City, District, Ward,
                       SpecificAddress, Type, IsDefault, CreatedAt, UpdatedAt)
VALUES (NEWID(), '$profileId', N'Nguoi Nhan Perf', '0912345678', N'TP HCM', N'Quan 1', N'Phuong 1',
        N'So 1 Duong Test', 0, 1, GETUTCDATE(), GETUTCDATE())
"@ | Out-Null
    Write-Host "  + Da tao dia chi mac dinh."
} else {
    Write-Host "  = Da co dia chi ($hasAddress dong)."
}

# 2. Gio hang + 1 dong hang
$cartId = Get-Scalar "SELECT TOP 1 Id FROM Carts WHERE CustomerProfileId = '$profileId'"
if (-not $cartId) {
    $cartId = [guid]::NewGuid()
    Invoke-Sql "INSERT INTO Carts (Id, CustomerProfileId, CreatedAt, UpdatedAt) VALUES ('$cartId', '$profileId', GETUTCDATE(), GETUTCDATE())" | Out-Null
    Write-Host "  + Da tao gio hang."
} else {
    Write-Host "  = Da co gio hang."
}

# UpdatedAt phai la HIEN TAI: OrderService chan don khi gio qua 24h.
Invoke-Sql "UPDATE Carts SET UpdatedAt = GETUTCDATE() WHERE Id = '$cartId'" | Out-Null

$hasItem = Get-Scalar "SELECT COUNT(*) FROM CartItems WHERE CartId = '$cartId'"
if ($hasItem -eq 0) {
    Invoke-Sql "INSERT INTO CartItems (Id, CartId, ProductId, Quantity, UnitPrice) VALUES (NEWID(), '$cartId', '$productId', 2, 120000)" | Out-Null
    Write-Host "  + Da them 1 dong vao gio."
} else {
    Write-Host "  = Gio da co $hasItem dong."
}

Write-Host "Xong. L3-PERF-03 gio se do duoc duong THANH CONG (200) thay vi duong loi."
