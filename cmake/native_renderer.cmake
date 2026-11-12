# Native renderer for Rumble Roses XX (RR-NR-4..6; plan research notes in the research repo).
#
# Ported from UFC-native (cmake/native_renderer.cmake), which took it from the SvR 2010 recomp by
# nefariousjosiah (GPL-3.0), adapted from re:Blue (BSD-3-Clause); see native/LICENSE*, native/THIRD-PARTY.md.
#
# Run-time switch: the exe always has the rrxx_renderer cvar (default "native" since RR-NR-12, src/rumble_roses_xx_flags.cpp;
# no shader_cache.bin beside the exe = emulated).
# This file only compiles the native pieces in. Two options, both OFF by default:
#   RRXX_NATIVE_RENDERER  compile in the native scaffolding (RRXX_NATIVE_RENDERER=1, native/ include dir).
#                         Without hooks this is the RR-NR-5 census build: --rrxx_d3d_census counts the
#                         game's D3D calls (native/src/dev/rr_d3d_census.cpp) and rrxx_renderer=native
#                         prints "not implemented" and falls back to the emulated renderer.
#   RRXX_NATIVE_HOOKS     the D3D hooks + plume D3D12 device (RR-NR-6). Every hook is gated at run time
#                         (native/src/native/ufc_hook_gate.h): on the emulated renderer it runs the game's body.
# The options stay in the CMake cache (UFC trap 2): build-native.bat passes them on every configure.
#
# The renderer's sources live in the research repo's native/ folder (RRXX_NATIVE_ROOT), not in this
# tree; this tree's changes are kept as my-fixes/v010-game-native.diff + v010-game-rrnr12.diff.
# Third-party code (RR-NR-12): native/third_party/ = reblue_thirdparty (zstd, miniz), fidelityfx, licenses
# (committed) + plume and XenosRecomp (headers, xxHash, smol-v, DXC) cloned at their pins and patched by
# native/tools/rr_fetch_third_party.py. The shader cache is built from the game files by
# rrxx_shader_cache.exe (native/tools/build-shader-tools.bat; the launcher on the player's machine) and read
# at run time (rrxx_shader_cache cvar). pipelines_seed.bin (native/data, RR-NR-10) is copied beside the exe.

set(RRXX_NATIVE_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../native" CACHE PATH
    "Native renderer sources (research repo native/ folder)")
set(RRXX_THIRD_PARTY "${RRXX_NATIVE_ROOT}/third_party" CACHE PATH
    "plume, reblue_thirdparty, fidelityfx (native/tools/rr_fetch_third_party.py)")
set(RRXX_XENOS_DIR "${RRXX_THIRD_PARTY}/XenosRecomp" CACHE PATH
    "Patched XenosRecomp source tree (headers, xxHash, smol-v, dxc-bin)")
option(RRXX_NATIVE_HOOKS "Compile the native D3D hooks + plume device (RR-NR-6)" OFF)

