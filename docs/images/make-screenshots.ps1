# Retakes the README screenshots (screenshot-light.png, screenshot-dark.png): a Release build in
# sample mode, Disk 1 selected, captured from the app window alone (nothing behind it shows through).
# Run from the repository root:  pwsh docs/images/make-screenshots.ps1
$ErrorActionPreference = 'Stop'
$build = Join-Path ([IO.Path]::GetTempPath()) "winparted-screenshots-$PID"
dotnet publish src/WinParted.App -c Release -r win-x64 -o $build | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
}
"@
[Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
$env:__COMPAT_LAYER = 'RunAsInvoker'   # Release builds ask for admin rights; sample mode doesn't need them.

function Capture($theme, $out) {
    $p = Start-Process (Join-Path $build 'WinParted.exe') "--sample --theme $theme --culture en-US --select 1:0" -PassThru
    try {
        while ($p.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 300; $p.Refresh() }
        Start-Sleep 8
        $h = $p.MainWindowHandle
        [Win]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 800

        # Render the window itself (PW_RENDERFULLCONTENT), then crop to the visible frame.
        $win = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$win) | Out-Null
        $vis = New-Object Win+RECT; [Win]::DwmGetWindowAttribute($h, 9, [ref]$vis, 16) | Out-Null
        $full = New-Object System.Drawing.Bitmap ($win.R - $win.L), ($win.B - $win.T)
        $g = [System.Drawing.Graphics]::FromImage($full); $hdc = $g.GetHdc()
        [Win]::PrintWindow($h, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc); $g.Dispose()
        $x = $vis.L - $win.L; $y = $vis.T - $win.T; $w = $vis.R - $vis.L; $ht = $vis.B - $vis.T

        # PrintWindow leaves the window border unpainted (black): trim it.
        function IsBlack($c) { $c.R -lt 8 -and $c.G -lt 8 -and $c.B -lt 8 }
        $midX = $x + [int]($w / 2); $midY = $y + [int]($ht / 2)
        while (IsBlack $full.GetPixel($x, $midY)) { $x++; $w-- }
        while (IsBlack $full.GetPixel($x + $w - 1, $midY)) { $w-- }
        while (IsBlack $full.GetPixel($midX, $y)) { $y++; $ht-- }
        while (IsBlack $full.GetPixel($midX, $y + $ht - 1)) { $ht-- }

        # Copy into a shape with Windows 11's rounded corners (8 px at 100 %), transparent outside.
        $d = 2 * [int][Math]::Round(8 * [Win]::GetDpiForWindow($h) / 96.0)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
        $path.AddArc($w - $d, $ht - $d, $d, $d, 0, 90); $path.AddArc(0, $ht - $d, $d, $d, 90, 90)
        $bmp = New-Object System.Drawing.Bitmap $w, $ht, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.FillPath((New-Object System.Drawing.TextureBrush $full, (New-Object System.Drawing.Rectangle $x, $y, $w, $ht)), $path)
        $g.Dispose(); $full.Dispose()
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        "$out ($w x $ht)"
    }
    finally {
        if (-not $p.HasExited) { $p.Kill() }
    }
}

try {
    Capture light (Join-Path $PSScriptRoot 'screenshot-light.png')
    Capture dark (Join-Path $PSScriptRoot 'screenshot-dark.png')
}
finally {
    Remove-Item Env:__COMPAT_LAYER
    Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
}
