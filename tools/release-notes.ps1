<#
.SYNOPSIS
    Bir sürümün yayın notunu CHANGELOG.md'den üretir.

.DESCRIPTION
    Yayın notunun başına "bu sürümde ne değişti" özetini koyar, ardından her
    sürümde aynı kalan kurulum/güvenlik metnini ekler.

    Neden üretiliyor da elle yazılmıyor: elle yazılan not, yazmayı unuttuğun
    sürümde sessizce bir önceki sürümün notuyla çıkar. Kullanıcı indirdiği
    pakette neyin düzeldiğini göremez; bir hata bildirdiyse tam olarak bunu
    arıyordur. CHANGELOG zaten tutuluyor; TEK kaynak o olsun.

    CHANGELOG'da o sürümün bölümü yoksa iş BAŞARISIZ olur. Sessizce boş not
    yayınlamak, notu unutmakla aynı sonucu verirdi.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$ChangelogPath = 'CHANGELOG.md',
    [string]$PreamblePath = '.github/release-notes.md',
    [string]$OutPath = 'release-notes-generated.md'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ChangelogPath)) { throw "CHANGELOG bulunamadi: $ChangelogPath" }
if (-not (Test-Path $PreamblePath)) { throw "Yayin notu govdesi bulunamadi: $PreamblePath" }

$lines = Get-Content $ChangelogPath -Encoding UTF8

# Bölüm "## [x.y.z]" ile başlar ve bir sonraki "## " başlığına kadar sürer.
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^##\s*\[$([regex]::Escape($Version))\]") { $start = $i; break }
}
if ($start -lt 0) {
    throw "CHANGELOG'da '## [$Version]' bolumu yok. Yayindan once surum notunu yazin."
}

$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s') { $end = $i; break }
}

# Başlık satırını atıyoruz: yayın sayfası zaten sürümü başlığından gösteriyor.
$body = $lines[($start + 1)..($end - 1)]
$body = ($body -join "`n").Trim()

if ([string]::IsNullOrWhiteSpace($body)) {
    throw "'## [$Version]' bolumu bos. Yayindan once ne degistigini yazin."
}

$preamble = (Get-Content $PreamblePath -Encoding UTF8) -join "`n"

$note = @"
## Bu sürümde neler değişti

$body

---

$preamble
"@

# GÖRELİ BAĞLANTILAR MUTLAK ADRESE ÇEVRİLİYOR.
#
# Yayın sayfası .../releases/tag/vX adresinde duruyor ve GitHub notlardaki göreli
# bağlantıları o adrese göre çözüyor. "../blob/main/CHANGELOG.md" böylece geçersiz bir
# adrese gidiyordu; kullanıcı tıkladığında "404 - Cannot find a valid ref in
# blob/main/CHANGELOG.md" gördü (2026-09-16). CHANGELOG'daki depo içi göreli
# bağlantılar (örneğin docs/SORUN-GIDERME.md#...) depoda doğru, notta ise aynı şekilde
# bozuluyordu. Sayfa içi (#...) ve mutlak bağlantılara dokunulmuyor.
$repoBlob = 'https://github.com/superuser-d0/zapret-tr/blob/main/'
$note = [regex]::Replace(
    $note,
    '\]\((?!https?://|#|mailto:)(?:\.\./blob/main/|\./)?([^)\s]+)\)',
    { param($m) '](' + $repoBlob + $m.Groups[1].Value + ')' })

# Out-File, Windows PowerShell 5.1'de BOM ekliyor ve BOM yayın notunun ilk
# başlığına görünmez bir karakter olarak sızabiliyor.
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
# .NET'in çalışma dizini PowerShell'inkiyle aynı olmayabiliyor, o yüzden göreli
# yol burada elle mutlaklaştırılıyor.
$fullOut = if ([System.IO.Path]::IsPathRooted($OutPath)) {
    $OutPath
} else {
    Join-Path (Get-Location).Path $OutPath
}
[System.IO.File]::WriteAllText($fullOut, $note, $utf8NoBom)
Write-Host "Yayin notu uretildi: $fullOut ($($note.Length) karakter)"
