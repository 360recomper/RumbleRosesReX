// RumbleRosesReX - ReXGlue Recompiled Project
//
// Customize your app by overriding virtual hooks from rex::ReXApp.

#pragma once

#include <rex/cvar.h>
#include <rex/filesystem.h>
#if REX_HAS_D3D12
#include <rex/graphics/d3d12/graphics_system.h>
#endif
#if REX_HAS_VULKAN
#include <rex/graphics/vulkan/graphics_system.h>
#endif
#include <rex/rex_app.h>
#include <rex/runtime.h>

REXCVAR_DECLARE(std::string, gpu_backend);

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
#if REX_HAS_VULKAN
    if (REXCVAR_GET(gpu_backend) == "vulkan") {
      config.graphics = REX_GRAPHICS_BACKEND(rex::graphics::vulkan::VulkanGraphicsSystem);
      return;
    }
#endif
#if REX_HAS_D3D12
    if (REXCVAR_GET(gpu_backend) == "d3d12") {
      config.graphics = REX_GRAPHICS_BACKEND(rex::graphics::d3d12::D3D12GraphicsSystem);
      return;
    }
#endif
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
