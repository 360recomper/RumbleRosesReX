# RumbleRosesReX

ReXGlue recompilation project for Rumble Roses XX.

This repo contains the project scaffolding, title-specific hooks, runtime-discovered
function seeds, and helper scripts. It intentionally does not contain game assets,
disc images, extracted files, dumps, logs, build products, or generated recomp output.

## Local SDK

Use a local ReXGlue SDK checkout next to this project:

```powershell
..\rexglue-sdk
```

This title was tested with the SDK built with Vulkan enabled and the local SDK patch
under `patches\rexglue-sdk` applied:

```powershell
Set-Location ..\rexglue-sdk
git apply ..\rumble-roses-xx-recomp\patches\rexglue-sdk\0001-rumblerosesrex-runtime-support.patch
cmake --preset win-amd64 -DREXGLUE_USE_VULKAN=ON
cmake --build --preset win-amd64-relwithdebinfo --target install
```

## Game Files

Place a legally obtained local dump under `assets\`. The manifest expects:

```text
assets\default.xex
```

If you have a local ISO, the helper can extract it:

```powershell
.\scripts\extract-iso.ps1 -IsoPath "E:\path\to\Rumble Roses XX.iso"
```

Do not commit game binaries, disc images, extracted assets, generated recomp output,
or playtest logs.

## Build

From this folder:

```powershell
.\scripts\codegen.ps1
.\scripts\configure-relwithdebinfo.ps1
.\scripts\build-relwithdebinfo.ps1
```

`codegen.ps1` requires `assets\default.xex`. Until codegen runs, the generated
entrypoint include under `generated\default\` will not exist, so a normal CMake
build is expected to fail.

## Playtest

The current baseline uses Vulkan:

```powershell
.\scripts\playtest-vulkan.ps1
```

Or manually:

```powershell
Set-Location "out\build\win-amd64-relwithdebinfo"
.\rumble_roses_xx.exe --gpu_backend=vulkan --log_file="..\..\playtest-vulkan.log" --log_level=debug
```

## Current State

The project reached a first playable baseline on Vulkan. The manifest includes
runtime-discovered indirect call seeds, and `src\title_hooks.cpp` contains a small
guard for a title cleanup callback that can receive stale guest pointers.
