# Khoi dong VietTien.API cho dot kiem thu L4 E2E (Playwright).
#
# VI SAO GIU ASPNETCORE_ENVIRONMENT=Development:
#   Program.cs:307-310 bat UseHttpsRedirection() khi environment != Development. Frontend Vite
#   proxy /api va /hubs qua HTTP toi 127.0.0.1:5050 (vite.config.ts), neu bi 307 redirect sang
#   HTTPS thi toan bo test E2E chet ngay tu request dau. Khac voi L3 (goi HTTP truc tiep nen
#   dung duoc environment "L3Perf").
#
# VI SAO PHAI GHI DE CONNECTION STRING:
#   appsettings.Development.json (gitignored) tro vao Azure SQL that
#   (viettien.database.windows.net / VietTien2). Ngay 12/08/2026 firewall Azure chan IP cua may
#   nay nen khong ket noi duoc; ngoai ra Program.cs:284-291 goi Migrate() VO DIEU KIEN moi lan
#   khoi dong -> neu ket noi duoc thi schema production se bi ALTER truoc khi chay test nao.
#   Bien moi truong thang appsettings trong thu tu cau hinh cua .NET nen dong duoi day la lop
#   chan chac chan, khong can sua file nao.
#
# CAC KEY GOI RA NGOAI (eSMS / Gemini / Cloudinary / make.com) DE NGUYEN THEO YEU CAU:
#   Nguoi dung da chon "de key that hoat dong". Nghia la SMS that se duoc gui va bai that co the
#   duoc dang len Facebook. Script nay KHONG ep rong chung. Neu muon chay an toan, bo comment
#   khoi lenh o cuoi file.
#
# Chay:
#   powershell -ExecutionPolicy Bypass -File tests\run-l4-api.ps1

param(
    [string]$Db   = 'VietTien22_L3',
    [int]   $Port = 5050
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS        = "http://localhost:$Port"

$env:ConnectionStrings__DefaultConnection =
    "Server=localhost;Database=$Db;Trusted_Connection=True;TrustServerCertificate=True;"

Write-Host "ENV = Development  (bat buoc, de KHONG bi UseHttpsRedirection)"
Write-Host "URL = $($env:ASPNETCORE_URLS)"
Write-Host "DB  = localhost / $Db   (KHONG phai Azure)"
Write-Host ""

# --- Chay an toan: bo comment 7 dong duoi de chan moi ket noi ra ngoai ---
# $env:eSMS__ApiKey                  = ''
# $env:eSMS__SecretKey               = ''
# $env:GeminiSettings__ApiKey        = ''
# $env:CloudinarySettings__ApiKey    = ''
# $env:CloudinarySettings__ApiSecret = ''
# $env:MakeCom__WebhookUrl           = ''
# $env:EmailSettings__SenderPassword = ''

dotnet run --project (Join-Path $repoRoot 'VietTien.API\VietTien.API.csproj') --no-launch-profile
