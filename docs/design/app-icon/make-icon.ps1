# Builds src/WinParted.App/Assets/WinParted.ico (and the PNG used in the app) from the SVGs here.
# Needs ImageMagick 7 (`magick`). Run from the repository root:  pwsh docs/design/app-icon/make-icon.ps1
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$assets = Join-Path $here '..\..\..\src\WinParted.App\Assets'
$temp = Join-Path ([IO.Path]::GetTempPath()) "winparted-icon-$PID"
New-Item -ItemType Directory -Force $temp, $assets | Out-Null
try {
    $layers = foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
        # Small sizes come from hand-tuned SVGs, rendered at their exact size so edges stay sharp.
        $source, $base = if ($size -le 16) { 'winparted-16.svg', 16 } elseif ($size -le 32) { 'winparted-32.svg', 32 } else { 'winparted.svg', 256 }
        $png = Join-Path $temp "$size.png"
        magick -background none -density (96 * $size / $base) (Join-Path $here $source) -resize "${size}x${size}!" $png
        if ($LASTEXITCODE -ne 0) { throw "magick failed for $size px" }
        $png
    }
    magick @layers (Join-Path $assets 'WinParted.ico')
    if ($LASTEXITCODE -ne 0) { throw 'magick failed to write the .ico' }
    Copy-Item (Join-Path $temp '64.png') (Join-Path $assets 'WinParted-64.png')
}
finally {
    Remove-Item -Recurse -Force $temp
}
