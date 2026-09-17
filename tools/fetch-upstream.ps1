<#
.SYNOPSIS
    zapret/winws ikililerini sabitlenmiş bir upstream sürümünden indirir ve doğrular.

.DESCRIPTION
    ZapretTR, zapret'in C kodunu fork'lamaz. Yalnızca çalışma zamanında gereken
    ikilileri (winws.exe, WinDivert sürücüsü, WinDivert filtre parçaları, sahte
    QUIC yükü) upstream'den çeker.

    Sürüm sabitleme commit SHA'sı ile yapılır: zapret-win-bundle deposunda etiket yok,
    ama commit SHA'sı içerik adresli, yani etiketten daha güçlü bir garanti verir.

    Her dosyanın SHA256'sı tools/upstream-manifest.json içinde tutulur ve indirme
    sonrası doğrulanır. Upstream sürümünü yükseltmek için: aşağıdaki $BundleCommit /
    $ZapretTag değerlerini değiştir, sonra -UpdateManifest ile çalıştır, sonra
    oluşan manifest farkını incele ve commit'le.

.PARAMETER UpdateManifest
    Doğrulama yapmak yerine manifest'i indirilen dosyalara göre yeniden üretir.
    Upstream sürümü yükseltilirken kullanılır. Manifest değişikliği kod incelemesinde
    görünür olsun diye ayrı bir bayrak olarak bırakıldı.

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

# --- Sabitlenmiş upstream sürümleri ------------------------------------------
# zapret-win-bundle @ 2026-08-23. Bu depoda etiket yok, commit SHA ile sabitliyoruz.
$BundleCommit = '32fbbebf29be855566faef0961ac85627a3a4aeb'
# Ana zapret deposu, lisans metni ve sahte TLS yükleri için.
$ZapretTag    = 'v72.13'
# dnscrypt-proxy: DNS kaçırmasını aşmak için. Türkiye'de engelleme çoğu zaman
# önce DNS katmanında oluyor ve o katman aşılmadan DPI stratejisi işe yaramıyor.
$DnsCryptVersion = '2.1.18'
$DnsCryptSha256  = '15f0c8f1f40620a54ddfd8752c327dabe1146f84618d68874f79c4f52490b396'

$BundleBase = "https://raw.githubusercontent.com/bol-van/zapret-win-bundle/$BundleCommit/zapret-winws"
$ZapretBase = "https://raw.githubusercontent.com/bol-van/zapret/$ZapretTag"

