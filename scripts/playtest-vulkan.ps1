param(
    [string]$LogFile = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$buildDir = Join-Path $projectRoot "out\build\win-amd64-relwithdebinfo"
$exe = Join-Path $buildDir "rumble_roses_xx.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Executable not found. Build RelWithDebInfo first: $exe"
}

if (-not $LogFile) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $LogFile = Join-Path $projectRoot "out\playtest-vulkan-$stamp.log"
}

Push-Location $buildDir
try {
    & $exe --gpu_backend=vulkan "--log_file=$LogFile" --log_level=debug
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
