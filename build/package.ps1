# Builds release\dist\RRXX-Recomp-<date>\ + .zip from a Release game build and the launcher publish.
# Usage: package.ps1 -GameBuild <dir with rumble_roses_xx.exe> [-Date yyyy-MM-dd]
# Ships no game assets, ISO data or mod content.
param(
  [Parameter(Mandatory = $true)][string]$GameBuild,
  [string]$Date = (Get-Date -Format 'yyyy-MM-dd')
)
$ErrorActionPreference = 'Stop'
$rel = Split-Path -Parent $PSScriptRoot
$name = "RRXX-Recomp-$Date"
$out = Join-Path $rel "dist\$name"
$zip = Join-Path $rel "dist\$name.zip"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force "$out\game", "$out\mods", "$out\saves" | Out-Null

# Launcher (self-contained single exe from `dotnet publish -c Release -o publish`)
Copy-Item (Join-Path $rel 'launcher\publish\RRXX Launcher.exe') $out

# Game exe + its runtime DLLs. Refuse dev-build leftovers.
Copy-Item (Join-Path $GameBuild 'rumble_roses_xx.exe') "$out\game"
Get-ChildItem $GameBuild -Filter *.dll | ForEach-Object { Copy-Item $_.FullName "$out\game" }
# App-local VC++ runtime (redistributable), so players don't need the VC++ installer.
$crt = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Redist\MSVC\*\x64\Microsoft.VC143.CRT' |
  Sort-Object Name -Descending | Select-Object -First 1
foreach ($d in 'msvcp140.dll', 'msvcp140_atomic_wait.dll', 'vcruntime140.dll', 'vcruntime140_1.dll') {
  Copy-Item (Join-Path $crt.FullName $d) "$out\game"
}
$bad = Get-ChildItem "$out\game" | Where-Object { $_.Name -match 'rd\.dll$|Tracy' }
if ($bad) { throw "dev DLLs in package: $($bad.Name -join ', ')" }

# Default settings: no paths (the exe uses ..\saves and ..\mods when they exist), no gpu_backend (F4 owns it).
@'
# Rumble Roses XX Recomp settings.
# Change them in the launcher (Settings tab) or in game (F4).
resolution_scale = 2
'@ | Set-Content -Encoding ascii "$out\game\RumbleRosesReX.toml"

# Docs + licences
# MODS.md is the internal change log (names imported content): it stays out of the exe release.
foreach ($f in 'README.md', 'CREDITS.md') { Copy-Item (Join-Path $rel $f) $out }
Copy-Item (Join-Path $rel 'guide\index.html') "$out\Install-Guide.html"   # illustrated install guide
Copy-Item -Recurse (Join-Path $rel 'licenses') "$out\licenses"
Remove-Item "$out\licenses\Tracy-dev-only.txt"  # Tracy is not in Release builds

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
Get-ChildItem -Recurse $out | Where-Object { -not $_.PSIsContainer } |
  Select-Object @{n = 'File'; e = { $_.FullName.Substring($out.Length + 1) } }, Length | Format-Table -AutoSize
"ZIP: $zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
