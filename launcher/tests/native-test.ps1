# RR-NR-11: launcher <-> native renderer, headless, with fake exes. Never starts the real game or touches a play folder.
#   release\launcher\tests\native-test.ps1 [-Launcher <published RRXX Launcher.exe>] [-Work <scratch folder>]
# Ported from UFC-native launcher/tests/shader-cache-test.ps1. The fake rumble_roses_xx.exe logs its arguments and exits
# with what fake-game.txt says (0 when started with --rrxx_renderer=emulated); rrxx_shader_cache.exe is the same fake
# exe, doing what fake-tool.txt says. Covered: --play (= the Play button and the skip-launcher start, minus message boxes),
# --shader-cache, stale-cache stamps (tools, DXC DLLs, game files, no stamp, old stamp), every tool failure code, exit 3.
# Not covered: anything on screen (Settings dropdown, GPU list, the build dialog, message boxes), the ISO import step.
param(
  [string]$Launcher,
  [string]$Work = (Join-Path $env:TEMP 'rrxx-native-launcher-test')
)
$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
if (-not $Launcher) {
  $pub = Join-Path $Work '_publish'
  Write-Host "== dotnet publish"
  dotnet publish "$proj\RRXXLauncher.csproj" -c Release -o $pub -v q -nologo | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "publish failed" }
  $Launcher = Join-Path $pub 'RRXX Launcher.exe'
}

# Non-ASCII on purpose: paths go to the tool through the wide command line.
$play = Join-Path $Work "play $([char]0xE9)"
if (Test-Path $play) { Remove-Item -Recurse -Force $play }   # plain folder made by this script (no junctions)
$game = "$play\game"
New-Item -ItemType Directory -Force $play, $game, "$game\assets\pac" | Out-Null
Copy-Item $Launcher $play
$exe = Join-Path $play 'RRXX Launcher.exe'
Set-Content "$game\assets\default.xex" 'fake xex'
Set-Content "$game\assets\pac\_startup_usa.pac" 'fake pac'

$src = @'
using System; using System.IO; using System.Linq; using System.Text;
static class Fake {
  static int Main(string[] a) {
    string dir = AppDomain.CurrentDomain.BaseDirectory, me = Path.GetFileName(Environment.GetCommandLineArgs()[0]);
    if (me.StartsWith("rrxx_shader_cache", StringComparison.OrdinalIgnoreCase)) {
      File.AppendAllText(Path.Combine(dir, "tool-log.txt"), string.Join("|", a) + "\r\n", Encoding.UTF8);
      string[] plan = File.ReadAllText(Path.Combine(dir, "fake-tool.txt")).Trim().Split(new[]{' '}, 2);
      int code = int.Parse(plan[0]);
      if (plan.Length > 1 && plan[1] == "nofiles") return 0;
      if (code != 0) { Console.Error.WriteLine("rrxx_shader_cache: error: fake failure " + code); return code; }
      Console.WriteLine("317 containers (fake)");
      Console.WriteLine("shader cache ready");
      File.WriteAllText(Path.Combine(a[1], "shader_cache.bin"), "x");
      File.WriteAllText(Path.Combine(a[1], "shader_cache_linked.bin"), "x");
      return 0;
    }
    File.AppendAllText(Path.Combine(dir, "game-log.txt"), "[" + string.Join(" ", a) + "]\r\n");
    return a.Contains("--rrxx_renderer=emulated") ? 0 : int.Parse(File.ReadAllText(Path.Combine(dir, "fake-game.txt")).Trim());
  }
}
'@
$fake = Join-Path $game 'rumble_roses_xx.exe'
Add-Type -TypeDefinition $src -OutputAssembly $fake -OutputType ConsoleApplication -ReferencedAssemblies System.Core
Copy-Item $fake "$game\rrxx_shader_cache.exe"
foreach ($n in 'XenosRecomp.exe', 'rrxx_prelink.exe', 'dxcompiler.dll', 'dxil.dll') { Set-Content "$game\$n" "fake $n" }
# The launcher's check for native code (NativeRenderer.Marker); the real exe logs "rrxx native: renderer on (experimental, ...".
$marker = [Text.Encoding]::ASCII.GetBytes('rrxx native: renderer on (plume D3D12)')
$f = [IO.File]::Open($fake, 'Append'); $f.Write($marker, 0, $marker.Length); $f.Close()

