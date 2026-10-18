# Brings the hidden "DLSS 5 Feed host" window (no taskbar button, sits behind the game) to the front.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); }
"@
$p = Get-Process dlss5-feed-host64 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host "The game with DLSS 5 is not running."; Start-Sleep 3; exit }
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 5) | Out-Null
[W]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null   # topmost, keep size/pos
[W]::SetForegroundWindow($h) | Out-Null
