<#
.SYNOPSIS
    zapret/winws ikililerini sabitlenmis bir upstream surumunden indirir ve dogrular.

.DESCRIPTION
    ZapretTR, zapret'in C kodunu fork'lamaz. Yalnizca calisma zamaninda gereken
    ikilileri (winws.exe, WinDivert surucusu, WinDivert filtre parcalari, sahte
    QUIC yuku) upstream'den ceker.

    Surum sabitleme commit SHA'si ile yapilir: zapret-win-bundle deposunda tag yok,
    ama commit SHA'si icerik-adreslidir, yani tag'den daha guclu bir garanti verir.

    Her dosyanin SHA256'si tools/upstream-manifest.json icinde tutulur ve indirme
    sonrasi dogrulanir. Upstream surumunu yukseltmek icin: asagidaki $BundleCommit /
    $ZapretTag degerlerini degistir, sonra -UpdateManifest ile calistir, sonra
    olusan manifest farkini incele ve commit'le.

.PARAMETER UpdateManifest
    Dogrulama yapmak yerine manifest'i indirilen dosyalara gore yeniden uretir.
    Upstream surumu yukseltilirken kullanilir. Manifest degisikligi kod incelemesinde
    gorunur olsun diye ayri bir bayrak olarak birakildi.

.PARAMETER Force
    vendor/ dolu olsa bile yeniden indirir.