# --- İndirilecek dosyalar -----------------------------------------------------
# Liste dar tutuldu (winws2.exe, lua/ ve örnek preset .cmd dosyaları alınmıyor),
# ama DARLIK BAĞIMLILIK ATLAMAK DEĞİL. İlk sürümde cygwin1.dll "gereksiz 3MB"
# diye çıkarılmıştı; winws.exe cygwin ile derlendiği için o dosya olmadan hiç
# başlamıyor ve "cygwin1.dll was not found" diye KALICI (modal) bir hata penceresi
# açıyor; süreç ölmediği için çağıran taraf da sonsuza kadar bekliyor.
#
# Bu listeyi budarken önce bağımlılıkları doğrula:
#   winws.exe -> ADVAPI32, KERNEL32, ole32, OLEAUT32, wlanapi  (Windows sistem)
#                cygwin1.dll, WinDivert.dll                     (bundle içinden)
$Files = @(
    # Motor ve sürücü
    @{ Url = "$BundleBase/winws.exe";        Dest = 'winws.exe' }
    @{ Url = "$BundleBase/cygwin1.dll";      Dest = 'cygwin1.dll' }
    @{ Url = "$BundleBase/WinDivert.dll";    Dest = 'WinDivert.dll' }
    @{ Url = "$BundleBase/WinDivert64.sys";  Dest = 'WinDivert64.sys' }

    # WinDivert ham filtre parçaları (--wf-raw-part ile kullanılır).
    # discord_media ve stun, Discord sesli görüşme için gerekli.
    @{ Url = "$BundleBase/windivert.filter/windivert_part.discord_media.txt"     ; Dest = 'windivert.filter/windivert_part.discord_media.txt' }
    @{ Url = "$BundleBase/windivert.filter/windivert_part.stun.txt"              ; Dest = 'windivert.filter/windivert_part.stun.txt' }
    @{ Url = "$BundleBase/windivert.filter/windivert_part.quic_initial_ietf.txt" ; Dest = 'windivert.filter/windivert_part.quic_initial_ietf.txt' }
    @{ Url = "$BundleBase/windivert.filter/README.txt"                           ; Dest = 'windivert.filter/README.txt' }

    # Sahte paket yükleri. TLS için ayrı dosyaya gerek yok: --dpi-desync-fake-tls-mod
    # ile SNI çalışma zamanında üretilebiliyor. QUIC için hazır yük şart; upstream'de
    # --dpi-desync-fake-quic-mod diye bir karşılığı YOK, tek eksen hazır yükün kendisi.
    @{ Url = "$BundleBase/files/quic_initial_www_google_com.bin" ; Dest = 'files/quic_initial_www_google_com.bin' }
    @{ Url = "$ZapretBase/files/fake/tls_clienthello_iana_org.bin" ; Dest = 'files/tls_clienthello_iana_org.bin' }

    # QUIC sahte yük çeşitleri. Tek bir yükle sınırlı kalmak arama uzayını yapay
    # olarak daraltıyordu: hangi yükün işe yaradığı DPI kutusunun neyi doğruladığına
    # bağlı ve bunlar farklı QUIC yığınlarından (Chrome, mvfst, quiche) üretilmiş.
    # kyber varyantları büyük ClientHello üretenler için; Discord istemcisi Chromium
    # tabanlı ve ölçüldüğü kadarıyla ClientHello'yu 2.5 KB'a yayıyor.
    @{ Url = "$ZapretBase/files/fake/quic_initial_facebook_com.bin"       ; Dest = 'files/quic_initial_facebook_com.bin' }
    @{ Url = "$ZapretBase/files/fake/quic_initial_vk_com.bin"             ; Dest = 'files/quic_initial_vk_com.bin' }
    @{ Url = "$ZapretBase/files/fake/quic_initial_rutracker_org_kyber_1.bin" ; Dest = 'files/quic_initial_rutracker_org_kyber_1.bin' }
    @{ Url = "$ZapretBase/files/fake/quic_short_header.bin"               ; Dest = 'files/quic_short_header.bin' }

    # --dpi-desync-udplen-pattern için dolgu deseni. Varsayılan dolgu sıfır; DPI
    # sıfır dolguyu eleyip paketi yine tanıyorsa desenin değişmesi gerekiyor.
    @{ Url = "$ZapretBase/files/fake/zero_512.bin" ; Dest = 'files/zero_512.bin' }

    # MIT lisans metni; dağıtımda yanında gitmek zorunda.
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

# Mevcut manifest, artımlı indirme için okunuyor.
$existingHashes = @{}
if (Test-Path $ManifestPath) {
    $existingManifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    foreach ($property in $existingManifest.files.PSObject.Properties) {
        $existingHashes[$property.Name] = $property.Value
    }
}

# --- İndirme ------------------------------------------------------------------
# İndirme ARTIMLI: yerinde duran ve manifest'teki özetiyle birebir aynı olan dosya
# yeniden indirilmez. Önceki hâli "dosya sayısı yeterliyse hepsini atla, değilse
# hepsini indir" şeklindeydi ve listeye YENİ bir dosya eklemek bütün listeyi
# yeniden indirmeyi zorunlu kılıyordu. Bu, WinDivert64.sys bir test koşumundan
# sonra hâlâ çekirdeğe yüklü olduğunda (servis silinse bile sürücü görüntüsü
# kaldırılana kadar kilitli kalır) "dosya başka bir süreç tarafından kullanılıyor"
# ile başarısız oluyor ve tek çözüm yeniden başlatmak oluyordu.
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

# --- Manifest üretimi ya da doğrulama -----------------------------------------
# --- dnscrypt-proxy ----------------------------------------------------------
# Tek dosya değil zip olduğu için ayrı ele alınıyor. Zip'in SHA256'sı betikte
# sabit; indirme sonrası doğrulanmadan açılmaz.
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

    # Zip'in tamamı değil, yalnızca ihtiyacımız olanlar açılıyor: örnek listeler
    # ve servis .bat dosyaları bizim akışımızda kullanılmıyor.
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

# Not: burada bir zamanlar "$skipWinws ise doğrulamayı atla" dalı vardı. İndirme
# hepsi-ya-hiçbiri iken anlamlıydı; artımlı hâle gelince o değişken kalktı ama dal
# kaldı ve StrictMode altında betik HER koşumda tam bu noktada patladı; yani
# dosyalar iniyor, SHA256 doğrulaması hiçbir zaman koşmuyordu. Artımlı akışta
# atlanacak bir şey yok: yerinde duran dosyanın özeti de $downloaded'a yazılıyor,
# dolayısıyla aşağıdaki döngü her zaman TÜM dosyaları doğruluyor.

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
