<#
.SYNOPSIS
    Saha testi paketini uretir: baskasinin makinesinde hicbir sey kurmadan
    calisacak, kendi kendine yeten bir klasor ve zip.

.DESCRIPTION
    Paket duzeni kasitli. Uygulama calisma dosyalarini once KENDI YANINDA arar
    (VendorPaths ve ProfileStore boyle yaziliyor), depo agacinda degil. Yani:

        zapret-tr-test.exe
        profiles/          <- ISS profilleri, merdiven, hedefler
        zapret-winws/      <- winws.exe ve bagimliliklari

    zapret-winws adi onemli: VendorPaths tam olarak bu klasor adina bakiyor.

    Pakete .pdb dosyalari girmez; hata ayiklama sembollerini ucuncu bir kisiye
    gondermenin bir anlami yok.

.PARAMETER OutputDirectory
    Paketin uretilecegi dizin. Varsayilan: depo kokunde 'dist'.

.PARAMETER SkipPublish
    dotnet publish adimini atlar (zaten yayinlanmis bir cikti varsa).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackageName = 'zapret-tr-saha-testi'
$DistDir = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $RepoRoot 'dist' }
$PackageDir = Join-Path $DistDir $PackageName
$PublishDir = Join-Path $RepoRoot 'publish/cli'
$VendorDir = Join-Path $RepoRoot 'vendor/zapret-winws'
$DnsCryptDir = Join-Path $RepoRoot 'vendor/dnscrypt-proxy'
$ProfilesDir = Join-Path $RepoRoot 'profiles'

Write-Host ''
Write-Host 'ZapretTR - saha testi paketi' -ForegroundColor Cyan
Write-Host ''

# --- Onkosullar ---------------------------------------------------------------
if (-not (Test-Path (Join-Path $VendorDir 'winws.exe'))) {
    throw "vendor/zapret-winws/winws.exe yok. Once tools/fetch-upstream.ps1 calistirin."
}

if (-not (Test-Path (Join-Path $VendorDir 'cygwin1.dll'))) {
    throw "vendor/zapret-winws/cygwin1.dll yok. winws cygwin ile derlendigi icin bu dosya sart. tools/fetch-upstream.ps1 calistirin."
}

if (-not (Test-Path (Join-Path $DnsCryptDir 'dnscrypt-proxy.exe'))) {
    throw "vendor/dnscrypt-proxy/dnscrypt-proxy.exe yok. tools/fetch-upstream.ps1 calistirin."
}

# --- Yayinla ------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Host '  yayinlaniyor (tek dosya, kendi kendine yeten)...'
    $projectPath = Join-Path $RepoRoot 'src/ZapretTr.Prober.Cli'
    & dotnet publish $projectPath -c Release -o $PublishDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish basarisiz (kod $LASTEXITCODE)." }
}

$exePath = Join-Path $PublishDir 'zapret-tr-test.exe'
if (-not (Test-Path $exePath)) { throw "Yayin ciktisi bulunamadi: $exePath" }

# msquic.dll exe'nin YANINDA gitmek ZORUNDA. Tek dosya paketine gomuldugunde
# calisma aninda bulunamiyor ve QuicConnection.IsSupported false donuyor; belirti
# "QUIC bu makinede desteklenmiyor (msquic yok)" ve QUIC bolumu sessizce
# olculemiyor. Bu paket bir kez bu sekilde dagitildi ve QUIC verisi hic gelmedi.
$msQuicPath = Join-Path $PublishDir 'msquic.dll'
if (-not (Test-Path $msQuicPath)) {
    throw "msquic.dll yayin ciktisinda yok: $msQuicPath`nBu dosya olmadan paket QUIC bolumunu olcemez. Cli projesindeki MsQuicTekDosyaDisindaKalsin hedefi calismamis olabilir."
}

# --- Paketi kur ---------------------------------------------------------------
if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

Write-Host '  dosyalar kopyalaniyor...'
Copy-Item $exePath (Join-Path $PackageDir 'zapret-tr-test.exe')
Copy-Item $msQuicPath (Join-Path $PackageDir 'msquic.dll')

# profiles/ ve zapret-winws/ uygulamanin YANINDA olmali.
Copy-Item $ProfilesDir (Join-Path $PackageDir 'profiles') -Recurse
Copy-Item $VendorDir (Join-Path $PackageDir 'zapret-winws') -Recurse

# dnscrypt-proxy zapret-winws'in KARDESI dizinde olmali; VendorPaths onu boyle
# ariyor. Ic ice koymak sessizce bulunamamasina yol acardi.
Copy-Item $DnsCryptDir (Join-Path $PackageDir 'dnscrypt-proxy') -Recurse

# --- Calistirma kisayollari ---------------------------------------------------
# Exe'nin manifesti requireAdministrator; cmd bunu dogrudan calistiramadigi icin
# .bat kendini once yukseltiyor. Aksi halde kullanici "erisim engellendi" gorur.
$startBat = @'
@echo off
chcp 65001 >nul
title ZapretTR - saha testi

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Yonetici yetkisi gerekiyor, izin penceresi acilacak...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
zapret-tr-test.exe --isp auto --doh --max-candidates 25 --out "zapret-tr-rapor.json"

echo.
echo ============================================================
echo  Test bitti. Sonuc dosyasi: zapret-tr-rapor.json
echo  Bu klasorde olusan bu dosyayi geri gonderin.
echo.
echo  Suruculeri kaldirmak icin TEMIZLIK.bat dosyasini calistirin.
echo ============================================================
echo.
pause
'@
Set-Content (Join-Path $PackageDir 'TESTI-BASLAT.bat') $startBat -Encoding ascii

$cleanBat = @'
@echo off
chcp 65001 >nul
title ZapretTR - temizlik

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Yonetici yetkisi gerekiyor, izin penceresi acilacak...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
zapret-tr-test.exe --cleanup

echo.
pause
'@
Set-Content (Join-Path $PackageDir 'TEMIZLIK.bat') $cleanBat -Encoding ascii

# --- Okuma dosyasi ------------------------------------------------------------
$readmeSource = Join-Path $PSScriptRoot 'field-package-readme.txt'
if (-not (Test-Path $readmeSource)) { throw "Aciklama dosyasi yok: $readmeSource" }
Copy-Item $readmeSource (Join-Path $PackageDir 'OKU-BENI.txt')

# --- Zip ----------------------------------------------------------------------
$zipPath = Join-Path $DistDir "$PackageName.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $PackageDir -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = (Get-Item $zipPath).Length / 1MB
$fileCount = @(Get-ChildItem $PackageDir -Recurse -File).Count

Write-Host ''
Write-Host "Paket hazir: $zipPath" -ForegroundColor Green
Write-Host ("  {0} dosya, {1:N1} MB" -f $fileCount, $zipSize)
Write-Host ''
