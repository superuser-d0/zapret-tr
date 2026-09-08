<#
.SYNOPSIS
    Bir surumun yayin notunu CHANGELOG.md'den uretir.

.DESCRIPTION
    Yayin notunun basina "bu surumde ne degisti" ozetini koyar, ardindan her
    surumde ayni kalan kurulum/guvenlik metnini ekler.

    Neden uretiliyor da elle yazilmiyor: elle yazilan not, yazmayi unuttugun
    surumde sessizce bir onceki surumun notuyla cikar. Kullanici indirdigi
    pakette neyin duzeldigini goremez -- ki bir hata bildirdiyse tam olarak
    bunu ariyordur. CHANGELOG zaten tutuluyor; TEK kaynak o olsun.

    CHANGELOG'da o surumun bolumu yoksa is BASARISIZ olur. Sessizce bos not
    yayinlamak, notu unutmakla ayni sonucu verirdi.
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

# Bolum "## [x.y.z]" ile baslar ve bir sonraki "## " basligina kadar surer.
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

# Baslik satirini atiyoruz: yayin sayfasi zaten surumu basligindan gosteriyor.
$body = $lines[($start + 1)..($end - 1)]
$body = ($body -join "`n").Trim()

if ([string]::IsNullOrWhiteSpace($body)) {
    throw "'## [$Version]' bolumu bos. Yayindan once ne degistigini yazin."
}

$preamble = (Get-Content $PreamblePath -Encoding UTF8) -join "`n"

$note = @"
## Bu sürümde neler düzeldi

$body

---

$preamble
"@

# Out-File, Windows PowerShell 5.1'de BOM ekliyor ve BOM yayin notunun ilk
# basligina gorunmez bir karakter olarak sizabiliyor.
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
# .NET'in calisma dizini PowerShell'inkiyle ayni olmayabiliyor, o yuzden goreli
# yol burada elle mutlaklastiriliyor.
$fullOut = if ([System.IO.Path]::IsPathRooted($OutPath)) {
    $OutPath
} else {
    Join-Path (Get-Location).Path $OutPath
}
[System.IO.File]::WriteAllText($fullOut, $note, $utf8NoBom)
Write-Host "Yayin notu uretildi: $fullOut ($($note.Length) karakter)"
