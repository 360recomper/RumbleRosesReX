# Builds the DLSS 5 add-on zip (release/dist/RRXX-DLSS5-Addon-<date>.zip). Contents: our scripts, settings templates,
# README and the v010-0019 release runtime DLL (release/dist/dlss5-runtime/rexruntime.dll). No third-party files.
param([string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
      [string]$Runtime = "$PSScriptRoot\..\dist\dlss5-runtime\rexruntime.dll")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$src = $PSScriptRoot
$out = Join-Path (Resolve-Path "$src\..\dist") "RRXX-DLSS5-Addon-$Date.zip"
$map = [ordered]@{
  'Setup DLSS 5.bat'                   = 'Setup DLSS 5.bat'
  'Play with DLSS 5.bat'               = 'Play with DLSS 5.bat'
  'dlss5/README-DLSS5.txt'             = 'README-DLSS5.txt'
  'dlss5/setup-dlss5.ps1'              = 'setup-dlss5.ps1'
  'dlss5/align-dlss5-panel.ps1'        = 'align-dlss5-panel.ps1'
  'dlss5/show-dlss5-settings.ps1'      = 'show-dlss5-settings.ps1'
  'dlss5/show-dlss5-settings.bat'      = 'show-dlss5-settings.bat'
  'dlss5/templates/ReShade.template.ini'      = 'templates/ReShade.template.ini'
  'dlss5/templates/ReShadePreset.ini'         = 'templates/ReShadePreset.ini'
  'dlss5/templates/dlss5-feed.template.cfg'   = 'templates/dlss5-feed.template.cfg'
  'dlss5/runtime/rexruntime.dll'       = $Runtime
}
if (Test-Path $out) { Remove-Item $out }
$z = [IO.Compression.ZipFile]::Open($out, 'Create')
try {
  foreach ($k in $map.Keys) {
    $f = $map[$k]; if (-not [IO.Path]::IsPathRooted($f)) { $f = Join-Path $src $f }
    if (-not (Test-Path $f)) { throw "missing: $f" }
    # Windows line endings for the player's .bat/.ps1/.txt/.ini files.
    if ($k -match '\.(bat|ps1|txt|ini|cfg)$') {
      $text = [IO.File]::ReadAllText($f) -replace "`r?`n", "`r`n"
      $e = $z.CreateEntry($k, 'Optimal'); $w = New-Object IO.StreamWriter($e.Open(), (New-Object Text.UTF8Encoding($false)))
      $w.Write($text); $w.Dispose()
    } else { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f, $k, 'Optimal') | Out-Null }
  }
} finally { $z.Dispose() }
$h = (Get-FileHash -Algorithm SHA256 $out).Hash.ToLower()
"$out`n$((Get-Item $out).Length) bytes  sha256 $h"
