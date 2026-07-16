// RumbleRosesReX - title-specific runtime hooks

#include "generated/default/rumble_roses_xx_init.h"

#include <atomic>

namespace {

std::atomic_bool g_warned_null_cleanup_slot{false};
std::atomic_bool g_warned_invalid_cleanup_object{false};
std::atomic_bool g_warned_invalid_cleanup_owner{false};

constexpr uint32_t kMinimumGuestPointer = 0x10000;

bool IsLikelyGuestPointer(uint32_t address) {
  return address >= kMinimumGuestPointer;
}

}  // namespace

extern "C" REX_FUNC(sub_823074B0) {
  uint32_t ea{};

  // Original function body with a guard for the final cleanup callback slot.
  ctx.r12.u64 = ctx.lr;
  REX_STORE_U32(ctx.r1.u32 + -8, ctx.r12.u32);
  REX_STORE_U64(ctx.r1.u32 + -16, ctx.r31.u64);
  ea = -96 + ctx.r1.u32;
  REX_STORE_U32(ea, ctx.r1.u32);
  ctx.r1.u32 = ea;

  ctx.r31.u64 = ctx.r3.u64;
  if (IsLikelyGuestPointer(ctx.r31.u32)) {
    ctx.r11.u64 = REX_LOAD_U32(ctx.r31.u32 + 4);
    ctx.cr6.compare<uint32_t>(ctx.r11.u32, 0, ctx.xer);
    if (!ctx.cr6.eq) {
      const uint32_t cleanup_owner = ctx.r11.u32;
      if (IsLikelyGuestPointer(cleanup_owner)) {
        ctx.r11.u64 = REX_LOAD_U32(cleanup_owner + 40);
      } else {
        ctx.r11.u64 = 0;
        if (!g_warned_invalid_cleanup_owner.exchange(true)) {
          REXLOG_WARN(
              "Skipping invalid Rumble Roses XX cleanup owner 0x{:08X} at 0x823074B0",
              cleanup_owner);
        }
      }

      if (ctx.r11.u32 != 0) {
        ctx.ctr.u64 = ctx.r11.u64;
        ctx.lr = 0x823074DC;
        REX_CALL_INDIRECT_FUNC(ctx.ctr.u32);
      } else if (!g_warned_null_cleanup_slot.exchange(true)) {
        REXLOG_WARN("Skipping null Rumble Roses XX cleanup callback at 0x823074B0");
      }
    }
  } else if (!g_warned_invalid_cleanup_object.exchange(true)) {
    REXLOG_WARN(
        "Skipping invalid Rumble Roses XX cleanup object 0x{:08X} at 0x823074B0",
        ctx.r31.u32);
  }

  if (IsLikelyGuestPointer(ctx.r31.u32)) {
    ctx.r11.s64 = 0;
    REX_STORE_U32(ctx.r31.u32 + 4, ctx.r11.u32);
    REX_STORE_U32(ctx.r31.u32 + 20, ctx.r11.u32);
  }

  ctx.r1.s64 = ctx.r1.s64 + 96;
  ctx.r12.u64 = REX_LOAD_U32(ctx.r1.u32 + -8);
  ctx.lr = ctx.r12.u64;
  ctx.r31.u64 = REX_LOAD_U64(ctx.r1.u32 + -16);
}
