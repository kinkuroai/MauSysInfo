<#
.SYNOPSIS
    Makes the installer's wizard images from the mascot artwork.
.DESCRIPTION
    Writes installer\wizard\large-*.png (the picture on the left of the welcome and finish pages) and
    small-*.png (top right of the other pages), one per Windows display scale (100%-250%), so they
    stay sharp. Run it again after changing installer\art\mascot.png.
.PARAMETER Source
    The artwork: a square image with a transparent background (PNG, WebP...). It is copied to
    installer\art\mascot.png first.
#>
param([string]$Source = "$PSScriptRoot\art\mascot.png")

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

function Load-Bitmap($path) {
    $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create([Uri](Resolve-Path $path).Path,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    # Some decoders (WebP) hand back a format WPF draws without its transparency: convert to BGRA first.
    $converted = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($decoder.Frames[0], [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $converted.Freeze()
    $converted
}

function Save-Png($source, $path) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($source))
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

function Render($width, $height, [scriptblock]$draw) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [System.Windows.Media.BitmapScalingMode]::HighQuality)
    $dc = $visual.RenderOpen()
    & $draw $dc $width $height
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($width, $height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap
}

function Color($hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

# Keep a copy of the artwork in the repository.
$artDir = Join-Path $PSScriptRoot 'art'
$outDir = Join-Path $PSScriptRoot 'wizard'
New-Item -ItemType Directory -Force $artDir, $outDir | Out-Null
$mascotPath = Join-Path $artDir 'mascot.png'
$art = Load-Bitmap $Source
if ((Resolve-Path $Source).Path -ne $mascotPath) { Save-Png $art $mascotPath }

# Sizes Inno Setup's modern wizard uses at 100, 125, 150, 175, 200, 225 and 250% display scale.
$large = @(@(164, 314), @(192, 386), @(246, 459), @(273, 556), @(328, 604), @(355, 700), @(410, 797))
$small = @(@(55, 55), @(64, 68), @(83, 80), @(92, 97), @(110, 106), @(119, 123), @(138, 140))

foreach ($size in $large) {
    $image = Render $size[0] $size[1] {
        param($dc, $w, $h)
        # Dark plum to crimson, matching the artwork's hair colours.
        $background = New-Object System.Windows.Media.LinearGradientBrush((Color '#2A1630'), (Color '#7A1838'), 90.0)
        $dc.DrawRectangle($background, $null, (New-Object System.Windows.Rect(0, 0, $w, $h)))

        $art_size = $w * 0.94
        $cx = $w / 2; $cy = $h * 0.42
        # Soft glow behind the mascot.
        $glow = New-Object System.Windows.Media.RadialGradientBrush((Color '#66FF5C8A'), (Color '#00FF5C8A'))
        $dc.DrawEllipse($glow, $null, (New-Object System.Windows.Point($cx, $cy)), $art_size * 0.62, $art_size * 0.62)
        $dc.DrawImage($art, (New-Object System.Windows.Rect(($cx - $art_size / 2), ($cy - $art_size / 2), $art_size, $art_size)))

        $typeface = New-Object System.Windows.Media.Typeface((New-Object System.Windows.Media.FontFamily('Segoe UI')),
            [System.Windows.FontStyles]::Normal, [System.Windows.FontWeights]::SemiBold, [System.Windows.FontStretches]::Normal)
        $text = New-Object System.Windows.Media.FormattedText('Mau SysInfo', [Globalization.CultureInfo]::InvariantCulture,
            [System.Windows.FlowDirection]::LeftToRight, $typeface, ($w * 0.1), [System.Windows.Media.Brushes]::White, 1.0)
        $dc.DrawText($text, (New-Object System.Windows.Point((($w - $text.Width) / 2), ($cy + $art_size / 2 + $h * 0.035))))
    }
    Save-Png $image (Join-Path $outDir ("large-{0}x{1}.png" -f $size[0], $size[1]))
}

foreach ($size in $small) {
    $image = Render $size[0] $size[1] {
        param($dc, $w, $h)
        $side = [Math]::Min($w, $h)
        $dc.DrawImage($art, (New-Object System.Windows.Rect((($w - $side) / 2), (($h - $side) / 2), $side, $side)))
    }
    Save-Png $image (Join-Path $outDir ("small-{0}x{1}.png" -f $size[0], $size[1]))
}

Get-ChildItem $outDir | Select-Object Name, Length

