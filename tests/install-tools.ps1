# Cài công cụ cho L3 System/API Test.
#
# Workbook Report_5_3 ghi "k6 (Performance)" và "OWASP ZAP (Security)". Theo quyết định của nhóm
# (12/08/2026) ta thay k6 bằng Apache JMeter — Java 21 đã có sẵn trên máy nên JMeter chạy được ngay,
# NGƯỠNG NFR giữ nguyên không đổi. ZAP không cài; các case SEC làm bằng xUnit/Postman.
#
# Chạy:  powershell -ExecutionPolicy Bypass -File tests\install-tools.ps1

$ErrorActionPreference = 'Stop'
$repoRoot  = Split-Path -Parent $PSScriptRoot
$toolsDir  = Join-Path $repoRoot 'tools'
$jmeterVer = '5.6.3'
$jmeterDir = Join-Path $toolsDir "apache-jmeter-$jmeterVer"

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null

# ---- Apache JMeter -----------------------------------------------------------------------------
if (Test-Path (Join-Path $jmeterDir 'bin\jmeter.bat')) {
    Write-Host "[skip] JMeter $jmeterVer da co tai $jmeterDir"
} else {
    $zip = Join-Path $toolsDir "apache-jmeter-$jmeterVer.zip"
    $url = "https://archive.apache.org/dist/jmeter/binaries/apache-jmeter-$jmeterVer.zip"
    Write-Host "[1/3] Tai JMeter $jmeterVer tu $url ..."
    # Invoke-WebRequest voi progress bar bat se cham hon nhieu lan tren file ~88MB.
    $prev = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    $ProgressPreference = $prev

    Write-Host "[2/3] Giai nen ..."
    Expand-Archive -Path $zip -DestinationPath $toolsDir -Force
    Remove-Item $zip -Force
    Write-Host "[ok] JMeter: $jmeterDir"
}

& (Join-Path $jmeterDir 'bin\jmeter.bat') --version 2>&1 | Select-Object -First 6

# ---- Newman (Postman CLI) ----------------------------------------------------------------------
Write-Host "[3/3] Cai newman + newman-reporter-htmlextra ..."
$newman = Get-Command newman -ErrorAction SilentlyContinue
if ($newman) {
    Write-Host "[skip] newman da co: $((newman --version) 2>&1)"
} else {
    npm install -g newman newman-reporter-htmlextra
}

Write-Host ""
Write-Host "XONG. JMeter=$jmeterDir ; newman=$((Get-Command newman -ErrorAction SilentlyContinue).Source)"
