# LAUNCHER-UI: screenshots of every page and dialog at 100% and 150% display scale (simulated, see Tabs/Shots.cs).
# Builds a fake play folder in -Scratch (the "game exe" there is a text file holding the native marker, never a program),
# then runs the launcher with the hidden --screenshot-all option twice. Nothing is started but the launcher itself.
#   release\launcher\tests\screenshots.ps1 [-Out <png folder>] [-Scratch <scratch folder>]
param(
  [string]$Out = (Join-Path $PSScriptRoot '..\..\..\reports\att-launcher-ui'),
  [string]$Scratch = (Join-Path $env:TEMP 'rrxx-launcher-shots')
)
$ErrorActionPreference = 'Stop'
$work = Join-Path $Scratch 'play'
$pub = Join-Path $Scratch '_publish'
dotnet publish (Join-Path (Split-Path $PSScriptRoot -Parent) 'RRXXLauncher.csproj') -c Release -o $pub -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
$game = "$work\game"
New-Item -ItemType Directory -Force "$game\assets\pac", "$work\attire" | Out-Null
Copy-Item "$pub\RRXX Launcher.exe" $work
[IO.File]::WriteAllText("$game\rumble_roses_xx.exe", 'not a program; rrxx native: renderer on (fake marker)')
Set-Content "$game\assets\default.xex" 'fake'
foreach ($n in 'rrxx_shader_cache.exe','XenosRecomp.exe','rrxx_prelink.exe','dxcompiler.dll','dxil.dll') { Set-Content "$game\$n" 'fake' }
Set-Content "$game\RumbleRosesReX.toml" "resolution_scale = 3`nbloom = 1`nrrxx_renderer = `"native`"`n"
Set-Content "$work\launcher.json" '{ "CheckForUpdates": false }'
foreach ($m in 'Pink tag-team gear','Velvet ring robe','Sample costume 3') {
  New-Item -ItemType Directory -Force "$work\mods\costumes\$m" | Out-Null; Set-Content "$work\mods\costumes\$m\cos.dat" '7' }
$dlc = "$work\saves\0000000000000000\4B4E07D1\00000002"
$divas = @(); $cos = @()
Add-Type -AssemblyName System.Drawing
for ($e = 1; $e -le 6; $e++) {
  $divas += @{ edit = $e; name = "Sample diva $e"; default = 600 + $e * 2 }
  foreach ($k in 0, 1) {
    $id = 600 + $e * 2 + $k
    $cos += @{ id = $id; edit = $e; label = "Sample outfit $id" }
    New-Item -ItemType Directory -Force "$dlc\RRXXMOD0$id\e$id" | Out-Null; Set-Content "$dlc\RRXXMOD0$id\cos.dat" "$id"
    if ($k -eq 0) {  # abstract placeholder art (gradient + circle), not a game render
      $b = New-Object Drawing.Bitmap 300, 200; $g = [Drawing.Graphics]::FromImage($b)
      $br = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Rectangle]::new(0,0,300,200)), ([Drawing.Color]::FromArgb(60,20,70)), ([Drawing.Color]::FromArgb(30,10,40)), 90
      $g.FillRectangle($br, 0, 0, 300, 200); $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(90,255,80,160))), 110, 40, 80, 120)
      $g.Dispose(); $b.Save("$work\attire\$id.jpg", [Drawing.Imaging.ImageFormat]::Jpeg); $b.Dispose() }
  }
}
@{ divas = $divas; costumes = $cos } | ConvertTo-Json -Depth 4 | Set-Content "$work\attire\attire.json"
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
Get-ChildItem $Out -Filter *.png | Where-Object { $_.Name -notlike '*-open.png' } | Remove-Item   # dropdown shots: on-screen run only
foreach ($p in 100, 150) {
  $r = Start-Process "$work\RRXX Launcher.exe" -ArgumentList '--screenshot-all', $Out, $p -Wait -PassThru
  "scale $p exit $($r.ExitCode)"
}
Get-ChildItem $Out | Select-Object Name, Length | Format-Table -AutoSize
