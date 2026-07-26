# Clicks a point given in Pos.App window coordinates (as seen in shot.ps1 output).
param([int]$X, [int]$Y)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Clk {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
}
"@
$p = Get-Process Pos.App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "NO_WINDOW"; exit 1 }
[Clk]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
$r = New-Object Clk+RECT
[Clk]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
[Clk]::SetCursorPos($r.Left + $X, $r.Top + $Y) | Out-Null
Start-Sleep -Milliseconds 150
[Clk]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
[Clk]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
Write-Output ("CLICKED {0},{1}" -f ($r.Left + $X), ($r.Top + $Y))
