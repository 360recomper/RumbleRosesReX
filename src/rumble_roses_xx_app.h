// RumbleRosesReX - ReXGlue Recompiled Project
//
// Customize your app by overriding virtual hooks from rex::ReXApp.

#pragma once

#include <algorithm>
#include <cstdio>
#include <cstdlib>

#include <rex/audio/audio_driver.h>
#include <rex/cvar.h>
#include <rex/filesystem.h>
#include <rex/rex_app.h>
#include <rex/runtime.h>
#include <rex/system/gpu_plugin.h>

#include <rex/input/input_system.h>
#include <rex/logging.h>
#include <rex/ui/keybinds.h>
#include <rex/ui/ui_event.h>

#include <atomic>
#include <chrono>
#include <memory>
#include <thread>

#if defined(RRXX_NATIVE_HOOKS)
// Native renderer (RR-NR-6): plume D3D12 device, ImGui overlay drawer, crash traces (native/src).
#include <atomic>
#include <cstdlib>
#include "gpu/device.h"
#include "gpu/imgui_overlay_drawer.h"
#include "native/ufc_native.h"
void SvrInstallCrashTrace(const char* trace_dir);  // native/src/native/crash_trace.cpp
bool SvrCpuName(char* name, uint32_t size, uint32_t& logical);  // native/src/native/display_size.cpp
#endif

REXCVAR_DECLARE(std::string, gpu_backend);
REXCVAR_DECLARE(bool, pause_on_focus_loss);
// Native renderer switch (rumble_roses_xx_flags.cpp, RR-NR-4).
REXCVAR_DECLARE(std::string, rrxx_renderer);
REXCVAR_DECLARE(std::string, rrxx_test_config);
REXCVAR_DECLARE(bool, rrxx_native_test_device_fail);
REXCVAR_DECLARE(std::string, rrxx_selftest_keys);
#if defined(RRXX_NATIVE_HOOKS)
REXCVAR_DECLARE(std::string, rrxx_shader_cache);  // native/src/reblue/gpu/shaders/guest_shaders.cpp
#endif

// rrxx_renderer is read once in LatchRenderer and latched here; nothing switches renderers later.
// Every native hook (RR-NR-6) starts with `if (!g_rrxx_native) return;`, so the default (emulated)
// runs exactly as the v1.5c exe.
#ifndef RRXX_NATIVE_LATCH_DEFINED
#define RRXX_NATIVE_LATCH_DEFINED
inline bool g_rrxx_native = false;
#endif

// title_hooks.cpp: hashes default.xex; a different XEX (title update) turns the data-writing hooks off.
void RRXX_CheckXexImage(const std::filesystem::path& xex_host);

