$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
$llvm = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\Llvm\x64\bin"

if (-not (Test-Path -LiteralPath $vcvars)) {
    throw "Visual Studio vcvars64.bat not found: $vcvars"
}

$command = "`"$vcvars`" >nul && set `"PATH=$llvm;%PATH%`" && cd /d `"$projectRoot`" && cmake --build --preset win-amd64-release"
cmd /d /c $command
exit $LASTEXITCODE
