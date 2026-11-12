// Native renderer scaffolding (RR-NR-4): compiled only with RRXX_NATIVE_RENDERER (cmake/native_renderer.cmake).
// Checks that the RR D3D layout headers from the research repo's native/src build with the game's flags.
// The hooks and the plume device come in RR-NR-5/6.

#include "rr_d3d.h"
#include "rr_d3d_names.h"

static_assert(rrxx::d3d::kDeviceSize == 0x5080, "RR XDK 2.0.2500 D3DDevice size");
