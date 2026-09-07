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
# dnscrypt-proxy: DNS kacirmasini asmak icin. Turkiye'de engelleme cogu zaman
# once DNS katmaninda oluyor ve o katman asilmadan DPI stratejisi ise yaramiyor.
$DnsCryptVersion = '2.1.18'
$DnsCryptSha256  = '15f0c8f1f40620a54ddfd8752c327dabe1146f84618d68874f79c4f52490b396'

$BundleBase = "https://raw.githubusercontent.com/bol-van/zapret-win-bundle/$BundleCommit/zapret-winws"
$ZapretBase = "https://raw.githubusercontent.com/bol-van/zapret/$ZapretTag"

# --- Indirilecek dosyalar -----------------------------------------------------
# Liste dar tutuldu (winws2.exe, lua/ ve ornek preset .cmd dosyalari alinmiyor),
# ama DARLIK BAGIMLILIK ATLAMAK DEGIL. Ilk surumde cygwin1.dll "gereksiz 3MB"
# diye cikarilmisti; winws.exe cygwin ile derlendigi icin o dosya olmadan hic
# baslamiyor ve "cygwin1.dll was not found" diye MODAL bir hata penceresi
# aciyor -- surec olmedigi icin cagiran taraf da sonsuza kadar bekliyor.
#
# Bu listeyi budarken once bagimliliklari dogrula:
#   winws.exe -> ADVAPI32, KERNEL32, ole32, OLEAUT32, wlanapi  (Windows sistem)
#                cygwin1.dll, WinDivert.dll                     (bundle icinden)
$Files = @(
    # Motor ve surucu
    @{ Url = "$BundleBase/winws.exe";        Dest = 'winws.exe' }
    @{ Url = "$BundleBase/cygwin1.dll";      Dest = 'cygwin1.dll' }
    @{ Url = "$BundleBase/WinDivert.dll";    Dest = 'WinDivert.dll' }
    @{ Url = "$BundleBase/WinDivert64.sys";  Dest = 'WinDivert64.sys' }

    # WinDivert ham filtre parcalari (--wf-raw-part ile kullanilir).
    # discord_media ve stun, Discord sesli gorusme icin gerekli.
    @{ Url = "$BundleBase/windivert.filter/windivert_part.discord_media.txt"     ; Dest = 'windivert.filter/windivert_part.discord_media.txt' }
    @{ Url = "$BundleBase/windivert.filter/windivert_part.stun.txt"              ; Dest = 'windivert.filter/windivert_part.stun.txt' }
    @{ Url = "$BundleBase/windivert.filter/windivert_part.quic_initial_ietf.txt" ; Dest = 'windivert.filter/windivert_part.quic_initial_ietf.txt' }
    @{ Url = "$BundleBase/windivert.filter/README.txt"                           ; Dest = 'windivert.filter/README.txt' }

    # Sahte paket yukleri. TLS icin ayri dosyaya gerek yok: --dpi-desync-fake-tls-mod
    # ile SNI runtime'da uretilebiliyor. QUIC icin hazir yuk sart -- upstream'de
    # --dpi-desync-fake-quic-mod diye bir karsiligi YOK, tek eksen hazir yukun kendisi.
    @{ Url = "$BundleBase/files/quic_initial_www_google_com.bin" ; Dest = 'files/quic_initial_www_google_com.bin' }
    @{ Url = "$ZapretBase/files/fake/tls_clienthello_iana_org.bin" ; Dest = 'files/tls_clienthello_iana_org.bin' }

    # QUIC sahte yuk cesitleri. Tek bir yukle sinirli kalmak arama uzayini yapay
    # olarak daraltiyordu: hangi yukun ise yaradigi DPI kutusunun neyi dogruladigina
    # bagli ve bunlar farkli QUIC yiginlarindan (Chrome, mvfst, quiche) uretilmis.
    # kyber varyantlari buyuk ClientHello uretenler icin; Discord istemcisi Chromium
    # tabanli ve olculdugu kadariyla ClientHello'yu 2.5 KB'a yayiyor.
    @{ Url = "$ZapretBase/files/fake/quic_initial_facebook_com.bin"       ; Dest = 'files/quic_initial_facebook_com.bin' }
    @{ Url = "$ZapretBase/files/fake/quic_initial_vk_com.bin"             ; Dest = 'files/quic_initial_vk_com.bin' }
    @{ Url = "$ZapretBase/files/fake/quic_initial_rutracker_org_kyber_1.bin" ; Dest = 'files/quic_initial_rutracker_org_kyber_1.bin' }
    @{ Url = "$ZapretBase/files/fake/quic_short_header.bin"               ; Dest = 'files/quic_short_header.bin' }

    # --dpi-desync-udplen-pattern icin dolgu deseni. Varsayilan dolgu sifir; DPI
    # sifir dolguyu eleyip paketi yine tanryorsa desenin degismesi gerekiyor.
    @{ Url = "$ZapretBase/files/fake/zero_512.bin" ; Dest = 'files/zero_512.bin' }

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

New-Item -ItemType Directory -Force -Path $VendorDir | Out-Null

# Mevcut manifest, artimli indirme icin okunuyor.
$existingHashes = @{}
if (Test-Path $ManifestPath) {
    $existingManifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    foreach ($property in $existingManifest.files.PSObject.Properties) {
        $existingHashes[$property.Name] = $property.Value
    }
}

# --- Indirme ------------------------------------------------------------------
# Indirme ARTIMLI: yerinde duran ve manifest'teki ozetiyle birebir ayni olan dosya
# yeniden indirilmez. Onceki hali "dosya sayisi yeterliyse hepsini atla, degilse
# hepsini indir" seklindeydi ve listeye YENI bir dosya eklemek butun listeyi
# yeniden indirmeyi zorunlu kiliyordu. Bu, WinDivert64.sys bir test kosumundan
# sonra hala cekirdege yuklu oldugunda (servis silinse bile surucu goruntusu
# kaldirilana kadar kilitli kalir) "dosya baska bir surec tarafindan kullaniliyor"
# ile basarisiz oluyor ve tek cozum yeniden baslatmak oluyordu.
$downloaded = @{}
foreach ($file in $Files) {
    $destPath = Join-Path $VendorDir $file.Dest
    $destDir  = Split-Path -Parent $destPath
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }

    if (-not $Force -and (Test-Path $destPath) -and $existingHashes.ContainsKey($file.Dest)) {
        $current = (Get-FileHash -Path $destPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($current -eq $existingHashes[$file.Dest]) {
            Write-Step "yerinde    : $($file.Dest)"
            $downloaded[$file.Dest] = $current
            continue
        }
    }

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
# --- dnscrypt-proxy ----------------------------------------------------------
# Tek dosya degil zip oldugu icin ayri ele aliniyor. Zip'in SHA256'si script'te
# sabit; indirme sonrasi dogrulanmadan acilmaz.
$dnsDir = Join-Path $RepoRoot 'vendor/dnscrypt-proxy'
$dnsExe = Join-Path $dnsDir 'dnscrypt-proxy.exe'

if ($Force -or $UpdateManifest -or -not (Test-Path $dnsExe)) {
    $dnsUrl = "https://github.com/DNSCrypt/dnscrypt-proxy/releases/download/$DnsCryptVersion/dnscrypt-proxy-win64-$DnsCryptVersion.zip"
    $dnsZip = Join-Path ([IO.Path]::GetTempPath()) "dnscrypt-proxy-$DnsCryptVersion.zip"

    Write-Step "indiriliyor: dnscrypt-proxy $DnsCryptVersion"
    try {
        Invoke-WebRequest -Uri $dnsUrl -OutFile $dnsZip -UseBasicParsing -TimeoutSec 180
    } catch {
        throw "dnscrypt-proxy indirilemedi: $($_.Exception.Message)"
    }

    $actual = (Get-FileHash -Path $dnsZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $DnsCryptSha256) {
        Remove-Item $dnsZip -Force -ErrorAction SilentlyContinue
        throw "dnscrypt-proxy SHA256 uyusmuyor.`n  beklenen: $DnsCryptSha256`n  gelen   : $actual"
    }

    if (Test-Path $dnsDir) { Remove-Item $dnsDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dnsDir | Out-Null

    # Zip'in tamami degil, yalnizca ihtiyacimiz olanlar aciliyor: ornek listeler
    # ve servis .bat dosyalari bizim akisimizda kullanilmiyor.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($dnsZip)
    try {
        foreach ($wanted in @('win64/dnscrypt-proxy.exe', 'win64/LICENSE')) {
            $entry = $archive.Entries | Where-Object { $_.FullName -eq $wanted }
            if (-not $entry) { throw "Zip icinde beklenen dosya yok: $wanted" }
            $target = Join-Path $dnsDir ([IO.Path]::GetFileName($wanted))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally {
        $archive.Dispose()
        Remove-Item $dnsZip -Force -ErrorAction SilentlyContinue
    }

    Write-Step "dnscrypt-proxy hazir: vendor/dnscrypt-proxy/"
}

Write-Host ''

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

# winws indirmesi atlandiysa dogrulanacak bir sey de yok. Bu dal olmadan script
# sonunda "SHA256 dogrulandi - 0 dosya" yaziyordu: hicbir sey dogrulanmadigi halde
# dogrulama mesaji basmak, yanlis guvence vermenin ta kendisi.
if ($skipWinws) {
    Write-Host 'winws dogrulamasi atlandi (dosyalar zaten yerinde, indirme yapilmadi).' -ForegroundColor Yellow
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
