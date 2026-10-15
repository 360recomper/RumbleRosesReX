# Building from source

Windows 10/11 x64. You need your own `default.xex` from your own copy of the game.

Tools: Visual Studio 2022 Build Tools (C++ workload + Windows SDK), LLVM/Clang, CMake, Ninja, Git, .NET 8 SDK (launcher only).
Edit `build\env.bat` so it points at them.

## 1. SDK (ReXGlue v0.10.0 + our patch)

```bat
git clone https://github.com/rexglue/rexglue-sdk.git
cd rexglue-sdk
git checkout c94f5eb
git submodule update --init --recursive
git apply ..\RumbleRosesReX\sdk-patch\rexglue-sdk-v0.10-all-fixes.patch
cd ..\RumbleRosesReX
build\build-sdk-release.bat ..\rexglue-sdk ..\sdk-build ..\sdk-install
```

`c94f5eb` is the "Release v0.10.0" commit of the SDK. The patch adds the save/file fixes, the mods overlay, the F4 menu
(settings, pad control, saves buttons, backend pick, sharpening), pause on focus loss and the portable `..\saves` / `..\mods` paths.

Windows note: if your clone checks symlinks out as one-line text files, `thirdparty/libmspack/cabextract/mspack/*`
will fail to compile. Copy the real files over the stubs (they live one folder up under `mspack/`).

## 2. Game

Put `default.xex` (from your ISO) into `assets\`, then:

```bat
build\build-game-release.bat . ..\sdk-install ..\game-build codegen
```

Result: `..\game-build\rumble_roses_xx.exe`.

## 3. Launcher and package

```bat
cd launcher
dotnet publish -c Release -o publish
cd ..
powershell -File build\package.ps1 -GameBuild ..\game-build
```

This creates `dist\RRXX-Recomp-<date>.zip`, the same layout as the GitHub release.

## Files this fork changes relative to upstream

`CMakeLists.txt`, `rumble_roses_xx_manifest.toml`, `src/rumble_roses_xx_app.h`, `src/rumble_roses_xx_flags.cpp`,
`src/title_hooks.cpp`. New: `launcher/`, `sdk-patch/`, `build/`, `licenses/`, `CREDITS.md`, `BUILDING.md`.
The old `patches/rexglue-sdk/0001-...patch` is replaced by `sdk-patch/`.
