@echo off
rem Release-only ReXGlue SDK build for the public exe: no Tracy, no "rd" DLLs.
rem Usage: build-sdk-release.bat <sdk source> <build dir> <install dir> [fidelityfx source dir]
rem The FidelityFX dir is optional (reuses an existing checkout instead of a 700 MB clone).
setlocal
call "%~dp0env.bat"
set "SRC=%~1"
set "BLD=%~2"
set "INST=%~3"
set "FFX="
if not "%~4"=="" set "FFX=-DFETCHCONTENT_SOURCE_DIR_FIDELITYFX=%~4"
rem Strip local folder names from __FILE__ strings baked into the binaries.
set "MAP=-ffile-prefix-map=%SRC:\=/%=rexglue-sdk -ffile-prefix-map=%SRC%=rexglue-sdk -ffile-prefix-map=%BLD:\=/%=build -ffile-prefix-map=%BLD%=build"
cmake -S "%SRC%" -B "%BLD%" -G "Ninja Multi-Config" ^
  -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ ^
  "-DCMAKE_C_FLAGS=-march=x86-64-v2 %MAP%" "-DCMAKE_CXX_FLAGS=-march=x86-64-v2 %MAP%" -DCMAKE_CXX_STANDARD=23 ^
  -DCMAKE_CONFIGURATION_TYPES=Release -DCMAKE_DEFAULT_BUILD_TYPE=Release ^
  "-DCMAKE_INSTALL_PREFIX=%INST%" ^
  -DREXGLUE_USE_VULKAN=ON -DREXGLUE_ENABLE_TRACY=OFF ^
  -DREXGLUE_ENABLE_FIDELITYFX=ON -DREXGLUE_FIDELITYFX_CAS_ONLY=ON %FFX% || exit /b 1
cmake --build "%BLD%" --config Release --target install -j 16 || exit /b 1
echo SDK RELEASE OK: %INST%