class RumbleRosesXxApp : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(
      rex::ui::WindowedAppContext& ctx) {
    return std::unique_ptr<RumbleRosesXxApp>(new RumbleRosesXxApp(ctx, "RumbleRosesReX",
        PPCImageConfig));
  }

  // Override virtual hooks for customization:
  // void OnPostInitLogging() override {}
  // Exit code when the native device can't be created: the launcher restarts the game with
  // --rrxx_renderer=emulated (nothing is written to the toml). Not 3: abort() and uncaught
  // exceptions exit with 3 too, and a plain crash must not switch the player to Emulated.
  static constexpr int kNativeDeviceFailedExitCode = 0x52520003;

  // rrxx_renderer is read once, here (from UFC-native ufc3_app.h LatchRenderer).
  static void LatchRenderer() {
    const std::string& renderer = REXCVAR_GET(rrxx_renderer);
    g_rrxx_native = false;
    if (renderer == "native") {
#if !defined(RRXX_NATIVE_HOOKS)
      if (REXCVAR_GET(rrxx_native_test_device_fail)) {
        // Test of the launcher's exit-3 restart; RR-NR-6 also takes this path when the plume
        // device can't be created.
        REXLOG_ERROR("rrxx native: host device creation failed; restart with --rrxx_renderer=emulated");
        std::fprintf(stderr, "rrxx native: host device creation failed (exit %d)\n",
                     kNativeDeviceFailedExitCode);
        rex::FlushLogging();
        std::_Exit(kNativeDeviceFailedExitCode);
      }
#endif
#if defined(RRXX_NATIVE_HOOKS)
      // RR-NR-12: native is the default, so a start without the launcher (no shader cache built
      // yet) must not come up black: no shader_cache.bin = the emulated renderer.
      std::filesystem::path cache = rex::to_path(REXCVAR_GET(rrxx_shader_cache));
      if (cache.is_relative())
        cache = rex::filesystem::GetExecutableFolder() / cache;
      std::error_code ec;
      if (!std::filesystem::is_regular_file(cache, ec)) {
        REXLOG_WARN("rrxx native: shader cache {} missing (the launcher builds it), using the "
                    "emulated renderer", cache.string());
        return;
      }
      g_rrxx_native = true;
      // Keep "native: renderer on (": the launcher and package.ps1 look for it in the exe.
      REXLOG_INFO("rrxx native: renderer on (plume D3D12)");
#else
      // Scaffolding / census build: no native hooks compiled yet (RR-NR-6).
      REXLOG_WARN("rrxx native: not implemented, using the emulated renderer");
      std::fprintf(stderr, "rrxx native: not implemented, using the emulated renderer\n");
#endif
    } else if (renderer != "emulated") {
      REXLOG_WARN("rrxx_renderer '{}' unknown, using the emulated renderer", renderer);
    }
  }

  // RR-NR-12: the native renderer's switches and debug cvars are not player settings: hidden from
  // the F4 overlay (SDK my-fixes/v010-sdk-rrnr12-overlay-debug-only.patch skips debug_only flags).
  // The launcher owns the renderer choice (it builds the shader cache first).
  static void HideNativeCvars() {
    for (auto& entry : rex::cvar::GetRegistry()) {
      if (entry.category == "Native" || entry.category == "SvR" || entry.category == "reblue" ||
          entry.category == "rrxx")
        entry.is_debug_only = true;
    }
  }

  void OnPreSetup(rex::RuntimeConfig& config) override {
    HideNativeCvars();
    const std::string& test_config = REXCVAR_GET(rrxx_test_config);
    if (!test_config.empty()) {
      rex::cvar::LoadConfig(rex::to_path(test_config));
      REXLOG_INFO("test config {} loaded, rrxx_renderer = {}", test_config,
                  REXCVAR_GET(rrxx_renderer));
    }
    LatchRenderer();
#if defined(RRXX_NATIVE_HOOKS)
    if (g_rrxx_native) {
      // Native renderer: the Xenos plugin's null backend (SDK patch 0036) keeps the game's D3D
      // command stream moving (fences, interrupts, vblank) without drawing; the hooks draw.
      if (!config.graphics) {
        config.graphics = rex::system::LoadGpuPlugin("xenos", "null");
        config.gpu_plugin = "xenos";
        REXLOG_INFO("rrxx native: guest GPU {}", config.graphics ? "null (sync only)" : "MISSING");
      }
      return;
    }
#endif
    // SDK v0.10 moved Xenos GPU emulation into the runtime-loaded rexgpu-xenos
    // plugin; pick the backend inside it from our gpu_backend cvar.
    if (!config.graphics) {
      config.graphics = rex::system::LoadGpuPlugin("xenos", REXCVAR_GET(gpu_backend));
      if (!config.graphics) {
        // Let ReXApp retry with any backend and show its fatal dialog on failure.
        config.gpu_plugin = "xenos";
      }
    }
  }
#if defined(RRXX_NATIVE_HOOKS)
  // Native (from UFC-native ufc3_app.h, which took it from SvR's svr2010_app.h): the plume device
  // draws the ImGui overlays at present time. Both return the SDK defaults on the emulated renderer.
  std::unique_ptr<rex::ui::ImmediateDrawer> OnCreateImmediateDrawer() override {
    if (!g_rrxx_native)
      return nullptr;
    auto drawer = std::make_unique<bd::gpu::ImGuiOverlayDrawer>();
    overlay_drawer_ = drawer.get();
    return drawer;
  }

  void OnPreLaunchModule() override {
    if (!g_rrxx_native)
      return;
    // Crash / hang tracer: traces go beside the game's log file (a farm run copies that folder back).
    {
      std::filesystem::path dir = rex::filesystem::GetExecutableFolder();
      const std::string log_file = rex::cvar::GetFlagByName("log_file");
      if (!log_file.empty()) {
        std::filesystem::path parent = std::filesystem::u8path(log_file).parent_path();
        if (!parent.empty()) dir = parent;
      }
      SvrInstallCrashTrace(rex::path_to_utf8(dir).c_str());
      REXLOG_INFO("rrxx native: crash and hang traces go to {}", rex::path_to_utf8(dir));
    }
    {
      char cpu[128] = {};
      uint32_t logical = 0;
      if (SvrCpuName(cpu, sizeof(cpu), logical))
        REXLOG_INFO("rrxx native: CPU {}, {} logical", cpu, logical);
    }
    if (REXCVAR_GET(rrxx_native_test_device_fail) || !bd::gpu::Video::CreateHostDevice(window())) {
      REXLOG_ERROR("rrxx native: host device creation failed; restart with --rrxx_renderer=emulated");
      std::fprintf(stderr, "rrxx native: host device creation failed (exit %d)\n",
                   kNativeDeviceFailedExitCode);
      rex::FlushLogging();
      std::_Exit(kNativeDeviceFailedExitCode);
    }
    // Present runs on the guest thread and ImGui on the UI thread: the UI thread records the
    // overlay in the background and Present replays the latest recording.
    bd::gpu::Video::SetOverlayDrawHook([this](plume::RenderCommandList* cmd,
                                              plume::RenderFramebuffer* fb, uint32_t w,
                                              uint32_t h) {
      auto* drawer = overlay_drawer_;
      if (!imgui_drawer() || !imgui_drawer()->HasDialogs()) {
        if (drawer)
          drawer->ClearCapture();
        return;
      }
      if (!drawer || drawer->NeedsDirectDraw() || !drawer->ReplayLatest(cmd, fb, w, h)) {
        app_context().CallInUIThreadSynchronous([this, cmd, fb, w, h] {
          bd::gpu::ReblueUIDrawContext ctx(w, h, cmd, fb);
          imgui_drawer()->Draw(ctx);
        });
      }
      if (drawer && !overlay_capture_pending_.exchange(true)) {
        app_context().CallInUIThread([this, w, h] {
          if (imgui_drawer() && overlay_drawer_)
            overlay_drawer_->Capture(
                w, h, [this](rex::ui::UIDrawContext& ctx) { imgui_drawer()->Draw(ctx); });
          overlay_capture_pending_.store(false);
        });
      }
    });
  }

  void OnWindowPixelSizeChanged(uint32_t w, uint32_t h) override {
    if (!g_rrxx_native) {
      rex::ReXApp::OnWindowPixelSizeChanged(w, h);
      return;
    }
    bd::gpu::Video::RequestResize();
  }
