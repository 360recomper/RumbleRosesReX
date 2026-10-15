// RumbleRosesReX - ReXGlue Recompiled Project
//
// Customize your app by overriding virtual hooks from rex::ReXApp.

#pragma once

#include <rex/audio/audio_driver.h>
#include <rex/cvar.h>
#include <rex/filesystem.h>
#include <rex/rex_app.h>
#include <rex/runtime.h>
#include <rex/system/gpu_plugin.h>

#include <rex/input/input_system.h>
#include <rex/logging.h>

REXCVAR_DECLARE(std::string, gpu_backend);
REXCVAR_DECLARE(bool, pause_on_focus_loss);

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
  void OnPreSetup(rex::RuntimeConfig& config) override {
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
  // Alt-tab: mute audio and hide the pad from the game (no thread suspend).
  void OnWindowFocusChanged(bool focused) override {
    const bool block = !focused && REXCVAR_GET(pause_on_focus_loss);
    rex::audio::g_audio_muted.store(block);
    rex::input::g_input_blocked.store(block);
    REXLOG_INFO("Window {}: audio {} / pad {}", focused ? "focused" : "unfocused",
                block ? "muted" : "on", block ? "ignored" : "active");
  }
  // void OnLoadXexImage(std::string& xex_image) override {}
  // void OnPostSetup() override {}
  // void OnCreateDialogs(rex::ui::ImGuiDrawer* drawer) override {}
  // void OnShutdown() override {}
  void OnConfigurePaths(rex::PathConfig& paths) override {
    if (!paths.game_data_root.empty())
      return;

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
        return;
      }
    }
  }
};
