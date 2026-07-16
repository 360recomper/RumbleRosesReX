#include <rex/cvar.h>

REXCVAR_DEFINE_STRING(gpu_backend, "d3d12", "GPU", "Graphics backend: d3d12, vulkan")
    .allowed({"d3d12", "vulkan"})
    .lifecycle(rex::cvar::Lifecycle::kInitOnly);
