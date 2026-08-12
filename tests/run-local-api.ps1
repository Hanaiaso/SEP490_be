# Khoi dong VietTien.API tren may local cho L3 (Newman + JMeter).
#
# AN TOAN — VI SAO KHONG DUNG ASPNETCORE_ENVIRONMENT=Development:
#   appsettings.Development.json (gitignored) tro connection string vao DB THAT tren Azure
#   (viettien.database.windows.net) va chua API key THAT cua eSMS/Gemini/Cloudinary + webhook make.com.
#   Load test 100 VUs vao do se ghi/xoa du lieu that va ton DTU. Vi vay mac dinh ta chay o environment
#   "L3Perf": .NET chi nap appsettings.json (moi secret deu rong) roi ta ghi de bang bien moi truong.
#
# Phu pham cua environment != Development (da kiem trong Program.cs):
#   - Program.cs:293 Swagger TAT  -> case L3-ADMC-17 phai chay o mode Development, dung -Env Development.
#   - Program.cs:303 UseHttpsRedirection BAT -> dung cho case L3-SEC-05 (ep HTTP -> HTTPS).
#
# Chay:
#   powershell -ExecutionPolicy Bypass -File tests\run-local-api.ps1                 # mode A (mac dinh)
#   powershell -ExecutionPolicy Bypass -File tests\run-local-api.ps1 -Env Development # mode B (co Swagger)

param(
    [string]$Env  = 'L3Perf',
    [int]   $Port = 5080
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$env:ASPNETCORE_ENVIRONMENT = $Env
$env:ASPNETCORE_URLS        = "http://localhost:$Port"

# DB local rieng cho test. Program.cs:285 tu chay Migrate() khi khoi dong -> DB duoc tao tu dong.
$env:ConnectionStrings__DefaultConnection =
    'Server=localhost;Database=VietTien22;Trusted_Connection=True;TrustServerCertificate=True;'

# Khoa ky JWT gia, >= 32 byte cho HMAC-SHA256. Khong phai secret that.
$env:JwtSettings__SecretKey = 'L3SystemApiTestsOnly_FakeSigningKey_DoNotUseInProduction_0123456789'

# BAT BUOC khac rong: SePayController doi chieu token webhook di VAO. Neu de rong thi moi request
# webhook deu 401 va khong case PAY-xx nao chay duoc (xem chu thich trong appsettings.Test.json).
$env:SePaySettings__ApiToken = 'test-sepay-token-not-a-real-secret'

# Lop phong ve thu hai: ep rong moi credential goi RA NGOAI, phong truong hop ai do commit gia tri
# that vao appsettings.json.
$env:eSMS__ApiKey                = ''
$env:eSMS__SecretKey             = ''
$env:GeminiSettings__ApiKey      = ''
$env:CloudinarySettings__ApiKey  = ''
$env:CloudinarySettings__ApiSecret = ''
$env:MakeCom__WebhookUrl         = ''
$env:EmailSettings__SenderPassword = ''

Write-Host "ENV        = $Env"
Write-Host "URL        = $($env:ASPNETCORE_URLS)"
Write-Host "DB         = localhost / VietTien22   (KHONG phai Azure)"
Write-Host ""

dotnet run --project (Join-Path $repoRoot 'VietTien.API\VietTien.API.csproj') --no-launch-profile
