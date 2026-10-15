# Credits

- **YucaGuapo**: project lead, testing, fixes, modding research
- **wowjinxy**: original [RumbleRosesReX](https://github.com/wowjinxy/RumbleRosesReX) recompilation setup, which this project is based on
- **ReXGlue SDK** (Tom Clay and contributors): the static recompilation toolkit and runtime
- **Xenia** (Ben Vanik and the Xenia contributors): the emulator the ReXGlue runtime is derived from
- **xenia-canary game-patches contributors**: the original bloom and blur patches, ported here as settings
- **AMD FidelityFX SDK** (Advanced Micro Devices, MIT): the CAS sharpening shader behind F4 > Sharpening (licence: `licenses\AMD-FidelityFX-SDK-MIT.txt`)
- **mariokart64n**: research on the DLC costume format

## Third-party licences (`licenses\`)
ReXGlue SDK (+ Xenia notice) `ReXGlue-SDK-LICENSE.txt`; AMD FidelityFX (MIT); Dear ImGui (MIT); SDL3 (zlib); FFmpeg (LGPL v2.1+, see `FFmpeg-LICENSE.md`);
fmt, spdlog, tomlplusplus, CLI11, inja, utfcpp, xxHash, simde, o1heap, aes_128, Catch2 (tests only), glslang, SPIRV-Headers/Tools,
Vulkan-Headers/Loader, VulkanMemoryAllocator, MoltenVK (macOS only), Tracy (dev builds only, not shipped).
Not found as separate licence files in the SDK checkout (check upstream before release): libmspack (LGPL), crypto, disasm, disruptorplus, dxbc, dxc, pe, renderdoc, stb, tiny-aes-c.
