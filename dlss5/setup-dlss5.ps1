# Setup DLSS 5 (neural rendering) for the Rumble Roses XX Recomp. Opt-in; the normal game launch is not changed.
# Run it with "Setup DLSS 5.bat". It downloads the third-party parts from their official pages, checks every file
# against a fixed SHA-256, asks you for your own nvngx_dlssnr.dll and fills this dlss5 folder.
# Then start the game with "Play with DLSS 5.bat". Undo: delete the dlss5 folder and the two DLSS 5 .bat files.
#
#   -DlssNrDll <path>   your nvngx_dlssnr.dll (skips the file picker)
#   -LocalFiles <dir>   folder that already holds the downloads (same file names); nothing is downloaded then
#   -NoPause            do not wait for a key at the end
param([string]$DlssNrDll, [string]$LocalFiles, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$D    = $PSScriptRoot                 # <install>\dlss5
$Root = Split-Path $D -Parent         # <install> (RRXX Launcher.exe, game\, mods\, saves\)
$Game = Join-Path $Root 'game'
$Dl   = Join-Path $D 'downloads'
$Host64 = Join-Path $D 'host64'

function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Done($code) { if (-not $NoPause) { Say ''; Read-Host 'Press Enter to close' | Out-Null }; exit $code }
trap { Say "`nSETUP FAILED: $($_.Exception.Message)" Red; Say 'Nothing outside the dlss5 folder was changed.' Red; Done 1 }

# ---- Pinned downloads (the exact versions the 4K60 setup was tested with) ------------------------------------------
$Files = @(
  @{ Name = 'ReShade_Setup_6.8.0_Addon.exe'; Size = 4318424
     Url  = 'https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe'
     Sha  = 'afe4c8f13048306307983b8b3d41d5bf00a86820440b0e57dea10950e1176445' }
  @{ Name = 'DLSS5-Feeder-1.19.0.zip'; Size = 503554
     Url  = 'https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/download/v1.19.0/DLSS5-Feeder-1.19.0.zip'
     Sha  = '9cc19e28f8fb4e216ea61621fdf6443e207e338ec639979ab335503c75f48f36' }
  @{ Name = 'OptiScaler-NR-v0.8.92.zip'; Size = 130482960
     Url  = 'https://github.com/jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass/releases/download/v0.8.92/OptiScaler-NR-v0.8.92.zip'
     Sha  = '9605352af2378eeb5ef96aedf69c648b36152eea40a388cb5413ca6a408bf728' }
  @{ Name = 'nvngx_dlss.dll'; Size = 58956912
     Url  = 'https://github.com/NVIDIA/DLSS/raw/v310.9.16/lib/Windows_x86_64/rel/nvngx_dlss.dll'
     Sha  = 'd00b5b0ab29c3ca101b70c3a5ecb3a01b7ef33766a3383a28a18623083e6123e' }
  @{ Name = 'ReShade.fxh'
     Url  = 'https://raw.githubusercontent.com/crosire/reshade-shaders/fd0022170615ce0d8162d219bff07232fa6dd84f/Shaders/ReShade.fxh'
     Sha  = '6dabfbbaf968c3871905d2ea17f96572ff7b1cec01310b5d0e5252b66b30174f' }
  @{ Name = 'ReShadeUI.fxh'
     Url  = 'https://raw.githubusercontent.com/crosire/reshade-shaders/fd0022170615ce0d8162d219bff07232fa6dd84f/Shaders/ReShadeUI.fxh'
     Sha  = '78adf672df47460297eb9fe6dd238d2aafa24510b52b84feb1a745dff70eb901' }
  @{ Name = 'DrawText.fxh'
     Url  = 'https://raw.githubusercontent.com/crosire/reshade-shaders/fd0022170615ce0d8162d219bff07232fa6dd84f/Shaders/DrawText.fxh'
     Sha  = 'b79cc4dfb3e98bcf4c06193d00ea7631d74f467f73a4deeeee13e71336d3e680' }
  # GitHub builds this zip on request, so its own hash may change; the files taken from it are checked one by one.
  @{ Name = 'LumeniteFX-f80e98d.zip'
     Url  = 'https://codeload.github.com/umar-afzaal/LumeniteFX/zip/f80e98d716ad72e12af6f7baa727bcfeb4be5094'
     Sha  = $null }
)
$LumeniteFiles = @{
  'Shaders/lumenite_Kernel.fx'                       = '869821002c97ead3424740d747af1972f9869adeb75288166f5ffdfa56b6c8ce'
  'Shaders/include/lumenite_ColorManagement.fxh'     = '116d3c41cd48f7787a2ed87eefc65fb2d9083fc1ab609766a3fbcca3e62649a6'
  'Shaders/include/lumenite_Compute.fxh'             = '736e3f39fc0c48a405c4d10b0d158502009db940e2673ff778e6b919d3c1a55f'
  'Shaders/include/lumenite_Helpers.fxh'             = '8826d613944be27e14095982b20098fff6d45e91c7d4028473c2f5b784c80028'
  'Shaders/include/lumenite_Projections.fxh'         = '709ec414649b74e573ca0f12a5ef25998d332238f7cab14a3d91053c8d388cab'
  'Textures/lumenite_bluenoise256.png'               = '2ad9e5b0bd28ddff569dbd756a551fe8e681b67eb72a83bb027434e1be24122b'
}
# nvngx_dlssnr.dll builds we know. Any other file is accepted only if NVIDIA signed it, or after you confirm.
$KnownNr = @{
  'CDFA6C1EC784867182164C7836F324F087B5C9F374D0C9FE46F54999FEABF3ED' = '310.9.16 compatibility build (RTX 20/30/40/50) - the one this setup was tested with'
  'E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A' = '310.8 compatibility build (RTX 20/30/40/50)'
  'E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E' = '310.8 original NVIDIA build (RTX 50 only)'
}

function Sha256($p) { (Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash.ToLower() }

function Get-Verified($f) {
  $p = Join-Path $Dl $f.Name
  if ((Test-Path $p) -and (-not $f.Sha -or (Sha256 $p) -eq $f.Sha)) { Say "  ok (already here)  $($f.Name)"; return $p }
  if ($LocalFiles -and (Test-Path (Join-Path $LocalFiles $f.Name))) {
    Copy-Item -LiteralPath (Join-Path $LocalFiles $f.Name) $p -Force
  } else {
    Say "  downloading        $($f.Name)  ($($f.Url))"
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (Test-Path $curl) { & $curl -sSfL --retry 2 -o $p $f.Url; if ($LASTEXITCODE) { throw "download failed: $($f.Url)" } }
    else { Invoke-WebRequest -UseBasicParsing -Uri $f.Url -OutFile $p }
  }
  if ($f.Sha) {
    $h = Sha256 $p
    if ($h -ne $f.Sha) { Remove-Item -LiteralPath $p -Force; throw "$($f.Name): SHA-256 mismatch (got $h, want $($f.Sha)). File deleted." }
    Say "  ok (SHA-256 match) $($f.Name)"
  } else { Say "  ok (downloaded)    $($f.Name) - its files are checked one by one below" }
  return $p
}

# Extracts zip entries whose path ends with $suffix (forward slashes) to $dest. Returns the count.
function Expand-Entries($zipPath, $suffix, $dest, [switch]$Tree, [string]$TreeRoot) {
  $z = [IO.Compression.ZipFile]::OpenRead($zipPath); $n = 0
  try {
    foreach ($e in $z.Entries) {
      $name = $e.FullName -replace '\\', '/'
      if (-not $e.Name) { continue }
      if ($Tree) {
        $i = $name.IndexOf($TreeRoot); if ($i -lt 0 -or ($i -gt 0 -and $name[$i - 1] -ne '/')) { continue }
        $rel = $name.Substring($i + $TreeRoot.Length); $t = Join-Path $dest ($rel -replace '/', '\')
      } elseif ($name -eq $suffix -or $name.EndsWith('/' + $suffix)) { $t = $dest } else { continue }
      New-Item -ItemType Directory -Force (Split-Path $t) | Out-Null
      [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $t, $true); $n++
    }
  } finally { $z.Dispose() }
  return $n
}

# The ReShade setup exe carries its files as a zip appended to the program. Cut that zip out (find the end-of-
# central-directory record, then where the zip really starts) so .NET can read it.
function Get-AppendedZip($exe, $out) {
  $b = [IO.File]::ReadAllBytes($exe)
  for ($i = $b.Length - 22; $i -ge [Math]::Max(0, $b.Length - 65557); $i--) {
    if ($b[$i] -eq 0x50 -and $b[$i+1] -eq 0x4B -and $b[$i+2] -eq 5 -and $b[$i+3] -eq 6) { break } }
  if ($i -lt 0) { throw 'ReShade setup: no zip found inside' }
  $cdSize = [BitConverter]::ToUInt32($b, $i + 12); $cdOff = [BitConverter]::ToUInt32($b, $i + 16)
  $start = ($i - $cdSize) - $cdOff
  if ($start -lt 0) { throw 'ReShade setup: zip layout not understood' }
  # The zip's own offsets count from its first byte, so the cut-out part is a normal zip.
  $fs = [IO.File]::Create($out); try { $fs.Write($b, [int]$start, $b.Length - [int]$start) } finally { $fs.Dispose() }
}

function Set-IniKey($lines, $section, $key, $value) {
  $in = $false
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*\[(.+)\]') { $in = ($Matches[1] -eq $section); continue }
    if ($in -and $lines[$i] -match "^\s*$([regex]::Escape($key))\s*=") { $lines[$i] = "$key=$value"; return }
  }
  throw "OptiScaler.ini: [$section] $key not found"
}

# ---- 1. Checks ------------------------------------------------------------------------------------------------------
Say 'Rumble Roses XX Recomp - DLSS 5 setup' White
Say "Install folder: $Root"
if (-not (Test-Path (Join-Path $Game 'rumble_roses_xx.exe'))) { throw "game\rumble_roses_xx.exe not found next to the dlss5 folder. Unzip the DLSS 5 add-on into the folder that holds 'RRXX Launcher.exe' and 'game'." }
if (-not (Test-Path (Join-Path $Game 'assets\default.xex'))) { throw 'The game is not set up yet (game\assets\default.xex missing). Run RRXX Launcher.exe > Setup first.' }
if (Get-Process rumble_roses_xx -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
$gpus = (Get-CimInstance Win32_VideoController | ForEach-Object Name) -join ', '
Say "Graphics card(s): $gpus"
if ($gpus -notmatch 'NVIDIA.*RTX') { Say 'WARNING: DLSS 5 needs an NVIDIA GeForce RTX card (RTX 40/50 recommended). Setup continues, but it will not run without one.' Yellow }

# ---- 2. Your nvngx_dlssnr.dll ---------------------------------------------------------------------------------------
Say "`nStep 1/3: your nvngx_dlssnr.dll (NVIDIA's DLSS 5 model file; it is not included, see README-DLSS5.txt)" White
if (-not $DlssNrDll) {
  Add-Type -AssemblyName System.Windows.Forms
  $dlg = New-Object System.Windows.Forms.OpenFileDialog
  $dlg.Title = 'Pick your nvngx_dlssnr.dll'; $dlg.Filter = 'nvngx_dlssnr.dll|nvngx_dlssnr.dll|DLL files|*.dll'
  if ($dlg.ShowDialog() -ne 'OK') { throw 'No nvngx_dlssnr.dll picked.' }
  $DlssNrDll = $dlg.FileName
}
if (-not (Test-Path -LiteralPath $DlssNrDll)) { throw "not found: $DlssNrDll" }
$nrHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $DlssNrDll).Hash
$nrVer = (Get-Item -LiteralPath $DlssNrDll).VersionInfo
Say "  $DlssNrDll"
Say "  version $($nrVer.FileVersion) '$($nrVer.FileDescription)', SHA-256 $nrHash"
if ($nrVer.FileDescription -notmatch 'DLSS' -and $nrVer.ProductName -notmatch 'DLSS') { throw 'This file is not an NVIDIA DLSS runtime.' }
if ($KnownNr.ContainsKey($nrHash)) { Say "  known: $($KnownNr[$nrHash])" Green }
else {
  $sig = Get-AuthenticodeSignature -LiteralPath $DlssNrDll
  if ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.Subject -match 'NVIDIA') { Say '  signed by NVIDIA (original build; RTX 50 cards only).' Green }
  else {
    Say '  Unknown build and not signed by NVIDIA. Only use files from a source you trust.' Yellow
    if ((Read-Host '  Use it anyway? (y/n)') -ne 'y') { throw 'Stopped: nvngx_dlssnr.dll not accepted.' }
  }
}

# ---- 3. Downloads ---------------------------------------------------------------------------------------------------
Say "`nStep 2/3: downloads (about 195 MB, each file checked against a fixed SHA-256)" White
New-Item -ItemType Directory -Force $Dl | Out-Null
$P = @{}; foreach ($f in $Files) { $P[$f.Name] = Get-Verified $f }

# ---- 4. Fill the dlss5 folder ---------------------------------------------------------------------------------------
Say "`nStep 3/3: installing into $D" White
$tmp = Join-Path $Dl 'reshade-files.zip'
Get-AppendedZip $P['ReShade_Setup_6.8.0_Addon.exe'] $tmp
foreach ($n in 'ReShade64.dll', 'ReShade64.json') { if ((Expand-Entries $tmp $n (Join-Path $D $n)) -ne 1) { throw "ReShade setup: $n missing" } }
Remove-Item $tmp
New-Item -ItemType Directory -Force $Host64 | Out-Null
Copy-Item (Join-Path $D 'ReShade64.dll') (Join-Path $Host64 'dxgi.dll') -Force

$feed = $P['DLSS5-Feeder-1.19.0.zip']
$sh = Join-Path $D 'shaders\Shaders'; $tx = Join-Path $D 'shaders\Textures'
foreach ($x in @(@('dlss5-feed-helper.addon64', (Join-Path $D 'dlss5-feed-helper.addon64')),
                 @('host64/dlss5-feed-host64.exe', (Join-Path $Host64 'dlss5-feed-host64.exe')),
                 @('reshade-shaders/Shaders/DLSS5_Feed.fx', (Join-Path $sh 'DLSS5_Feed.fx')))) {
  if ((Expand-Entries $feed $x[0] $x[1]) -ne 1) { throw "DLSS5-Feeder zip: $($x[0]) missing" } }

$opti = $P['OptiScaler-NR-v0.8.92.zip']
if ((Expand-Entries $opti 'OptiScaler.dll' (Join-Path $Host64 'winmm.dll')) -ne 1) { throw 'OptiScaler zip: OptiScaler.dll missing' }
if ((Expand-Entries $opti 'OptiScaler.ini' (Join-Path $Host64 'OptiScaler.ini')) -ne 1) { throw 'OptiScaler zip: OptiScaler.ini missing' }
$od = Join-Path $Host64 'OptiScaler'; if (Test-Path $od) { Remove-Item -Recurse -Force $od }
if ((Expand-Entries $opti '' $od -Tree -TreeRoot 'OptiScaler/') -lt 1) { throw 'OptiScaler zip: OptiScaler folder missing' }
New-Item -ItemType Directory -Force (Join-Path $D 'licenses') | Out-Null
Expand-Entries $opti 'LICENSE' (Join-Path $D 'licenses\OptiScaler-GPL-3.0.txt') | Out-Null
# Tested OptiScaler settings (DLSS-UP-6): DLSS 5 model at half working size, one pass, standard style.
$ini = [Collections.Generic.List[string]](Get-Content (Join-Path $Host64 'OptiScaler.ini'))
foreach ($k in @(@('Upscalers','Dx12Upscaler','dlss'), @('Menu','Scale','1.0'), @('Log','LogToFile','true'), @('Log','LogLevel','2'),
                 @('DlssNr','Enabled','true'), @('DlssNr','RunBeforeSR','true'), @('DlssNr','Passes','1'),
                 @('DlssNr','TransferStrength','1.5'), @('DlssNr','WorkingScale','0.5'))) { Set-IniKey $ini $k[0] $k[1] $k[2] }
Set-Content -Encoding ascii (Join-Path $Host64 'OptiScaler.ini') $ini

Copy-Item $P['nvngx_dlss.dll'] (Join-Path $Host64 'nvngx_dlss.dll') -Force
Copy-Item -LiteralPath $DlssNrDll (Join-Path $Host64 'nvngx_dlssnr.dll') -Force

foreach ($n in 'ReShade.fxh', 'ReShadeUI.fxh', 'DrawText.fxh') { Copy-Item $P[$n] (Join-Path $sh $n) -Force }
foreach ($k in $LumeniteFiles.Keys) {
  $dest = if ($k -like 'Textures/*') { Join-Path $tx (Split-Path $k -Leaf) } else { Join-Path $sh ($k.Substring(8) -replace '/', '\') }
  if ((Expand-Entries $P['LumeniteFX-f80e98d.zip'] $k $dest) -ne 1) { throw "LumeniteFX zip: $k missing" }
  if ((Sha256 $dest) -ne $LumeniteFiles[$k]) { Remove-Item $dest; throw "LumeniteFX $k`: SHA-256 mismatch" }
}

# ReShade + feed settings (ReShade.ini is rebuilt from the template at every launch; paths are absolute).
$t = Join-Path $D 'templates'
(Get-Content (Join-Path $t 'ReShade.template.ini')) -replace '@DLSS5@', $D | Set-Content -Encoding ascii (Join-Path $D 'ReShade.template.ini')
Copy-Item (Join-Path $t 'ReShadePreset.ini') (Join-Path $D 'ReShadePreset.ini') -Force
Copy-Item (Join-Path $t 'dlss5-feed.template.cfg') (Join-Path $D 'dlss5-feed.template.cfg') -Force

# Own settings file for DLSS 5 runs: your game settings + resolution_scale 2 (the game renders 2560x1440 and
# DLSS 5 outputs 4K; this is what holds 60 fps). Kept if it exists, so F4 changes made in DLSS 5 runs survive.
$toml = Join-Path $D 'RumbleRosesReX.toml'
if (-not (Test-Path $toml)) {
  $src = Join-Path $Game 'RumbleRosesReX.toml'
  $lines = if (Test-Path $src) { @(Get-Content $src) } else { @() }
  $lines = @($lines | Where-Object { $_ -notmatch '^\s*(resolution_scale|gpu_backend|game_data_root)\s*=' })
  $lines = @('# DLSS 5 launch settings (Play with DLSS 5.bat). Normal launch settings: game\RumbleRosesReX.toml') + $lines +
           @('gpu_backend = "vulkan"', 'resolution_scale = 2')
  Set-Content -Encoding ascii $toml $lines
}

Say "`nDLSS 5 is set up." Green
Say 'Start the game with "Play with DLSS 5.bat" (the normal launch stays as it was).'
Say 'In game: F9 = DLSS 5 on/off, F10 = DLSS 5 settings panel, Home = ReShade menu.'
Say 'NVIDIA Smooth Motion must be OFF for this game (NVIDIA app > Graphics).'
Done 0
