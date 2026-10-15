@echo off
rem Sets up the build environment (MSVC libs + Windows SDK, Clang, CMake, Ninja). Adjust the paths to your install.
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
set "PATH=C:\Program Files\LLVM\bin;C:\Program Files\CMake\bin;%PATH%"
rem Ninja must also be on PATH (winget install Ninja-build.Ninja).