$toml = "$game\RumbleRosesReX.toml"
$fail = 0
function Check($ok, $what) { if ($ok) { Write-Host "  ok   $what" } else { Write-Host "  FAIL $what"; $script:fail++ } }
function Reset($tool, $gameCode = '0', $renderer = 'native') {
  foreach ($n in 'shader_cache.bin', 'shader_cache_linked.bin', 'shader_cache.stamp', 'tool-log.txt', 'game-log.txt') {
    if (Test-Path "$game\$n") { Remove-Item -Force "$game\$n" }
  }
  if (Test-Path "$play\launcher-native.log") { Remove-Item -Force "$play\launcher-native.log" }
  if ($renderer) { Set-Content $toml "resolution_scale = 2`r`nrrxx_renderer = `"$renderer`"" } else { Set-Content $toml "resolution_scale = 2" }
  Set-Content "$game\fake-tool.txt" $tool
  Set-Content "$game\fake-game.txt" $gameCode
}
function Run([string[]]$a) { (Start-Process $exe -ArgumentList $a -Wait -PassThru).ExitCode }
function GameLog() { if (Test-Path "$game\game-log.txt") { (Get-Content "$game\game-log.txt") -join ' ' } else { '' } }
function ToolRuns() { if (Test-Path "$game\tool-log.txt") { @(Get-Content "$game\tool-log.txt").Count } else { 0 } }
function Renderer() { ((Get-Content $toml) -match '^rrxx_renderer') -join '' }
function Log() { if (Test-Path "$play\launcher-native.log") { Get-Content "$play\launcher-native.log" -Raw -Encoding UTF8 } else { '' } }
function Clear-Run() { foreach ($n in 'tool-log.txt', 'game-log.txt') { if (Test-Path "$game\$n") { Remove-Item "$game\$n" } } }
function Touch-Bytes($file) { $b = [IO.File]::ReadAllBytes($file); [IO.File]::WriteAllBytes($file, $b + [byte[]](0)) }

Write-Host "== No rrxx_renderer key (existing install): Native is the default"
Reset '0' '0' $null
Check ((Run '--play') -eq 0) "launcher exit 0"
Check ((ToolRuns) -eq 1 -and (Test-Path "$game\shader_cache.stamp")) "cache built before the first native start"
Check ((Renderer) -match '"native"') "toml now says native (the game's own default may be emulated)"
Check ((GameLog) -eq '[]') "game started with no arguments: $(GameLog)"

Write-Host "== Emulated picked: plain start, no cache"
Reset '0' '0' 'emulated'
Check ((Run '--play') -eq 0) "launcher exit 0"
Check ((GameLog) -eq '[]') "game started with no arguments: $(GameLog)"
Check ((ToolRuns) -eq 0) "tool not run"
Check (-not (Test-Path "$game\shader_cache.bin")) "no cache built"
Check ((Renderer) -match '"emulated"') "toml still emulated"

Write-Host "== Native, no cache, tool works"
Reset '0'
[void](Run '--play')
Check (Test-Path "$game\shader_cache_linked.bin") "cache built"
$args0 = (Get-Content "$game\tool-log.txt" -Encoding UTF8) -join ''
Check ($args0 -eq "$game\assets|$game") "tool got <game\assets>|<game>, non-ASCII intact: $args0"
Check ((GameLog) -eq '[]') "game started with no renderer override"
Check ((Renderer) -match '"native"') "toml still native"
Check (Test-Path "$game\shader_cache.stamp") "stamp written"
$stamp = Get-Content "$game\shader_cache.stamp" -Raw
Check ($stamp -match '(?m)^rrxx shader cache stamp 1$' -and $stamp -match 'tool XenosRecomp.exe [0-9A-F]{64}' -and $stamp -match 'source pac/_startup_usa.pac [0-9A-F]{64}') "stamp lists tools + sources"
Check ((Log) -match 'shader cache ready') "tool output logged"

Write-Host "== Native, cache current: tool not run"
Clear-Run
[void](Run '--play')
Check ((ToolRuns) -eq 0) "tool not run"
Check ((GameLog) -eq '[]') "game started"

Write-Host "== Stale stamp cases: each one rebuilds once"
$cases = @(
  @{ what = 'rrxx_shader_cache.exe updated'; file = "$game\rrxx_shader_cache.exe"; reason = 'tool rrxx_shader_cache.exe' },
  @{ what = 'XenosRecomp.exe updated'; file = "$game\XenosRecomp.exe"; reason = 'tool XenosRecomp.exe' },
  @{ what = 'rrxx_prelink.exe updated'; file = "$game\rrxx_prelink.exe"; reason = 'tool rrxx_prelink.exe' },
  @{ what = 'dxcompiler.dll updated'; file = "$game\dxcompiler.dll"; reason = 'tool dxcompiler.dll' },
  @{ what = 'default.xex changed'; file = "$game\assets\default.xex"; reason = 'source default.xex' },
  @{ what = '_startup_usa.pac changed (repair / mod)'; file = "$game\assets\pac\_startup_usa.pac"; reason = 'source pac/_startup_usa.pac' }
)
foreach ($c in $cases) {
  Clear-Run
  if (Test-Path "$play\launcher-native.log") { Remove-Item "$play\launcher-native.log" }
  Touch-Bytes $c.file
  [void](Run '--play')
  Check ((ToolRuns) -eq 1) "$($c.what): tool run"
  Check ((Log) -match [regex]::Escape($c.reason)) "$($c.what): reason logged ($($c.reason))"
  Check ((GameLog) -eq '[]') "$($c.what): game started native"
  Clear-Run
  [void](Run '--play')
  Check ((ToolRuns) -eq 0) "$($c.what): next run, tool not run"
}

Write-Host "== cache with no stamp: rebuilt"
Clear-Run
Remove-Item "$game\shader_cache.stamp"
[void](Run '--play')
Check ((ToolRuns) -eq 1) "tool run"
Check (Test-Path "$game\shader_cache.stamp") "stamp written"

Write-Host "== stamp from another launcher (UFC-style, tool hashes only): rebuilt"
Clear-Run
Set-Content "$game\shader_cache.stamp" "rrxx_shader_cache.exe 00`nXenosRecomp.exe 00`n" -NoNewline
[void](Run '--play')
Check ((ToolRuns) -eq 1) "tool run"
Check ((Log) -match 'older launcher|changed since') "reason logged"

Write-Host "== linked file deleted: rebuilt"
Clear-Run
Remove-Item "$game\shader_cache_linked.bin"
[void](Run '--play')
Check ((ToolRuns) -eq 1) "tool run"

Write-Host "== every tool failure exit code falls back to emulated"
foreach ($c in 1, 2, 3, 4, 5, 77) {
  Reset "$c"
  [void](Run '--play')
  Check ((GameLog) -eq '[--rrxx_renderer=emulated]') "exit $c`: game started on emulated, once: $(GameLog)"
  Check ((Renderer) -match '"emulated"') "exit $c`: toml back on emulated"
  Check ((Log) -match "FAILED" -and (Log) -match "fake failure $c") "exit $c`: reason logged"
  Check (-not (Test-Path "$game\shader_cache.stamp")) "exit $c`: no stamp"
}

Write-Host "== failed rebuild over an old cache: old files kept, stamp gone, emulated"
Reset '0'
[void](Run '--play')
Touch-Bytes "$game\XenosRecomp.exe"
Set-Content "$game\fake-tool.txt" '4'
Clear-Run
[void](Run '--play')
Check ((GameLog) -eq '[--rrxx_renderer=emulated]') "game started on emulated"
Check ((Test-Path "$game\shader_cache.bin") -and -not (Test-Path "$game\shader_cache.stamp")) "old cache left, stamp removed"

Write-Host "== tool missing"
Reset '0'
Rename-Item "$game\rrxx_shader_cache.exe" 'tool.off'
[void](Run '--play')
Rename-Item "$game\tool.off" 'rrxx_shader_cache.exe'
Check ((GameLog) -eq '[--rrxx_renderer=emulated]') "game started on emulated"
Check ((Log) -match 'is not in the game folder') "reason logged"

Write-Host "== exit 0 but no cache files"
Reset '0 nofiles'
[void](Run '--play')
Check ((GameLog) -eq '[--rrxx_renderer=emulated]') "game started on emulated"
Check ((Log) -match 'finished, but') "reason logged"

Write-Host "== exit 3 (native device failed) after a good build"
Reset '0' '3'
Check ((Run '--play') -eq 0) "launcher exit 0"
Check ((GameLog) -eq '[] [--rrxx_renderer=emulated]') "ran native, then emulated: $(GameLog)"
Check ((Renderer) -match '"native"') "toml untouched by that path (headless; the window asks Yes/No)"
Check ((Log) -match 'exit code 3') "logged"

Write-Host "== other native exit codes: no restart"
foreach ($c in 0, 1, -1073741819) {
  Reset '0' "$c"
  [void](Run '--play')
  Check ((GameLog) -eq '[]') "exit $c`: game ran once: $(GameLog)"
}

Write-Host "== --shader-cache (headless build)"
Reset '0' '0' 'emulated'
Check ((Run '--shader-cache') -eq 0) "exit 0"
Check ((ToolRuns) -eq 1 -and (Test-Path "$game\shader_cache.stamp")) "built + stamped, renderer choice not needed"
Check ((Renderer) -match '"emulated"') "toml still emulated"
Clear-Run
Check ((Run '--shader-cache') -eq 0) "current: exit 0"
Check ((ToolRuns) -eq 0) "current: tool not run"
Check ((Run '--shader-cache', '--force') -eq 0) "--force: exit 0"
Check ((ToolRuns) -eq 1) "--force: tool run"
Set-Content "$game\fake-tool.txt" '2'
Check ((Run '--shader-cache', '--force') -eq 1) "failed build: exit 1"
Check ((Renderer) -match '"emulated"') "failed build: toml still emulated"

Write-Host "== custom rrxx_shader_cache path in the toml"
Reset '0'
New-Item -ItemType Directory -Force "$game\cache" | Out-Null
Add-Content $toml 'rrxx_shader_cache = "cache\shader_cache.bin"'
[void](Run '--play')
Check (((Get-Content "$game\tool-log.txt" -Encoding UTF8) -join '') -eq "$game\assets|$game\cache") "tool writes into game\cache"
Check (Test-Path "$game\cache\shader_cache.stamp") "stamp beside it"
Check ((GameLog) -eq '[]') "game started native"
Remove-Item -Recurse -Force "$game\cache"

Write-Host "== emulated-only build (no marker), toml says native: plain start, no tool, toml not rewritten"
$bytes = [IO.File]::ReadAllBytes($fake)
[IO.File]::WriteAllBytes($fake, $bytes[0..($bytes.Length - $marker.Length - 1)])
Reset '4' '3' 'native'
[void](Run '--play')
Check ((GameLog) -eq '[]') "game started once, no override: $(GameLog)"
Check ((ToolRuns) -eq 0) "tool not run"
Check ((Run '--shader-cache') -eq 2) "--shader-cache: exit 2 (no native renderer)"

Write-Host ""
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host "all ok"; exit 0 }