if(RRXX_NATIVE_HOOKS)
    enable_language(C)  # zstd and xxHash are C
    if(NOT EXISTS "${RRXX_THIRD_PARTY}/plume/CMakeLists.txt" OR
       NOT EXISTS "${RRXX_XENOS_DIR}/thirdparty/dxc-bin/bin/x64/dxc.exe")
        message(FATAL_ERROR "RRXX_NATIVE_HOOKS: ${RRXX_THIRD_PARTY}/plume or ${RRXX_XENOS_DIR} dxc missing: "
            "run python -I native/tools/rr_fetch_third_party.py")
    endif()
    set(RRXX_NATIVE_GEN "${CMAKE_BINARY_DIR}/native_gen")

    # D3D12 only (as UFC).
    set(PLUME_D3D12_ENABLED ON CACHE BOOL "" FORCE)
    add_subdirectory("${RRXX_THIRD_PARTY}/plume" "${CMAKE_BINARY_DIR}/plume" EXCLUDE_FROM_ALL)

    add_library(rrxx_native_thirdparty STATIC
        "${RRXX_THIRD_PARTY}/reblue_thirdparty/zstd/zstddeclib.c"
        "${RRXX_XENOS_DIR}/thirdparty/xxHash/xxhash.c"
        "${RRXX_XENOS_DIR}/thirdparty/smol-v/source/smolv.cpp"
        "${RRXX_THIRD_PARTY}/reblue_thirdparty/miniz/miniz.cpp")
    target_include_directories(rrxx_native_thirdparty PUBLIC
        "${RRXX_THIRD_PARTY}/reblue_thirdparty/zstd"
        "${RRXX_XENOS_DIR}/thirdparty/xxHash"
        "${RRXX_XENOS_DIR}/thirdparty/smol-v/source"
        "${RRXX_THIRD_PARTY}/reblue_thirdparty/miniz")

    # Host shaders (copy, ImGui overlay, MSAA resolve, gamma, FSR 1) compiled with DXC to DXIL headers.
    set(RRXX_DXC_DIR "${RRXX_XENOS_DIR}/thirdparty/dxc-bin/bin/x64")
    set(RRXX_DXC "${RRXX_DXC_DIR}/dxc.exe")
    set(RRXX_HLSL_DIR "${RRXX_NATIVE_ROOT}/src/reblue/gpu/shaders/hlsl")
    set(RRXX_HOST_SHADER_HEADERS "")
    function(rrxx_host_shader STEM PROFILE)
        file(GLOB hlsl_includes "${RRXX_HLSL_DIR}/*.hlsli" "${RRXX_THIRD_PARTY}/fidelityfx/gpu/*.h"
            "${RRXX_THIRD_PARTY}/fidelityfx/gpu/fsr1/*.h")
        set(out "${RRXX_NATIVE_GEN}/src/gpu/shaders/hlsl/${STEM}.hlsl.dxil.h")
        add_custom_command(
            OUTPUT "${out}"
            COMMAND "${RRXX_DXC}" -T ${PROFILE} -HV 2021 -all-resources-bound -Wno-ignored-attributes
                    -I "${RRXX_THIRD_PARTY}/.." -Fh "${out}" "${RRXX_HLSL_DIR}/${STEM}.hlsl"
                    -Vn g_${STEM}_dxil
            DEPENDS "${RRXX_HLSL_DIR}/${STEM}.hlsl" ${hlsl_includes}
            COMMENT "Compiling ${STEM}.hlsl (${PROFILE}, dxil)"
            VERBATIM)
        set(RRXX_HOST_SHADER_HEADERS ${RRXX_HOST_SHADER_HEADERS} "${out}" PARENT_SCOPE)
    endfunction()
    foreach(shader IN ITEMS copy_vs imgui_vs)
        rrxx_host_shader(${shader} vs_6_0)
    endforeach()
    foreach(shader IN ITEMS copy_color_ps copy_depth_ps gamma_correction_ps fsr_easu_ps fsr_rcas_ps pfx_occlusion_count_ps
            imgui_ps resolve_msaa_color_2x resolve_msaa_color_4x resolve_msaa_color_8x
            resolve_msaa_depth_2x resolve_msaa_depth_4x resolve_msaa_depth_8x)
        rrxx_host_shader(${shader} ps_6_0)
    endforeach()
    add_custom_target(rrxx_host_shaders DEPENDS ${RRXX_HOST_SHADER_HEADERS})

    # DXC's import library for the run-time spec-constant linker (dxc_link.cpp).
    add_library(rrxx_dxcompiler SHARED IMPORTED)
    set_target_properties(rrxx_dxcompiler PROPERTIES
        IMPORTED_LOCATION "${RRXX_DXC_DIR}/dxcompiler.dll"
        IMPORTED_IMPLIB "${RRXX_XENOS_DIR}/thirdparty/dxc-bin/lib/x64/dxcompiler.lib"
        INTERFACE_INCLUDE_DIRECTORIES "${RRXX_XENOS_DIR}/thirdparty/dxc-bin/inc")

    # rrxx_prelink (UFC's ufc3_prelink): DXC-links every spec-constant variant of a shader_cache.bin
    # into shader_cache_linked.bin, so the game never links one while drawing.
    add_executable(rrxx_prelink
        "${RRXX_NATIVE_ROOT}/tools/native/prelink_shader_cache.cpp"
        "${RRXX_NATIVE_ROOT}/src/reblue/gpu/shaders/dxc_link.cpp"
        "${RRXX_NATIVE_ROOT}/src/reblue/gpu/shaders/shader_cache_file.cpp")
    target_include_directories(rrxx_prelink PRIVATE "${RRXX_NATIVE_ROOT}/src/reblue")
    target_link_libraries(rrxx_prelink PRIVATE rrxx_native_thirdparty rrxx_dxcompiler)

    file(GLOB_RECURSE RRXX_NATIVE_SOURCES CONFIGURE_DEPENDS
        "${RRXX_NATIVE_ROOT}/src/reblue/*.cpp" "${RRXX_NATIVE_ROOT}/src/reblue/*.h"
        "${RRXX_NATIVE_ROOT}/src/native/*.cpp" "${RRXX_NATIVE_ROOT}/src/native/*.h")
    list(APPEND RRXX_NATIVE_SOURCES "${RRXX_NATIVE_ROOT}/src/gpu_preference.cpp")
