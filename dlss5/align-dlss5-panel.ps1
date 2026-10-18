# Keeps the hidden DLSS 5 host window exactly under the in-game DLSS 5 panel, so OptiScaler's menu
# (which maps clicks from the real cursor position against its own window) gets clicks where you see them.
# Started by "Play with DLSS 5.bat"; exits when the game exits. Log: align-dlss5-panel.log
param([string]$FeedLog = "$PSScriptRoot\dlss5-feed.log", [string]$OptiLog = "$PSScriptRoot\host64\OptiScaler.log")
Add-Type @"
using System; using System.Runtime.InteropServices;
public struct RECT { public int L,T,R,B; } public struct PT { public int X,Y; }
public static class V { [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
 [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); }
"@
[V]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$log = "$PSScriptRoot\align-dlss5-panel.log"; "start $(Get-Date -f T)" | Set-Content $log
$seen = $false; $last = ''
for ($i = 0; $i -lt 36000; $i++) {
    Start-Sleep -Milliseconds 500
    $gp = Get-Process rumble_roses_xx -EA 0 | Select-Object -First 1
    if (-not $gp) { if ($seen -or $i -gt 240) { break } else { continue } }
    $seen = $true
    $hp = Get-Process dlss5-feed-host64 -EA 0 | Select-Object -First 1
    if (-not $hp -or $hp.MainWindowHandle -eq 0 -or $gp.MainWindowHandle -eq 0) { continue }
    $m = Select-String -Path $FeedLog -Pattern 'told the host the panel is (shown at (-?\d+),(-?\d+)-|hidden)' -EA 0 | Select-Object -Last 1
    if (-not $m) { continue }
    $g = $gp.MainWindowHandle; $h = $hp.MainWindowHandle
    if ($m.Matches[0].Groups[1].Value -eq 'hidden') {
        # Panel hidden: send the host window back behind everything, out of the way of clicks.
        if ($last -ne 'hidden') { [V]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x13) | Out-Null; [V]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x13) | Out-Null
            Add-Content $log "$(Get-Date -f T) panel hidden -> host window to the back"; $last = 'hidden' }
        continue
    }
    $px = [int]$m.Matches[0].Groups[2].Value; $py = [int]$m.Matches[0].Groups[3].Value
    $go = New-Object PT; [V]::ClientToScreen($g, [ref]$go) | Out-Null
    $hr = New-Object RECT; [V]::GetWindowRect($h, [ref]$hr) | Out-Null
    $ho = New-Object PT; [V]::ClientToScreen($h, [ref]$ho) | Out-Null
    $wantX = $go.X + $px; $wantY = $go.Y + $py
    # OptiScaler's menu only takes mouse input when its window can be focused: drop WS_EX_NOACTIVATE.
    $ex = [V]::GetWindowLong($h, -20); if ($ex -band 0x08000000) { [V]::SetWindowLong($h, -20, ($ex -band (-bnot 0x08000000))) | Out-Null }
    # No resize border (WS_THICKFRAME|WS_MAXIMIZEBOX): its edge showed a <-> cursor over the game.
    $st = [V]::GetWindowLong($h, -16); if ($st -band 0x00050000) { [V]::SetWindowLong($h, -16, ($st -band (-bnot 0x00050000))) | Out-Null; [V]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 0, 0, 0x37) | Out-Null }
    # Panel shown: the REAL host window sits on top (topmost, never activates: WS_EX_NOACTIVATE) exactly over the
    # panel, so real clicks reach OptiScaler's menu and the game keeps focus. SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW.
    [V]::SetWindowPos($h, [IntPtr](-1), $wantX - ($ho.X - $hr.L), $wantY - ($ho.Y - $hr.T), 0, 0, 0x51) | Out-Null
    $msg = "panel shown at ($wantX,$wantY)"
    if ($msg -ne $last) {
        # Just shown: open OptiScaler's menu if it is closed (it starts closed after every host (re)start).
        # Its log records each change as "menu visibility changed A -> B"; no line yet = closed.
        Start-Sleep -Milliseconds 300
        $v = Select-String -Path $OptiLog -Pattern 'menu visibility changed \d -> (\d)' -EA 0 | Select-Object -Last 1
        if (-not $v -or $v.Matches[0].Groups[1].Value -eq '0') {
            [V]::PostMessage($h, 0x100, [IntPtr]0x2D, [IntPtr]0x01520001) | Out-Null   # WM_KEYDOWN Insert
            Start-Sleep -Milliseconds 50
            [V]::PostMessage($h, 0x101, [IntPtr]0x2D, [IntPtr]([int64]0xC1520001)) | Out-Null   # WM_KEYUP
            Add-Content $log "$(Get-Date -f T) OptiScaler menu was closed -> sent Insert"
        }
        Add-Content $log "$(Get-Date -f T) $msg -> host window on top there (was client $($ho.X),$($ho.Y))"; $last = $msg }
}
Add-Content $log "end $(Get-Date -f T)"
