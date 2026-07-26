Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@
$p = Get-Process Pos.App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "NO_WINDOW"; exit 1 }
$h = $p.MainWindowHandle
$TOPMOST = [IntPtr](-1); $NOTOPMOST = [IntPtr](-2)
$FLAGS = [uint32](0x1 -bor 0x2 -bor 0x40)   # NOSIZE|NOMOVE|SHOWWINDOW
[Win]::ShowWindow($h, 9) | Out-Null          # SW_RESTORE
[Win]::SetWindowPos($h, $TOPMOST, 0, 0, 0, 0, $FLAGS) | Out-Null
Start-Sleep -Milliseconds 600
$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
$out = Join-Path $env:TEMP 'pos_shot.png'
$bmp.Save($out)
$g.Dispose(); $bmp.Dispose()
[Win]::SetWindowPos($h, $NOTOPMOST, 0, 0, 0, 0, $FLAGS) | Out-Null
Write-Output $out