endif()

function(rrxx_add_native_renderer target)
    if(NOT EXISTS "${RRXX_NATIVE_ROOT}/src/rr_d3d.h")
        message(FATAL_ERROR "RRXX_NATIVE_RENDERER: ${RRXX_NATIVE_ROOT}/src/rr_d3d.h missing (set RRXX_NATIVE_ROOT)")
    endif()
    target_compile_definitions(${target} PRIVATE RRXX_NATIVE_RENDERER=1)
    target_include_directories(${target} PRIVATE "${RRXX_NATIVE_ROOT}/src")
    # Compile check of the RR D3D layout headers with the game's flags.
    target_sources(${target} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src/rrxx_native.cpp")
    if(NOT RRXX_NATIVE_HOOKS)
        # RR-NR-5 D3D call census (--rrxx_d3d_census, observe only). It overrides the same D3D
        # functions as the native hooks, so it is built only without them (as UFC's NR-6 census).
        target_sources(${target} PRIVATE "${RRXX_NATIVE_ROOT}/src/dev/rr_d3d_census.cpp")
        message(STATUS "RRXX native renderer: scaffolding + D3D census on (hooks off), sources ${RRXX_NATIVE_ROOT}")
        return()
    endif()
    target_include_directories(${target} PRIVATE
        "${RRXX_NATIVE_ROOT}/src/reblue"
        "${RRXX_NATIVE_ROOT}/src/native"
        "${RRXX_NATIVE_GEN}"
        "${RRXX_THIRD_PARTY}/plume"
        "${RRXX_XENOS_DIR}/XenosRecomp")
    target_link_libraries(${target} PRIVATE plume rrxx_native_thirdparty)
    add_dependencies(${target} rrxx_host_shaders rrxx_prelink)
    target_sources(${target} PRIVATE ${RRXX_NATIVE_SOURCES} ${RRXX_HOST_SHADER_HEADERS})
    # SVR_NATIVE_RENDERER selects SvR's observe-the-game's-D3D design in the re:Blue sources;
    # RRXX_NATIVE_RENDERER picks RR's device / resource layouts inside it (gpu/d3d.h).
    target_compile_definitions(${target} PRIVATE REBLUE_D3D12=1 SVR_NATIVE_RENDERER=1 RRXX_NATIVE_HOOKS=1)
    target_link_libraries(${target} PRIVATE d3d12 dxgi dwmapi rrxx_dxcompiler rex::o1heap)
    # RR's addresses for the D3D functions re:Blue's hooks name, then the run-time gate.
    set(RRXX_HOOKED_SOURCES ${RRXX_NATIVE_SOURCES})
    list(REMOVE_ITEM RRXX_HOOKED_SOURCES
        "${RRXX_NATIVE_ROOT}/src/reblue/gpu/shaders/dxc_link.cpp"
        "${RRXX_NATIVE_ROOT}/src/reblue/gpu/shaders/shader_cache_file.cpp")
    set_source_files_properties(${RRXX_HOOKED_SOURCES} PROPERTIES
        COMPILE_OPTIONS "-include;${RRXX_NATIVE_ROOT}/src/rr_d3d_names.h;-include;${RRXX_NATIVE_ROOT}/src/native/ufc_hook_gate.h")
    target_link_options(${target} PRIVATE "-Wl,/MAP:${CMAKE_BINARY_DIR}/rumble_roses_xx.map")
    add_custom_command(TARGET ${target} POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${RRXX_DXC_DIR}/dxcompiler.dll" "${RRXX_DXC_DIR}/dxil.dll" "$<TARGET_FILE_DIR:${target}>"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:rrxx_prelink>" "$<TARGET_FILE_DIR:${target}>"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${RRXX_NATIVE_ROOT}/data/pipelines_seed.bin" "$<TARGET_FILE_DIR:${target}>"
        VERBATIM)
    message(STATUS "RRXX native renderer: hooks + plume D3D12 device on, sources ${RRXX_NATIVE_ROOT}")
endfunction()