#endif
  // Alt-tab: mute audio and hide the pad from the game (no thread suspend).
  void OnWindowFocusChanged(bool focused) override {
    const bool block = !focused && REXCVAR_GET(pause_on_focus_loss);
    rex::audio::g_audio_muted.store(block);
    rex::input::g_input_blocked.store(block);
    REXLOG_INFO("Window {}: audio {} / pad {}", focused ? "focused" : "unfocused",
                block ? "muted" : "on", block ? "ignored" : "active");
  }
  void OnLoadXexImage(std::string& xex_image) override {
    std::string tail = xex_image;
    if (tail.rfind("game:\\", 0) == 0) {
      tail.erase(0, 6);
    }
    std::replace(tail.begin(), tail.end(), '\\', '/');
    RRXX_CheckXexImage(game_data_root_ / tail);
  }
  // void OnPostSetup() override {}
  // Self-tests (RR-NR-7): rrxx_selftest_keys = "F4@75,F4@83" presses host keys (key binds such as the
  // F4 settings overlay) at those seconds; scripted pads (input_script) can't reach the overlay.
  void OnCreateDialogs(rex::ui::ImGuiDrawer* drawer) override {
    (void)drawer;
    const std::string spec = REXCVAR_GET(rrxx_selftest_keys);
    if (spec.empty())
      return;
    auto alive = selftest_keys_alive_;
    std::thread([this, spec, alive] {
      const auto t0 = std::chrono::steady_clock::now();
      size_t pos = 0;
      while (pos < spec.size()) {
        size_t end = spec.find(',', pos);
        if (end == std::string::npos) end = spec.size();
        const std::string item = spec.substr(pos, end - pos);
        pos = end + 1;
        const size_t at = item.find('@');
        if (at == std::string::npos) continue;
        const std::string key = item.substr(0, at);
        const double secs = std::atof(item.c_str() + at + 1);
        std::this_thread::sleep_until(t0 + std::chrono::milliseconds(static_cast<int64_t>(secs * 1000)));
        if (!*alive) return;
        app_context().CallInUIThreadDeferred([this, key, alive] {
          if (!*alive || !window()) return;
          rex::ui::KeyEvent e(window(), rex::ui::ParseVirtualKey(key), 0, false, false, false, false, false);
          const bool hit = rex::ui::ProcessKeyEvent(e);
          REXLOG_INFO("rrxx_selftest_keys: {} {}", key, hit ? "pressed" : "no bind");
        });
      }
    }).detach();
  }
  // void OnShutdown() override {}
  void OnShutdown() override {
    *selftest_keys_alive_ = false;
#if defined(RRXX_NATIVE_HOOKS)
    overlay_drawer_ = nullptr;
#endif
  }
  void OnConfigurePaths(rex::PathConfig& paths) override {
    if (!paths.game_data_root.empty()) {
      game_data_root_ = paths.game_data_root;
      return;
    }

    const auto exe_dir = rex::filesystem::GetExecutableFolder();
    for (const auto& candidate : {
             exe_dir / "assets",
             exe_dir / ".." / ".." / ".." / "assets",
             exe_dir / ".." / ".." / "assets",
             std::filesystem::current_path() / "assets",
         }) {
      std::error_code ec;
      auto resolved = std::filesystem::weakly_canonical(candidate, ec);
      if (!ec && std::filesystem::is_directory(resolved)) {
        paths.game_data_root = resolved;
        game_data_root_ = resolved;
        return;
      }
    }
  }

 private:
  std::filesystem::path game_data_root_;
  std::shared_ptr<std::atomic<bool>> selftest_keys_alive_ = std::make_shared<std::atomic<bool>>(true);
#if defined(RRXX_NATIVE_HOOKS)
  bd::gpu::ImGuiOverlayDrawer* overlay_drawer_ = nullptr;  // owned by ReXApp's immediate drawer
  std::atomic<bool> overlay_capture_pending_{false};
#endif
};
