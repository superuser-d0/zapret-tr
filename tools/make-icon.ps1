<#
.SYNOPSIS
    Uygulama simgesini (.ico) ve kurulum sihirbazının küçük resmini üretir.

.DESCRIPTION
    0.1.21'e kadar uygulamanın kendi simgesi yoktu: exe, görev çubuğu, bildirim
    alanı ve pencere başlığı .NET'in varsayılan simgesini, kurulum sihirbazı da
    Inno Setup'ın varsayılan resmini gösteriyordu. Bildirim alanında ZapretTR
    başka bir .NET uygulamasından ayırt edilemiyordu.

    Simge çizilerek değil KODLA üretiliyor: kaynak dosyası (SVG, PSD) tutmaya ve
    onu okuyacak bir araç kurmaya gerek kalmıyor, değişiklik bu betikte görünüyor.
    Çıktı dosyaları depoda duruyor; derleme bu betiği ÇALIŞTIRMIYOR.

    Her boyut ayrı çiziliyor ve ölçüler piksele yuvarlanıyor. 256'dan küçültülmüş
    bir 16x16, Z'nin çizgilerini yarım piksele düşürüp bulanıklaştırıyordu.

    .ico içinde 256 PNG, diğerleri 32 bit DIB olarak yazılıyor: PNG girdileri her
    aracın (Inno Setup'ın simge gömücüsü dahil) okuduğu biçim değil.

.EXAMPLE
    powershell -NoProfile -File tools/make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$IcoPath = 'src/ZapretTr.App/Assets/ZapretTR.ico',
    [string]$WizardDir = 'installer',
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

# Uygulamanın vurgu rengi (App.xaml AccentBrush = #1F5FAF) etrafında dikey geçiş.
$Ust = [System.Drawing.Color]::FromArgb(255, 0x2B, 0x72, 0xC9)
$Alt = [System.Drawing.Color]::FromArgb(255, 0x18, 0x4B, 0x8E)

function New-IconBitmap([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.Clear([System.Drawing.Color]::Transparent)

    # Zemin: yuvarlatılmış kare. 16 ve 20'de kenar boşluksuz; büyüklerde Windows
    # simgelerindeki gibi ince bir boşluk.
    $pad = if ($Size -le 20) { 0 } else { [math]::Round($Size * 0.04) }
    $side = $Size - 2 * $pad
    $r = [math]::Max(3, [math]::Round($side * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($pad + $side - 2 * $r, $pad, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($pad + $side - 2 * $r, $pad + $side - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($pad, $pad + $side - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, $side, $side
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $Ust, $Alt, 90.0
    $g.FillPath($brush, $path)

    # Beyaz Z. Kalınlık en az 2 piksel: 16x16'da 1 piksellik çizgi okunmuyor.
    $mx = [math]::Round($Size * 0.25)
    $my = [math]::Round($Size * 0.23)
    $t = [math]::Max(2, [math]::Round($Size * 0.12))
    $x0 = $mx; $x1 = $Size - $mx; $y0 = $my; $y1 = $Size - $my

    # Çaprazın yatay payı, dik kalınlığı yatay çizgilerle aynı olacak şekilde
    # açıdan hesaplanıyor. Sabit bir katsayıyla çapraz, yatay çizgiler kalın
    # olduğunda yatıklaşıp 16 ve 20 pikselde neredeyse kayboluyordu.
    $yatay = $x1 - $x0
    $dikey = ($y1 - $t) - ($y0 + $t)
    $d = [math]::Round($t * [math]::Sqrt($yatay * $yatay + $dikey * $dikey) / [math]::Max(1, $dikey))
    # Parantezler şart: PowerShell'de virgül toplamadan önce bağlanıyor.
    $pts = @(
        @($x0, $y0), @($x1, $y0), @($x1, ($y0 + $t)),
        @(($x0 + $d), ($y1 - $t)), @($x1, ($y1 - $t)), @($x1, $y1),
        @($x0, $y1), @($x0, ($y1 - $t)),
        @(($x1 - $d), ($y0 + $t)), @($x0, ($y0 + $t))
    ) | ForEach-Object { New-Object System.Drawing.PointF $_[0], $_[1] }
    $g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$pts)

    $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$Bmp) {
    $w = $Bmp.Width; $h = $Bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER; yükseklik renk + maske için iki kat.
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    # Pikseller alttan üste, BGRA.
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $Bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    # AND maskesi: alfa kanalı varken hepsi sıfır.
    $maskRow = [int]([math]::Floor(($w + 31) / 32) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $h)))
    $bw.Flush()
    return ,$ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$Bmp) {
    $ms = New-Object System.IO.MemoryStream
    $Bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

# --- .ico ---------------------------------------------------------------------
# 20/40: %125 ve %250 ölçekte bildirim alanı; 24/48: %150 ve %300.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $bytes = if ($s -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    [pscustomobject]@{ Size = $s; Bytes = $bytes; Bitmap = $bmp }
}

$IcoFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $IcoPath))
New-Item -ItemType Directory -Force -Path (Split-Path $IcoFull) | Out-Null
$fs = [System.IO.File]::Create($IcoFull)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$img.Bytes.Length); $bw.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Bytes) }
$bw.Dispose(); $fs.Dispose()
Write-Host "Yazildi: $IcoPath ($((Get-Item $IcoFull).Length) bayt, boyutlar: $($sizes -join ', '))"

# --- Kurulum sihirbazı küçük resmi --------------------------------------------
# Modern sihirbazın sağ üst köşesi. Inno Setup ekran ölçeğine göre listeden en
# yakınını seçiyor; %100 için 55, %200 için 110. Beyaz zemine 24 bit BMP: bu
# biçimi her Inno Setup 6 sürümü okuyor.
$WizardFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $WizardDir))
foreach ($s in 55, 110) {
    $icon = New-IconBitmap ([math]::Round($s * 0.82))
    $canvas = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::White)
    $o = [math]::Floor(($s - $icon.Width) / 2)
    $g.DrawImage($icon, $o, $o, $icon.Width, $icon.Height)
    $g.Dispose()
    $out = Join-Path $WizardFull "wizard-small-$s.bmp"
    $canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $canvas.Dispose(); $icon.Dispose()
    Write-Host "Yazildi: $out"
}

# --- İsteğe bağlı önizleme: her boyut 8 kat büyütülmüş, yan yana --------------
if ($PreviewPath) {
    $scale = 8
    $width = [int]($sizes | Where-Object { $_ -le 64 } | ForEach-Object { $_ * $scale + 16 } | Measure-Object -Sum).Sum
    $prev = New-Object System.Drawing.Bitmap $width, (64 * $scale + 16)
    $g = [System.Drawing.Graphics]::FromImage($prev)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 0xF4, 0xF6, 0xF9))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $x = 8
    foreach ($img in $images | Where-Object { $_.Size -le 64 }) {
        $g.DrawImage($img.Bitmap, $x, 8, $img.Size * $scale, $img.Size * $scale)
        $x += $img.Size * $scale + 16
    }
    $g.Dispose()
    $prev.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $prev.Dispose()
    Write-Host "Onizleme: $PreviewPath"
}

foreach ($img in $images) { $img.Bitmap.Dispose() }