#>
[CmdletBinding()]
param(
    [switch]$UpdateManifest,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Sabitlenmis upstream surumleri ------------------------------------------
# zapret-win-bundle @ 2026-08-23. Bu depoda tag yok, commit SHA ile sabitliyoruz.
$BundleCommit = '32fbbebf29be855566faef0961ac85627a3a4aeb'
# Ana zapret deposu, lisans metni ve sahte TLS yukleri icin.
$ZapretTag    = 'v72.13'

$BundleBase = "https://raw.githubusercontent.com/bol-van/zapret-win-bundle/$BundleCommit/zapret-winws"
$ZapretBase = "https://raw.githubusercontent.com/bol-van/zapret/$ZapretTag"

# --- Indirilecek dosyalar -----------------------------------------------------
# Kasitli olarak dar tutuldu. Ihtiyac duymadigimiz seyleri (cygwin1.dll ~3MB,
# winws2.exe, lua/, ornek preset .cmd dosyalari) indirmiyoruz: kullanmadigimiz
# bir seyi dagitmak hem gereksiz yer kaplar hem de guvenlik yuzeyini genisletir.
$Files = @(
    # Motor ve surucu
    @{ Url = "$BundleBase/winws.exe";        Dest = 'winws.exe' }
    @{ Url = "$BundleBase/WinDivert.dll";    Dest = 'WinDivert.dll' }
    @{ Url = "$BundleBase/WinDivert64.sys";  Dest = 'WinDivert64.sys' }

    # WinDivert ham filtre parcalari (--wf-raw-part ile kullanilir).
    # discord_media ve stun, Discord sesli gorusme icin gerekli.
    @{ Url = "$BundleBase/windivert.filter/windivert_part.discord_media.txt"     ; Dest = 'windivert.filter/windivert_part.discord_media.txt' }
    @{ Url = "$BundleBase/windivert.filter/windivert_part.stun.txt"              ; Dest = 'windivert.filter/windivert_part.stun.txt' }
    @{ Url = "$BundleBase/windivert.filter/windivert_part.quic_initial_ietf.txt" ; Dest = 'windivert.filter/windivert_part.quic_initial_ietf.txt' }
    @{ Url = "$BundleBase/windivert.filter/README.txt"                           ; Dest = 'windivert.filter/README.txt' }

    # Sahte paket yukleri. TLS icin ayri dosyaya gerek yok: --dpi-desync-fake-tls-mod
    # ile SNI runtime'da uretilebiliyor. QUIC icin hazir yuk sart.
    @{ Url = "$BundleBase/files/quic_initial_www_google_com.bin" ; Dest = 'files/quic_initial_www_google_com.bin' }
    @{ Url = "$ZapretBase/files/fake/tls_clienthello_iana_org.bin" ; Dest = 'files/tls_clienthello_iana_org.bin' }

    # MIT lisans metni - dagitimda yaninda gitmek zorunda.
    @{ Url = "$ZapretBase/docs/LICENSE.txt" ; Dest = 'LICENSE.upstream.txt' }
)

# --- Yollar -------------------------------------------------------------------
$RepoRoot     = Split-Path -Parent $PSScriptRoot
$VendorDir    = Join-Path $RepoRoot 'vendor/zapret-winws'
$ManifestPath = Join-Path $PSScriptRoot 'upstream-manifest.json'

function Write-Step { param([string]$Message) Write-Host "  $Message" }

Write-Host ''
Write-Host 'ZapretTR - upstream ikili indirme' -ForegroundColor Cyan
Write-Host "  zapret-win-bundle @ $($BundleCommit.Substring(0,12))"
Write-Host "  zapret            @ $ZapretTag"
Write-Host ''

if ((Test-Path $VendorDir) -and -not $Force -and -not $UpdateManifest) {
    $existing = @(Get-ChildItem -Path $VendorDir -Recurse -File)
    if ($existing.Count -ge $Files.Count) {
        Write-Host 'vendor/ zaten dolu. Yeniden indirmek icin -Force kullan.' -ForegroundColor Yellow
        Write-Host ''
        return
    }
}

New-Item -ItemType Directory -Force -Path $VendorDir | Out-Null

# --- Indirme ------------------------------------------------------------------
$downloaded = @{}
foreach ($file in $Files) {
    $destPath = Join-Path $VendorDir $file.Dest
    $destDir  = Split-Path -Parent $destPath
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }

    Write-Step "indiriliyor: $($file.Dest)"
    try {
        Invoke-WebRequest -Uri $file.Url -OutFile $destPath -UseBasicParsing -TimeoutSec 60
    } catch {
        throw "Indirilemedi: $($file.Url)`n$($_.Exception.Message)"
    }

    $downloaded[$file.Dest] = (Get-FileHash -Path $destPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

Write-Host ''

# --- Manifest uretimi ya da dogrulama -----------------------------------------
if ($UpdateManifest) {
    $manifest = [ordered]@{
        bundleCommit = $BundleCommit
        zapretTag    = $ZapretTag
        generated    = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        files        = [ordered]@{}
    }
    foreach ($key in ($downloaded.Keys | Sort-Object)) { $manifest.files[$key] = $downloaded[$key] }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $ManifestPath -Encoding utf8
    Write-Host "Manifest yeniden uretildi: tools/upstream-manifest.json" -ForegroundColor Green
    Write-Host 'Commit etmeden once farki incele - bu dosya tedarik zinciri guvencemiz.' -ForegroundColor Yellow
    Write-Host ''
    return
}

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest yok: $ManifestPath`nIlk uretim icin: .\tools\fetch-upstream.ps1 -UpdateManifest"
}

$manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json

if ($manifest.bundleCommit -ne $BundleCommit -or $manifest.zapretTag -ne $ZapretTag) {
    throw @"
Manifest, script'te sabitlenen surumle uyusmuyor.
  manifest : bundle=$($manifest.bundleCommit) zapret=$($manifest.zapretTag)
  script   : bundle=$BundleCommit zapret=$ZapretTag
Surumu bilerek yukselttiysen: .\tools\fetch-upstream.ps1 -UpdateManifest
"@
}

$failed = @()
foreach ($dest in $downloaded.Keys) {
    $expected = $manifest.files.$dest
    if (-not $expected) { $failed += "$dest - manifest'te kayitli degil"; continue }
    if ($expected -ne $downloaded[$dest]) {
        $failed += "$dest - beklenen $expected, gelen $($downloaded[$dest])"
    }
}

if ($failed.Count -gt 0) {
    Write-Host 'SHA256 DOGRULAMA BASARISIZ' -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ''
    throw 'Upstream dosyalari beklenen hash ile eslesmiyor. Indirilenler kullanilmamali.'
}

Write-Host "SHA256 dogrulandi - $($downloaded.Count) dosya." -ForegroundColor Green
Write-Host "Konum: vendor/zapret-winws/"
Write-Host ''
