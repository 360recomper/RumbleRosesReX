@echo off
rem Release build of RumbleRosesReX against a Release SDK install.
rem Usage: build-game-release.bat <game source> <sdk install dir> <build dir> [codegen]
rem "codegen" runs rexglue codegen first (needs <game source>\assets\default.xex).
setlocal
call "%~dp0env.bat"
set "SRC=%~1"
set "SDK=%~2"
set "BLD=%~3"
rem Strip local folder names from __FILE__ strings baked into the exe.
set "MAP=-ffile-prefix-map=%SRC:\=/%=game -ffile-prefix-map=%SRC%=game -ffile-prefix-map=%SDK:\=/%=rexglue-sdk -ffile-prefix-map=%SDK%=rexglue-sdk -ffile-prefix-map=%BLD:\=/%=build -ffile-prefix-map=%BLD%=build"
if /i "%~4"=="codegen" (
  pushd "%SRC%" || exit /b 1
  "%SDK%\bin\rexglue.exe" codegen rumble_roses_xx_manifest.toml --ignore-stamp || exit /b 1
  popd
)
cmake -S "%SRC%" -B "%BLD%" -G Ninja -DCMAKE_BUILD_TYPE=Release ^
  -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ "-DCMAKE_CXX_FLAGS=%MAP%" ^
  "-DCMAKE_PREFIX_PATH=%SDK%" || exit /b 1
cmake --build "%BLD%" -j 16 || exit /b 1
echo GAME RELEASE OK: %BLD%\rumble_roses_xx.exe
