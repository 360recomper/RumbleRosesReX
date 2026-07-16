param(
    [Parameter(Mandatory = $true)]
    [string]$IsoPath,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$iso = Resolve-Path $IsoPath
$output = if ($OutputPath) { $OutputPath } else { Join-Path $projectRoot "assets" }
$extractor = Join-Path $projectRoot "scripts\extract_xgd.py"

Push-Location $projectRoot
try {
    python $extractor $iso $output
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
