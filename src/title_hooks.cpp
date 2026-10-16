// RumbleRosesReX - title-specific runtime hooks

#include "generated/default/rumble_roses_xx_init.h"

#include <rex/cvar.h>
#include <rex/filesystem.h>
#include <rex/memory.h>
#include <rex/system/kernel_state.h>

#include <algorithm>
#include <chrono>
#include <atomic>
#include <deque>
#include <mutex>
#include <string>
#include <cctype>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <unordered_map>
#include <unordered_set>
#include <vector>

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

// Bloom passes (manifest [[midasm_hook]] after "li r6,1"; r6 = pass enabled).
REXCVAR_DECLARE(int32_t, bloom);

void RRXX_BloomPass1(PPCRegister& r6) {
  if (REXCVAR_GET(bloom) < 2) {
    r6.s64 = 0;
  }
}

void RRXX_BloomPass2(PPCRegister& r6) {
  if (REXCVAR_GET(bloom) < 1) {
    r6.s64 = 0;
  }
}

// Blur passes (community xenia patch "Blur Off"), same r6 = enabled scheme.
REXCVAR_DECLARE(bool, blur);

void RRXX_BlurPass(PPCRegister& r6) {
  if (!REXCVAR_GET(blur)) {
    r6.s64 = 0;
  }
}

// Returns true to skip the "bl 0x821e92c8" blur quad draw in sub_8226DC50.
bool RRXX_SkipBlurQuad() {
  return !REXCVAR_GET(blur);
}

// Cheats. sub_8216DFA0 returns the static game data object (the save file is a
// raw copy of it, so save offsets == object offsets). It runs on the game thread
// right before the game reads its data, so patching here avoids races.
REXCVAR_DECLARE(bool, unlock_all);
REXCVAR_DECLARE(bool, infinite_money);

namespace {

constexpr uint32_t kGameData = 0x8275AF78;
constexpr uint32_t kMoney = kGameData + 0x68828;           // BE u32 RR cash
constexpr uint32_t kMoneyMax = 9999999;
constexpr uint32_t kPersonas = kGameData + 0x29565;        // u8[20], 3 = unlocked
constexpr uint32_t kLadyXFlags = kGameData + 0x31188;      // bit0 = Lady X (personas 20/21)
constexpr uint32_t kItemsBegin = kGameData + 0x311A0;      // 0x20-byte shop item records
constexpr uint32_t kItemsEnd = kGameData + 0x32680;
constexpr uint32_t kItemOwnedAll = 0x001FFFFF;             // +0x08 owned mask

uint32_t LoadBE32(const uint8_t* p) {
  return (uint32_t(p[0]) << 24) | (uint32_t(p[1]) << 16) | (uint32_t(p[2]) << 8) | p[3];
}

void StoreBE32(uint8_t* p, uint32_t v) {
  p[0] = uint8_t(v >> 24);
  p[1] = uint8_t(v >> 16);
  p[2] = uint8_t(v >> 8);
  p[3] = uint8_t(v);
}

}  // namespace

void LogMovesDumpImage();

void RRXX_ApplyCheats() {
  LogMovesDumpImage();
  const bool unlock = REXCVAR_GET(unlock_all);
  const bool money = REXCVAR_GET(infinite_money);
  if (!unlock && !money) {
    return;
  }
  uint8_t* base = rex::system::kernel_state()->memory()->virtual_membase();

  if (money && LoadBE32(base + kMoney) < kMoneyMax) {
    StoreBE32(base + kMoney, kMoneyMax);
  }

  if (unlock) {
    uint8_t* personas = base + kPersonas;
    for (int i = 0; i < 20; ++i) {
      personas[i] = 3;
    }
    uint8_t* lady_x = base + kLadyXFlags;
    StoreBE32(lady_x, LoadBE32(lady_x) | 1);
    // Records with a non-zero owned mask are real items; all-zero ones are unused.
    for (uint32_t rec = kItemsBegin; rec < kItemsEnd; rec += 0x20) {
      uint8_t* r = base + rec;
      const uint32_t owned = LoadBE32(r + 0x08);
      if (owned != 0 && owned != kItemOwnedAll) {
        StoreBE32(r + 0x08, kItemOwnedAll);
        StoreBE32(r + 0x18, 0xFFFFFFFF);
        StoreBE32(r + 0x1C, 0xFFFFFFFF);
      }
    }
  }
}

// Cut content / debug.
REXCVAR_DECLARE(bool, debug_event_menu);
REXCVAR_DECLARE(bool, log_asset_paths);

// Before "cmpwi cr6,r3,0" in sub_822607D8: r3 = "an event is queued". Clearing it
// sends the event scene to its leftover debug menu (vtable slot 13).
void RRXX_DebugEventMenu(PPCRegister& r3) {
  if (REXCVAR_GET(debug_event_menu)) {
    r3.u64 = 0;
  }
}

// Entry of sub_82092238(path, out): log each distinct archive path once.
void RRXX_LogAssetPath(PPCRegister& r3) {
  if (!REXCVAR_GET(log_asset_paths) || !IsLikelyGuestPointer(r3.u32)) {
    return;
  }
  const char* guest = reinterpret_cast<const char*>(
      rex::system::kernel_state()->memory()->virtual_membase() + r3.u32);
  std::string path;
  for (int i = 0; i < 64 && guest[i]; ++i) {
    path.push_back(guest[i]);
  }
  static std::mutex mutex;
  static std::unordered_set<std::string> seen;
  std::lock_guard<std::mutex> lock(mutex);
  if (seen.insert(path).second) {
    REXLOG_INFO("RRXX asset path: {}", path);
  }
}

// Created wrestlers ("Lambda").
REXCVAR_DECLARE(bool, log_lambda_parts);

namespace {

constexpr uint32_t kCharBuilderPtr = 0x82A8BCDC;  // global builder G (record at G+8600)
constexpr uint32_t kEditCompositePtr = 0x82A859C8;  // edit-mode composite

uint32_t GuestU32(uint32_t address) {
  if (!IsLikelyGuestPointer(address)) {
    return 0;
  }
  return LoadBE32(rex::system::kernel_state()->memory()->virtual_membase() + address);
}

// Skips lines seen among the last 256 (part loads repeat every frame while
// pending) but logs them again when they come back later.
void LogLambdaOnce(const std::string& line) {
  static std::mutex mutex;
  static std::deque<std::string> recent;
  std::lock_guard<std::mutex> lock(mutex);
  if (std::find(recent.begin(), recent.end(), line) != recent.end()) {
    return;
  }
  recent.push_back(line);
  if (recent.size() > 256) {
    recent.pop_front();
  }
  REXLOG_INFO("RRXX lambda: {}", line);
}

std::string BuilderContext() {
  const uint32_t g = GuestU32(kCharBuilderPtr);
  if (g == 0) {
    return "G=0";
  }
  return fmt::format("G={:08X} Gcomp={:08X} Grec=({},{}) editcomp={:08X}", g, GuestU32(g + 4),
                     GuestU32(g + 8600), GuestU32(g + 8604), GuestU32(kEditCompositePtr));
}

void LogPart(const char* which, PPCRegister& r3, PPCRegister& r4, PPCRegister& r5,
             PPCRegister& r6, PPCRegister& r7, PPCRegister& r12) {
  if (!REXCVAR_GET(log_lambda_parts)) {
    return;
  }
  LogLambdaOnce(fmt::format("part{} from={:08X} comp={:08X} slot={} id={} kind={} var={} | {}",
                            which, r12.u32, r3.u32, r4.s32, r5.s32, r6.s32, r7.s32,
                            BuilderContext()));
}

}  // namespace

// lambda_model_overrides: "3=480, 5=481+head, 1=487:head" (EDIT number = DLC costume id).
REXCVAR_DECLARE(std::string, lambda_model_overrides);

namespace {

constexpr int kLambdaSlots = 30;
constexpr uint32_t kEditLoaderBegin = 0x82180920;  // sub_82180920 (select / Locker Room)
constexpr uint32_t kEditLoaderEnd = 0x821816A0;
constexpr int32_t kFacePartId = 5000;              // EDFC face models
constexpr int32_t kDlcKindHair = 4;                // bs_hair (DLC part kinds:
constexpr int32_t kDlcKindFace = 5;                //   0 arm 1 trunk 2 waist 3 leg
constexpr int32_t kDlcKindHead = 6;                //   4 hair 5 face 6 head)

struct LambdaOverride {
  int32_t costume_id = 0;  // 0 = none
  bool keep_head = false;  // "+head": outfit only, Lambda keeps her face and hair
  bool head_only = false;  // ":head": face and hair only, Customize outfit stays
};

std::string Trim(const std::string& s) {
  const size_t b = s.find_first_not_of(" 	");
  const size_t e = s.find_last_not_of(" 	");
  return b == std::string::npos ? std::string() : s.substr(b, e - b + 1);
}

// Re-parsed only when the setting text changes (it is hot-reloadable).
LambdaOverride GetLambdaOverride(uint32_t lambda_index) {
  static std::mutex mutex;
  static std::string parsed_text;
  static LambdaOverride table[kLambdaSlots];
  std::lock_guard<std::mutex> lock(mutex);
  const std::string text = REXCVAR_GET(lambda_model_overrides);
  if (text != parsed_text) {
    parsed_text = text;
    for (auto& entry : table) {
      entry = {};
    }
    size_t pos = 0;
    while (pos <= text.size()) {
      size_t comma = text.find(',', pos);
      if (comma == std::string::npos) {
        comma = text.size();
      }
      std::string item = Trim(text.substr(pos, comma - pos));
      pos = comma + 1;
      if (item.empty()) {
        continue;
      }
      LambdaOverride entry;
      const size_t plus = item.find('+');
      if (plus != std::string::npos) {
        entry.keep_head = Trim(item.substr(plus + 1)) == "head";
        item = Trim(item.substr(0, plus));
      }
      const size_t colon = item.find(':');
      if (colon != std::string::npos) {
        entry.head_only = Trim(item.substr(colon + 1)) == "head";
        item = Trim(item.substr(0, colon));
      }
      const size_t eq = item.find('=');
      int slot = 0;
      int id = 0;
      if (eq != std::string::npos) {
        slot = std::atoi(item.substr(0, eq).c_str());
        id = std::atoi(item.substr(eq + 1).c_str());
      }
      if (slot < 1 || slot > kLambdaSlots || id < 200 || id > 999) {
        REXLOG_WARN("lambda_model_overrides: ignoring \"{}\" (want EDIT 1-30 = costume id 200-999)",
                    item);
        continue;
      }
      entry.costume_id = id;
      table[slot - 1] = entry;
      REXLOG_INFO("lambda_model_overrides: EDIT{} -> costume {}{}", slot, id,
                  entry.keep_head   ? " (keeps Lambda head)"
                  : entry.head_only ? " (head only, keeps outfit)"
                                    : "");
    }
  }
  return lambda_index < kLambdaSlots ? table[lambda_index] : LambdaOverride{};
}

// Which character the composite being built belongs to: the select / Locker
// Room loader (sub_82180920 keeps itself in r21; +12 mode, +16 index) or the
// Customize builder G (record copy at G+8600: +0 mode, +4 index).
// Mode 0 = Lambda, 1 = built-in wrestler.
bool FindPartOwner(uint32_t composite, uint32_t caller, uint32_t r21, uint32_t* mode,
                   uint32_t* index) {
  if (caller >= kEditLoaderBegin && caller < kEditLoaderEnd && IsLikelyGuestPointer(r21)) {
    *mode = GuestU32(r21 + 12);
    *index = GuestU32(r21 + 16);
    return true;
  }
  const uint32_t g = GuestU32(kCharBuilderPtr);
  if (g != 0 && composite == GuestU32(g + 4)) {
    *mode = GuestU32(g + 8600);
    *index = GuestU32(g + 8604);
    return true;
  }
  return false;
}

}  // namespace

// Entry of sub_82180760(composite, slot, id, kind, variant). Returns true to
// skip the part (r3 = 0, like a part that is not available).
bool RRXX_LambdaPart(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5, PPCRegister& r6,
                     PPCRegister& r7, PPCRegister& r12, PPCRegister& r21) {
  LogPart("A", r3, r4, r5, r6, r7, r12);
  if (REXCVAR_GET(lambda_model_overrides).empty()) {
    return false;
  }
  uint32_t mode = 0;
  uint32_t index = 0;
  if (!FindPartOwner(r3.u32, r12.u32, r21.u32, &mode, &index) || mode != 0) {
    return false;
  }
  const LambdaOverride ov = GetLambdaOverride(index);
  if (ov.costume_id == 0) {
    return false;
  }
  const int32_t slot = r4.s32;
  const int32_t id = r5.s32;
  bool skip = false;
  if (slot >= 4 && slot <= 7) {
    if (ov.head_only) {
      return false;  // Customize gloves / outerwear / pants / boots stay
    }
    r5.s64 = ov.costume_id;  // outfit pieces: bs_arm / bs_trunk / bs_waist / bs_leg
  } else if (!ov.keep_head && !ov.head_only && (slot == 8 || id == kFacePartId)) {
    // Full diva: her bs_waist already has head and hair. Skip the Lambda's hair
    // and face instead of pointing them at the slot, so the same slot can also
    // hold bs_head / bs_hair / bs_face for ":head" (DV-5).
    skip = true;
  } else if (!ov.keep_head && slot == 8) {
    r5.s64 = ov.costume_id;  // hair -> bs_hair (skipped by the game if missing)
    r6.s64 = kDlcKindHair;
  } else if (!ov.keep_head && id == kFacePartId) {
    if (slot == 9) {
      r5.s64 = ov.costume_id;
      r6.s64 = kDlcKindHead;
    } else if (slot == 10) {
      r5.s64 = ov.costume_id;
      r6.s64 = kDlcKindFace;
    } else {
      skip = true;  // Customize's third face part (slot 14)
    }
  } else {
    return false;
  }
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("override EDIT{} slot={} -> {}", index + 1, slot,
                              skip ? std::string("skipped")
                                   : fmt::format("id={} kind={}", r5.s32, r6.s32)));
  }
  if (skip) {
    r3.u64 = 0;
  }
  return skip;
}

// DV-3 fix. Each part gets a model code (+1140): the pac key of its model file
// (stock edit parts 0, face EDFC/M001 1, hair ED0n/M400 10000). A DLC part takes
// its code from the matching texture entry in e<id>.def, so the bs_head / bs_hair
// parts of a head-only override get 0. In the Locker Room / select merge
// (sub_82213DE0) slots 8 and 9 become sub-models of the merged body, and with
// code 0 there the body, the outfit and the other sub-models stop drawing
// (Customize does not merge, so it looked fine). Give DLC hair and head parts
// the stock codes. Sits on "stw r9,1140(r31)" in sub_8220AB48 (DLC load done).
void RRXX_LambdaDlcPartCode(PPCRegister& r9, PPCRegister& r31) {
  if (r9.u32 != 0 || REXCVAR_GET(lambda_model_overrides).empty() ||
      !IsLikelyGuestPointer(r31.u32)) {
    return;
  }
  const uint32_t id = GuestU32(r31.u32 + 1124);
  const uint32_t kind = GuestU32(r31.u32 + 1128);
  if (id < 200 || id >= 1000) {
    return;
  }
  if (kind == uint32_t(kDlcKindHair)) {
    r9.u64 = 10000;
  } else if (kind == uint32_t(kDlcKindHead)) {
    r9.u64 = 1;
  } else {
    return;
  }
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("dlcCode part={:08X} id={} kind={} -> code {}", r31.u32, id, kind,
                              r9.u32));
  }
}

void RRXX_LogPartB(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5, PPCRegister& r6,
                   PPCRegister& r7, PPCRegister& r12) {
  LogPart("B", r3, r4, r5, r6, r7, r12);
}

void RRXX_LogPartCtor(PPCRegister& r4, PPCRegister& r5, PPCRegister& r6, PPCRegister& r12) {
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(
        fmt::format("ctor from={:08X} id={} kind={} var={}", r12.u32, r4.s32, r5.s32, r6.s32));
  }
}

void RRXX_LogRecordLoadG(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5) {
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("recordG G={:08X} mode={} idx={} comp={:08X}", r3.u32, r4.s32,
                              r5.s32, GuestU32(r3.u32 + 4)));
  }
}

void RRXX_LogRecordLoadEdit(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5) {
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("recordEdit loader={:08X} mode={} idx={}", r3.u32, r4.s32, r5.s32));
  }
}

void RRXX_LogBodyBuiltin(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5, PPCRegister& r6) {
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("bodyBuiltin comp={:08X} char={} a={} b={} | {}", r3.u32, r4.s32,
                              r5.s32, r6.s32, BuilderContext()));
  }
}

void RRXX_LogBodyLambda(PPCRegister& r3, PPCRegister& r4) {
  if (REXCVAR_GET(log_lambda_parts)) {
    const uint32_t p = r4.u32;
    LogLambdaOnce(fmt::format("bodyLambda comp={:08X} skn={} eye={} lip={} may={} | {}", r3.u32,
                              GuestU32(p), GuestU32(p + 4), GuestU32(p + 8), GuestU32(p + 12),
                              BuilderContext()));
  }
}

void RRXX_LogBodyLambdaAll(PPCRegister& r3) {
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("bodyLambdaAll comp={:08X} | {}", r3.u32, BuilderContext()));
  }
}

void RRXX_LogDlcPart(PPCRegister& r3, PPCRegister& r4, PPCRegister& r12) {
  if (REXCVAR_GET(log_lambda_parts)) {
    LogLambdaOnce(fmt::format("dlcPart from={:08X} id={} kind={}", r12.u32, r3.s32, r4.s32));
  }
}

void RRXX_LogRecordSaveG(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5) {
  if (!REXCVAR_GET(log_lambda_parts) || !IsLikelyGuestPointer(r3.u32)) {
    return;
  }
  const uint8_t* rec = rex::system::kernel_state()->memory()->virtual_membase() + r3.u32 + 8600;
  std::string hex;
  for (int i = 0; i < 492; ++i) {
    hex += fmt::format("{:02x}", rec[i]);
  }
  LogLambdaOnce(fmt::format("recordSave mode={} idx={} data={}", r4.s32, r5.s32, hex));
}

// DV-3 diagnostics (log_lambda_parts): on each state change of the select /
// Locker Room loader sub_82180920, dump the edit composite's parts with a hash
// of each part's raw model data (+692 ptr, +696 size), which the Locker Room
// merge (sub_82213DE0 -> sub_8227EE68) reads after the face parts load.
namespace {

uint32_t HashGuest(uint32_t address, uint32_t size) {
  if (!IsLikelyGuestPointer(address) || size == 0 || size > 0x1000000) {
    return 0;
  }
  const uint8_t* p = rex::system::kernel_state()->memory()->virtual_membase() + address;
  uint32_t h = 2166136261u;
  for (uint32_t i = 0; i < size; ++i) {
    h = (h ^ p[i]) * 16777619u;
  }
  return h;
}

void DumpEditComposite(const std::string& why) {
  const uint32_t comp = GuestU32(kEditCompositePtr);
  if (comp == 0) {
    return;
  }
  for (uint32_t slot = 0; slot < 16; ++slot) {
    const uint32_t p = GuestU32(comp + 4 + slot * 4);
    if (p == 0) {
      continue;
    }
    const uint32_t data = GuestU32(p + 692);
    const uint32_t size = GuestU32(p + 696);
    REXLOG_INFO(
        "RRXX lambda: [{}] comp={:08X} slot={} part={:08X} id={} kind={} st={} ld={} code={} "
        "flags={:X} model={:08X} raw={:08X}/{:X} data={:08X}+{:X} hash={:08X} kids={}",
        why, comp, slot, p, GuestU32(p + 1124), GuestU32(p + 1128), GuestU32(p + 1092),
        GuestU32(p + 1096), GuestU32(p + 1140), GuestU32(p + 816), GuestU32(p + 548),
        GuestU32(p + 684), GuestU32(p + 688), data, size, HashGuest(data, size),
        GuestU32(p + 496));
  }
}

}  // namespace

void RRXX_LogLockerTick(PPCRegister& r3) {
  if (!REXCVAR_GET(log_lambda_parts) || !IsLikelyGuestPointer(r3.u32)) {
    return;
  }
  static std::mutex mutex;
  static uint32_t last_loader = 0;
  static uint32_t last_state = 0xFFFFFFFF;
  std::lock_guard<std::mutex> lock(mutex);
  const uint32_t state = GuestU32(r3.u32 + 8);
  if (r3.u32 == last_loader && state == last_state) {
    return;
  }
  last_loader = r3.u32;
  last_state = state;
  REXLOG_INFO("RRXX lambda: loader {:08X} state {} mode={} idx={}", r3.u32, state,
              GuestU32(r3.u32 + 12), GuestU32(r3.u32 + 16));
  DumpEditComposite(fmt::format("s{}", state));
}

// sub_8227A4E0 (CEditDLLoader file read) right after it allocated the buffer.
void RRXX_LogDlcRead(PPCRegister& r3, PPCRegister& r31) {
  if (!REXCVAR_GET(log_lambda_parts) || !IsLikelyGuestPointer(r31.u32)) {
    return;
  }
  const char* path = reinterpret_cast<const char*>(
      rex::system::kernel_state()->memory()->virtual_membase() + r31.u32 + 1248);
  REXLOG_INFO("RRXX lambda: dlcRead loader={:08X} buf={:08X} size={:X} file={}", r31.u32,
              r3.u32, GuestU32(r31.u32 + 844), std::string(path, strnlen(path, 200)));
}

// Exhibition stage select. The picker on the
// character-select screen (object = char select + 11752, cursor at +144) is built
// for 3 stages: sub_8214CAC8 maps the cursor to a stage (0, 1, 4), sub_8214CA80
// maps a stage back to the cursor, the input handler sub_8214D0B0 (on the +28
// list sub-object, cursor at +116) wraps at 2, and sub_8214CB80 creates the list
// with a count of 3. exhibition_stages replaces that fixed list. The card strip and
// stage names come from menu.pac MENU/CSLL, which must be built for the same list
// (a modded menu.pac).
REXCVAR_DECLARE(std::string, exhibition_stages);

namespace {

const std::vector<int32_t>& ExhibitionStages() {
  static std::string parsed_text;
  static std::vector<int32_t> stages;
  const std::string text = REXCVAR_GET(exhibition_stages);
  if (text == parsed_text) {
    return stages;
  }
  parsed_text = text;
  stages.clear();
  size_t pos = 0;
  while (pos <= text.size()) {
    size_t end = text.find(',', pos);
    if (end == std::string::npos) {
      end = text.size();
    }
    std::string item = text.substr(pos, end - pos);
    item.erase(std::remove_if(item.begin(), item.end(), ::isspace), item.end());
    if (!item.empty()) {
      char* tail = nullptr;
      const long id = std::strtol(item.c_str(), &tail, 10);
      if (*tail == '\0' && id >= 0 && id <= 127) {
        stages.push_back(int32_t(id));
      } else {
        REXLOG_WARN("exhibition_stages: ignoring \"{}\" (want stage ids 0-127)", item);
      }
    }
    pos = end + 1;
  }
  if (stages.size() == 1) {
    REXLOG_WARN("exhibition_stages: a single stage is not supported, using the original list");
    stages.clear();
  }
  if (!stages.empty()) {
    std::string list;
    for (int32_t id : stages) {
      list += (list.empty() ? "" : ",") + std::to_string(id);
    }
    REXLOG_INFO("exhibition_stages: {} stages [{}]", stages.size(), list);
  }
  return stages;
}

uint8_t* GuestBase() {
  return rex::system::kernel_state()->memory()->virtual_membase();
}

}  // namespace

namespace {
constexpr uint32_t kStageIdAddress = 0x828302A8 + 0x160;
constexpr int32_t kFirstImportedStage = 12;
int32_t g_imported_stage = -1;  // real id while the byte holds a stand-in 0
}  // namespace

// sub_8214CAC8 entry (leaf): r3 = picker; returns the stage id for the cursor.
bool RRXX_StageFromCursor(PPCRegister& r3) {
  const auto& stages = ExhibitionStages();
  if (stages.empty() || !IsLikelyGuestPointer(r3.u32)) {
    return false;
  }
  const uint32_t cursor = LoadBE32(GuestBase() + r3.u32 + 144);
  const int32_t stage = stages[cursor < stages.size() ? cursor : 0];
  if (stage < kFirstImportedStage && g_imported_stage >= 0) {
    REXLOG_INFO("AR-10 picker: built-in stage {} under cursor, forget imported {}", stage, g_imported_stage);
    g_imported_stage = -1;
  }
  r3.u64 = uint64_t(stage);
  return true;
}

// sub_8214CA80 entry (leaf): r3 = picker, r4 = stage id; sets the cursor.
bool RRXX_CursorFromStage(PPCRegister& r3, PPCRegister& r4) {
  const auto& stages = ExhibitionStages();
  if (stages.empty() || !IsLikelyGuestPointer(r3.u32)) {
    return false;
  }
  const int32_t stage = r4.s32 == 0 && g_imported_stage >= 0 ? g_imported_stage : r4.s32;
  auto it = std::find(stages.begin(), stages.end(), stage);
  const uint32_t cursor = it == stages.end() ? 0 : uint32_t(it - stages.begin());
  StoreBE32(GuestBase() + r3.u32 + 144, cursor);
  return true;
}

// sub_8214D0B0 at 0x8214D100 (right pressed; r10 = cursor, r31 = list sub-object).
bool RRXX_StageNext(PPCRegister& r10, PPCRegister& r31) {
  const auto& stages = ExhibitionStages();
  if (stages.empty()) {
    return false;
  }
  const int32_t n = int32_t(stages.size());
  const int32_t next = r10.s32 + 1 >= n ? 0 : r10.s32 + 1;
  StoreBE32(GuestBase() + r31.u32 + 116, uint32_t(next));
  return true;  // continue at 0x8214D19C like the original
}

// sub_8214D0B0 at 0x8214D12C (left pressed).
bool RRXX_StagePrev(PPCRegister& r10, PPCRegister& r31) {
  const auto& stages = ExhibitionStages();
  if (stages.empty()) {
    return false;
  }
  const int32_t n = int32_t(stages.size());
  const int32_t prev = r10.s32 <= 0 ? n - 1 : r10.s32 - 1;
  StoreBE32(GuestBase() + r31.u32 + 116, uint32_t(prev));
  return true;
}

// sub_8214CB80 after "li r5,3": number of entries in the picker's list.
void RRXX_StageCount(PPCRegister& r5) {
  const auto& stages = ExhibitionStages();
  if (!stages.empty()) {
    r5.u64 = stages.size();
  }
}

// Imported stages (AR-10). Game logic checks the stage id (game data +0x160, signed
// byte, getter sub_8216DFB0) against 0-4 and 9-11 in about 65 places; any other id
// takes default paths, e.g. the EDIT 1/2 sepia entrance filter never turns off.
// So an imported id (>= 12) is stored as 0 (Arena) right after every game write of
// the byte, and only the stage file load (sub_82156CF0) and the picker cursor get
// the real id back.

// After each "stb rX,352(game data)" (13 sites, see manifest). Some code writes the
// stand-in 0 back (it read it from +0x160), so a 0 keeps the imported id; built-in
// ids 1-11, a built-in card in the picker, or a story-mode write (strict) end it.
namespace {
void StageIdWritten(uint32_t site, bool strict) {
  auto* id = reinterpret_cast<int8_t*>(GuestBase() + kStageIdAddress);
  const int32_t value = *id;
  if (value >= kFirstImportedStage) {
    g_imported_stage = value;
    *id = 0;
  } else if (value != 0 || strict) {
    g_imported_stage = -1;
  }
  REXLOG_INFO("AR-10 stage write @{:08X}: {} -> byte {}, imported {}", site, value, int32_t(*id),
              g_imported_stage);
}
}  // namespace

#define RRXX_STAGE_SITE(addr, strict)   void RRXX_StageIdWritten_##addr() { StageIdWritten(0x##addr, strict); }
RRXX_STAGE_SITE(820E3700, false)  // sub_820E3318 match setup
RRXX_STAGE_SITE(820E5DBC, false)  // sub_820E5B50 game data reset
RRXX_STAGE_SITE(82133630, true)   // sub_821334C0 story match
RRXX_STAGE_SITE(82295BD0, false)  // sub_82295AF8
RRXX_STAGE_SITE(82297020, false)  // sub_82296F88 (9)
RRXX_STAGE_SITE(8229A3E0, false)  // sub_8229A290
RRXX_STAGE_SITE(8229A5C0, false)  // sub_8229A560 (0)
RRXX_STAGE_SITE(822A4890, false)  // sub_822A4670
RRXX_STAGE_SITE(822A9910, false)  // sub_822A98B8
RRXX_STAGE_SITE(822ABDF4, false)  // sub_822ABD98
RRXX_STAGE_SITE(822ABEF4, false)  // sub_822ABEC0
RRXX_STAGE_SITE(822ABFBC, false)  // sub_822ABF78 (+=)
RRXX_STAGE_SITE(822AC478, false)  // sub_822AC3A0
#undef RRXX_STAGE_SITE

// sub_82156CF0 before the existence check and the "/STG/%04d" load: r4 = stage id.
void RRXX_StageFileId(PPCRegister& r4) {
  const int32_t game_id = r4.s32;
  if (game_id == 0 && g_imported_stage >= kFirstImportedStage) {
    r4.u64 = uint64_t(g_imported_stage);
  }
  REXLOG_INFO("AR-10 stage file: game id {} -> STG/{:04d}", game_id, r4.s32);
}

// Game heaps (FS-CAP). sub_82061828 carves 31 heaps out of one 0xDA00000-byte block
// via sub_82090318(block, end, sizes, 31); sizes = KiB table in .rdata at 0x82000BB8,
// carved from the top down, a 0 entry ends it and heap 30 (the default heap) gets the
// rest (~32 MiB). Heap 7 = 0x2100000 holds the m.pac MOT/GAME pack: one byte more and
// the allocation fails and the loader waits forever (boot freeze, APC 7).
// FS-CAP2: the block sits in guest physical 0x2E00000-0x10800000 (on the 360 the low
// 46 MB was kernel/exe); heap_block_extra_mb grows it downwards and gives heap 7 the
// extra, so heap 30 keeps its original size.
REXCVAR_DECLARE(int32_t, mot_heap_mb);
REXCVAR_DECLARE(int32_t, heap_block_extra_mb);
REXCVAR_DECLARE(bool, log_heaps);

namespace {
constexpr uint32_t kHeapSizeTable = 0x82000BB8;
constexpr uint32_t kHeapCount = 31;
constexpr uint32_t kMotHeap = 7;
constexpr uint32_t kHeapBlockSize = 0xDA00000;
constexpr uint32_t kMinDefaultHeapKiB = 16 * 1024;
constexpr uint32_t kHeapDescs = 0x8270C6F0;  // -> 24-byte {start, end, ...} per heap
// Lowest physical address the block may start at: 0x0-0x1000000 is GPU writeback.
constexpr uint32_t kLowestBlockPhys = 0x1000000;
uint32_t g_heap_block_extra = 0;  // bytes added to the block (set by RRXX_HeapBlockAlloc)
}  // namespace

// sub_82061828 after "lis r3,3488" (3rd sub_82065DC8 = MmAllocatePhysicalMemoryEx):
// r3 = size 0xDA00000, r4 = lowest physical address 0x2E00000, r5 = highest 0x10800000.
// The allocation is top-down, so a bigger size and a lower minimum grow the block down.
void RRXX_HeapBlockAlloc(PPCRegister& r3, PPCRegister& r4) {
  g_heap_block_extra = 0;
  const int32_t mib = REXCVAR_GET(heap_block_extra_mb);
  if (mib <= 0) {
    return;
  }
  uint32_t extra = uint32_t(mib) << 20;
  if (r4.u32 < kLowestBlockPhys + extra) {
    extra = r4.u32 > kLowestBlockPhys ? (r4.u32 - kLowestBlockPhys) & ~0xFFFFFu : 0;
    REXLOG_WARN("heap_block_extra_mb {}: limited to {} MiB", mib, extra >> 20);
  }
  if (r3.u32 != kHeapBlockSize || !extra) {
    return;
  }
  r3.u64 = r3.u32 + extra;
  r4.u64 = r4.u32 - extra;
  g_heap_block_extra = extra;
}

// After "mr r31,r3": r31 = the heap block (0 = failed; the game stops before this).
void RRXX_HeapBlockGot(PPCRegister& r31) {
  REXLOG_INFO("heap block at {:#010x}, {:#x} bytes (+{} MiB)", r31.u32,
              kHeapBlockSize + g_heap_block_extra, g_heap_block_extra >> 20);
}

// After "lis r4,3488": r4 = bytes the game clears (sub_82307FE8) = whole block.
void RRXX_HeapBlockClear(PPCRegister& r4) {
  r4.u64 = r4.u32 + g_heap_block_extra;
}

// sub_82061828 after "addi r5,r11,3000": r4 = block end (block + 0xDA00000, the game
// subtracts 512 next), r5 = heap size table.
void RRXX_HeapSizes(PPCRegister& r4, PPCRegister& r5) {
  const uint32_t extra = g_heap_block_extra;
  r4.u64 = r4.u32 + extra;
  const int32_t mib = REXCVAR_GET(mot_heap_mb);
  uint8_t* base = GuestBase();
  uint32_t kib[kHeapCount + 1];
  uint32_t used = 0;
  for (uint32_t i = 0; i <= kHeapCount; ++i) {
    kib[i] = LoadBE32(base + kHeapSizeTable + i * 4);
    if (i < kHeapCount) {
      used += kib[i];
    }
  }
  const uint32_t block_kib = (kHeapBlockSize + extra - 512) / 1024;
  const uint32_t default_kib = block_kib - used - extra / 1024;  // heap 30 as originally
  // Heap 7 gets the block's extra; mot_heap_mb (if bigger) takes more from heap 30.
  uint32_t want = kib[kMotHeap] + extra / 1024;
  uint32_t taken = 0;
  if (mib > 0 && uint32_t(mib) * 1024 > want) {
    taken = uint32_t(mib) * 1024 - want;
    if (default_kib < taken + kMinDefaultHeapKiB) {
      REXLOG_WARN("mot_heap_mb {}: default heap would drop below 16 MiB, ignored", mib);
      taken = 0;
    }
  }
  want += taken;
  if (want == kib[kMotHeap]) {
    return;
  }
  kib[kMotHeap] = want;
  auto* memory = rex::system::kernel_state()->memory();
  const uint32_t table = memory->SystemHeapAlloc(sizeof(kib));
  if (!table) {
    // The block is still bigger; the original table leaves the extra to heap 30.
    REXLOG_WARN("heap sizes: no guest memory for the heap table, keeping the original");
    return;
  }
  for (uint32_t i = 0; i <= kHeapCount; ++i) {
    StoreBE32(base + table + i * 4, kib[i]);
  }
  r5.u64 = table;
  REXLOG_INFO("heap sizes: MOT heap {} = {} KiB, default heap = {} KiB (original {})", kMotHeap,
              want, default_kib - taken, default_kib);
}

namespace {
struct HeapFree {
  uint32_t total = 0, largest = 0;
};

// Free list: record 0 at the heap start, word 2 = first free 16-byte unit; a free
// block at unit i has word 0 (low 28 bits) = its end unit and word 2 = next free unit.
HeapFree WalkHeap(uint8_t* base, uint32_t heap) {
  HeapFree f;
  const uint32_t descs = LoadBE32(base + kHeapDescs);
  if (!descs) {
    return f;
  }
  const uint32_t start = LoadBE32(base + descs + heap * 24);
  const uint32_t end = LoadBE32(base + descs + heap * 24 + 4);
  if (!start || end <= start) {
    return f;
  }
  const uint32_t units = (end - start) >> 4;
  uint32_t i = LoadBE32(base + start + 8);
  for (int guard = 0; i < units && guard < 1000000; ++guard) {
    const uint32_t rec = start + i * 16;
    const uint32_t stop = LoadBE32(base + rec) & 0x0FFFFFFF;
    if (stop > i + 1) {
      const uint32_t bytes = (stop - i - 1) * 16;
      f.total += bytes;
      f.largest = std::max(f.largest, bytes);
    }
    i = LoadBE32(base + rec + 8);
  }
  return f;
}

std::chrono::steady_clock::time_point g_next_heap_log{};
uint32_t g_heap_min_free[kHeapCount];
bool g_heap_min_init = false;
}  // namespace

// sub_8208FDC0 (game heap alloc) right after it takes the heap lock.
void RRXX_LogHeaps() {
  if (!REXCVAR_GET(log_heaps)) {
    return;
  }
  uint8_t* base = GuestBase();
  if (!g_heap_min_init) {
    std::fill(std::begin(g_heap_min_free), std::end(g_heap_min_free), UINT32_MAX);
    g_heap_min_init = true;
  }
  HeapFree f[kHeapCount];
  for (uint32_t h = 0; h < kHeapCount; ++h) {
    f[h] = WalkHeap(base, h);
    g_heap_min_free[h] = std::min(g_heap_min_free[h], f[h].total);
  }
  const auto now = std::chrono::steady_clock::now();
  if (now < g_next_heap_log) {
    return;
  }
  g_next_heap_log = now + std::chrono::seconds(10);
  std::string line;
  for (uint32_t h = 0; h < kHeapCount; ++h) {
    if (f[h].total || g_heap_min_free[h]) {
      line += fmt::format(" {}:{}K/min{}K/big{}K", h, f[h].total / 1024,
                          g_heap_min_free[h] / 1024, f[h].largest / 1024);
    }
  }
  REXLOG_INFO("heaps free:{}", line);
}

namespace {
// The game retries a failed allocation forever (that was the boot freeze), so log
// each failing size once.
void LogHeapFailure(int path, uint32_t heap, uint64_t size) {
  static std::mutex mutex;
  static std::unordered_set<uint64_t> seen;
  std::lock_guard<std::mutex> lock(mutex);
  if (seen.insert((uint64_t(path) << 60) | (uint64_t(heap) << 40) | size).second) {
    REXLOG_WARN("game heap alloc failed (path {}, heap id {}): {} bytes", path, heap, size);
  }
}
}  // namespace

// sub_8208FDC0 failure exit: r4 = current heap id, r7 = 16-byte units asked for.
void RRXX_HeapAllocFailed(PPCRegister& r4, PPCRegister& r7) {
  LogHeapFailure(1, r4.u32, uint64_t(r7.u32) * 16);
}

// sub_820908B0 failure exit: r28 = current heap id, r30 = 16-byte units asked for.
// Path 2 also fails harmlessly at boot (8 probes of ~8.7 MB, original sizes too).
void RRXX_HeapAllocFailed2(PPCRegister& r28, PPCRegister& r30) {
  LogHeapFailure(2, r28.u32, uint64_t(r30.u32) * 16);
}

// Extra entrance tracks (MU-4). sub_820858A0(name, &found)
// scans a fixed 63-entry table at 0x820057E8 (20 bytes: bank name, cue name, title,
// artist, album) for the cue name built from the music number: "ENTRANCE_%03d", or
// "MENU_%03d" with number - 46 for numbers >= 61 (61/62 = MENU_015/016). The player
// then asks XACT for the cue by name. Numbers 63+ get an entry here pointing at cue
// ENTRANCE_NNN in bank MUSIC_ENTRANCE; a modding tool adds those cues
// (and their waves) to the sound bank. A number without a cue plays nothing.
namespace {
constexpr uint32_t kMusicBankEntrance = 0x82003DF4;  // "MUSIC_ENTRANCE"
constexpr uint32_t kMusicAlbum = 0x820057A0;         // "Alubum", as in every table entry
constexpr int kFirstExtraTrack = 63;
constexpr int kExtraTrackCount = 64;                 // 63-126
constexpr uint32_t kExtraEntrySize = 20;
constexpr uint32_t kExtraStringsSize = 160;          // cue name 16 + title 80 + artist 64
constexpr uint32_t kExtraTitleSize = 80;
constexpr uint32_t kExtraArtistSize = 64;

// Optional captions from <mods_root>/music/titles.txt: "track<TAB>title<TAB>artist" per
// line (MU-4d; optional titles file). Missing = "Custom Track N".
struct ExtraTrackCaption {
  std::string title;
  std::string artist;
};

void LoadExtraTrackCaptions(ExtraTrackCaption (&captions)[kExtraTrackCount]) {
  const std::string root = rex::cvar::GetFlagByName("mods_root");
  if (root.empty()) {
    return;
  }
  std::ifstream file(rex::to_path(root) / "music" / "titles.txt");
  if (!file) {
    return;
  }
  std::string line;
  int count = 0;
  while (std::getline(file, line)) {
    if (!line.empty() && line.back() == '\r') {
      line.pop_back();
    }
    if (line.empty() || line[0] == '#') {
      continue;
    }
    const size_t tab1 = line.find('\t');
    if (tab1 == std::string::npos) {
      continue;
    }
    const int track = std::atoi(line.substr(0, tab1).c_str());
    if (track < kFirstExtraTrack || track >= kFirstExtraTrack + kExtraTrackCount) {
      continue;
    }
    const size_t tab2 = line.find('\t', tab1 + 1);
    ExtraTrackCaption& caption = captions[track - kFirstExtraTrack];
    caption.title = line.substr(tab1 + 1, tab2 == std::string::npos ? std::string::npos : tab2 - tab1 - 1);
    caption.artist = tab2 == std::string::npos ? std::string() : line.substr(tab2 + 1);
    ++count;
  }
  REXLOG_INFO("extra music tracks: {} titles from music/titles.txt", count);
}

// Returns the track number for a name the game builds, or -1.
int ExtraTrackFromName(const char* name) {
  int value = 0;
  const char* digits = nullptr;
  int offset = 0;
  if (std::strncmp(name, "ENTRANCE_", 9) == 0) {
    digits = name + 9;
  } else if (std::strncmp(name, "MENU_", 5) == 0) {
    digits = name + 5;
    offset = 46;
  } else {
    return -1;
  }
  for (int i = 0; i < 3; ++i) {
    if (!std::isdigit(static_cast<unsigned char>(digits[i]))) {
      return -1;
    }
    value = value * 10 + (digits[i] - '0');
  }
  if (digits[3] != '\0') {
    return -1;  // "_US" twins: let the game fall back to the plain name
  }
  const int track = value + offset;
  return track >= kFirstExtraTrack && track < kFirstExtraTrack + kExtraTrackCount ? track : -1;
}

uint32_t ExtraTrackEntry(int track) {
  static std::mutex mutex;
  static uint32_t block = 0;
  static bool logged[kExtraTrackCount] = {};
  std::lock_guard<std::mutex> lock(mutex);
  const int slot = track - kFirstExtraTrack;
  if (!block) {
    block = rex::system::kernel_state()->memory()->SystemHeapAlloc(
        kExtraTrackCount * (kExtraEntrySize + kExtraStringsSize));
    if (!block) {
      REXLOG_WARN("extra music tracks: guest alloc failed");
      return 0;
    }
    static ExtraTrackCaption captions[kExtraTrackCount];
    LoadExtraTrackCaptions(captions);
    uint8_t* base = GuestBase();
    for (int i = 0; i < kExtraTrackCount; ++i) {
      const uint32_t entry = block + i * kExtraEntrySize;
      const uint32_t strings = block + kExtraTrackCount * kExtraEntrySize + i * kExtraStringsSize;
      char* s = reinterpret_cast<char*>(base + strings);
      std::memset(s, 0, kExtraStringsSize);
      const ExtraTrackCaption& caption = captions[i];
      std::snprintf(s, 16, "ENTRANCE_%03d", kFirstExtraTrack + i);
      if (caption.title.empty()) {
        std::snprintf(s + 16, kExtraTitleSize, "Custom Track %03d", kFirstExtraTrack + i);
      } else {
        std::snprintf(s + 16, kExtraTitleSize, "%s", caption.title.c_str());
      }
      std::snprintf(s + 16 + kExtraTitleSize, kExtraArtistSize, "%s",
                    caption.artist.empty() ? "------------" : caption.artist.c_str());
      StoreBE32(base + entry + 0, kMusicBankEntrance);
      StoreBE32(base + entry + 4, strings);
      StoreBE32(base + entry + 8, strings + 16);
      StoreBE32(base + entry + 12, strings + 16 + kExtraTitleSize);
      StoreBE32(base + entry + 16, kMusicAlbum);
    }
  }
  if (!logged[slot]) {
    logged[slot] = true;
    REXLOG_INFO("extra music track {} -> cue ENTRANCE_{:03d} (MUSIC_ENTRANCE)", track, track);
  }
  return block + slot * kExtraEntrySize;
}
}  // namespace

// sub_820858A0 entry: r3 = cue name, r4 = found flag (u32). True = return r3 as the entry.
bool RRXX_ExtraMusicEntry(PPCRegister& r3, PPCRegister& r4) {
  if (!IsLikelyGuestPointer(r3.u32)) {
    return false;
  }
  const char* name = reinterpret_cast<const char*>(GuestBase() + r3.u32);
  const int track = ExtraTrackFromName(name);
  if (track < 0) {
    return false;
  }
  const uint32_t entry = ExtraTrackEntry(track);
  if (!entry) {
    return false;
  }
  StoreBE32(GuestBase() + r4.u32, 1);
  r3.u64 = entry;
  return true;
}

// EN-6h: titantron set per EDIT char. sub_822C70D8 takes the evt_eff.pac EVRT id from the
// entrant's model / 100 and maps every EDIT char (100-129) to 100 (li r4,100; r11 = real id).
// Keep the real id when evt_eff.pac (mods folder first, else the game's) has EVRT/<id>.
namespace {

std::unordered_set<int> LoadEvrtIds() {
  std::unordered_set<int> ids;
  std::vector<std::filesystem::path> candidates;
  const std::string root = rex::cvar::GetFlagByName("mods_root");
  if (!root.empty()) {
    candidates.push_back(rex::to_path(root) / "pac" / "evt_eff.pac");
  }
  const auto exe_dir = rex::filesystem::GetExecutableFolder();
  for (const auto& assets : {exe_dir / "assets", exe_dir / ".." / ".." / ".." / "assets",
                             exe_dir / ".." / ".." / "assets",
                             std::filesystem::current_path() / "assets"}) {
    candidates.push_back(assets / "pac" / "evt_eff.pac");
  }
  for (const auto& path : candidates) {
    std::ifstream file(path, std::ios::binary);
    if (!file) {
      continue;
    }
    std::vector<uint8_t> toc(0x4000);
    file.read(reinterpret_cast<char*>(toc.data()), toc.size());
    toc.resize(size_t(file.gcount()));
    if (toc.size() < 0x800 || std::memcmp(toc.data(), "EPAC", 4) != 0) {
      continue;
    }
    // Rows of 12 bytes from 0x800: name[4], u32 block, u32 length / 256. A row whose
    // block is not the running block names the group that follows (EVRT, PCEN, ...).
    auto le32 = [&](size_t at) {
      return uint32_t(toc[at]) | uint32_t(toc[at + 1]) << 8 | uint32_t(toc[at + 2]) << 16 |
             uint32_t(toc[at + 3]) << 24;
    };
    std::string group;
    uint32_t block = 0;
    for (size_t pos = 0x800; pos + 12 <= toc.size(); pos += 12) {
      const std::string name(reinterpret_cast<const char*>(&toc[pos]), 4);
      if (name == std::string(4, '\0')) {
        break;
      }
      const uint32_t off = le32(pos + 4);
      if (off != block) {
        group = name;
        continue;
      }
      block += (le32(pos + 8) * 256 + 2047) / 2048;
      if (group == "EVRT" && std::all_of(name.begin(), name.end(), ::isdigit)) {
        ids.insert(std::atoi(name.c_str()));
      }
    }
    REXLOG_INFO("EDIT titantrons: {} EVRT sets in {}", ids.size(), path.string());
    return ids;
  }
  REXLOG_WARN("EDIT titantrons: evt_eff.pac not found, EDIT chars keep EVRT/0100");
  return ids;
}

bool HasEvrtSet(int id) {
  static const std::unordered_set<int> ids = LoadEvrtIds();
  return ids.count(id) != 0;
}

}  // namespace

// After li r4,100 at 0x822C71D0 / 0x822C72EC / 0x822C7354: r11 = the entrant's char id.
// EN-6L: EDIT1 (char 100) must keep RR's EVRT/0100 (the default set of every EDIT char), so
// its own set uses the new id 130 (no RR char or EVRT set uses it).
constexpr int kEdit1EvrtSet = 130;

void RRXX_EditTitantron(PPCRegister& r4, PPCRegister& r11) {
  const int id = int(r11.u32);
  const int set = id == 100 ? kEdit1EvrtSet : id;
  if (id < 100 || id > 129 || !HasEvrtSet(set)) {
    return;
  }
  r4.u64 = uint64_t(set);
  static std::mutex mutex;
  static std::unordered_set<int> logged;
  std::lock_guard<std::mutex> lock(mutex);
  if (logged.insert(id).second) {
    REXLOG_INFO("EDIT titantron: char {} -> EVRT/{:04d}", id, set);
  }
}

// LETH-BLACK (2026-10-13): move / motion-track / camera diagnostics, `log_moves` (Debug, off by default).
REXCVAR_DECLARE(bool, log_moves);

namespace {

uint64_t LogMovesMs() {
  static const auto start = std::chrono::steady_clock::now();
  return uint64_t(std::chrono::duration_cast<std::chrono::milliseconds>(
                      std::chrono::steady_clock::now() - start).count());
}

uint8_t GuestU8(uint32_t a) { return IsLikelyGuestPointer(a) ? GuestBase()[a] : 0; }

// Same key within 1.5 s is logged once (lookups can repeat every frame).
bool LogMovesFresh(uint64_t key) {
  static std::mutex mutex;
  static std::unordered_map<uint64_t, uint64_t> last;
  std::lock_guard<std::mutex> lock(mutex);
  const uint64_t now = LogMovesMs();
  auto it = last.find(key);
  if (it != last.end() && now - it->second < 1500) {
    return false;
  }
  last[key] = now;
  return true;
}

// Once per run (log_moves): guest image 0x82000000-0x82AA0000 -> rrxx_image.bin next to the exe (static RE of data).
void DumpGuestImage() {
  static std::atomic_bool done{false};
  if (done.exchange(true)) {
    return;
  }
  std::ofstream out("rrxx_image.bin", std::ios::binary);
  out.write(reinterpret_cast<const char*>(GuestBase() + 0x82000000u), 0xAA0000);
  REXLOG_INFO("LB guest image dumped to rrxx_image.bin");
}

void LogMoveLookup(uint32_t key, uint32_t found) {
  if (REXCVAR_GET(log_moves)) {
    DumpGuestImage();
  }
  const uint32_t act = key >> 16;
  if (!REXCVAR_GET(log_moves) || act < 10000) {
    return;
  }
  if (LogMovesFresh((uint64_t(key) << 1) | (found ? 1 : 0))) {
    REXLOG_INFO("LB {} lookup act {} var {} track {} -> {}", LogMovesMs(), act, (key >> 8) & 0xFF,
                key & 0xFF, found ? "hit" : "MISS");
  }
}

}  // namespace

void LogMovesDumpImage() {
  if (REXCVAR_GET(log_moves)) {
    DumpGuestImage();
  }
}

// sub_820E07C0 (motion entry lookup): r9 = act<<16 | var<<8 | track.
void RRXX_LogLookupHit(PPCRegister& r9) { LogMoveLookup(r9.u32, 1); }
void RRXX_LogLookupMiss(PPCRegister& r9) { LogMoveLookup(r9.u32, 0); }

// sub_821B5C10 entry: a wrestler object starts a move (obj +184 act, +188 var, +76 partner).
void RRXX_LogMoveStart(PPCRegister& r3) {
  if (!REXCVAR_GET(log_moves)) {
    return;
  }
  const uint32_t o = r3.u32;
  const uint32_t partner = GuestU32(o + 76);
  REXLOG_INFO("LB {} start obj {:08X} act {} var {} f1056 {:08X} b386 {} b387 {} f408 {:08X}{:08X} "
              "partner {:08X} act {} var {} cam mode {}",
              LogMovesMs(), o, GuestU32(o + 184), GuestU32(o + 188), GuestU32(o + 1056), GuestU8(o + 386),
              GuestU8(o + 387), GuestU32(o + 408), GuestU32(o + 412), partner,
              partner ? GuestU32(partner + 184) : 0, partner ? GuestU32(partner + 188) : 0,
              GuestU32(GuestU32(0x82A8B0A4) + 556));
}

// sub_821D3BD8 entry: camera director (0x82A8B0A4) switches mode r4 (param r5).
void RRXX_LogCameraMode(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5) {
  if (!REXCVAR_GET(log_moves)) {
    return;
  }
  REXLOG_INFO("LB {} camera {:08X} mode {} -> {} param {:08X}", LogMovesMs(), r3.u32, GuestU32(r3.u32 + 556),
              r4.u32, r5.u32);
}

// VIC-CAM (2026-10-13): hold camera diagnostics (`log_moves`). sub_821E48C0 entry, r3 = camera (+356 mode, 5 = hold
// camera from event `39`, 6 = free cam). Logs the latched look-at (+400), the smoothed one (+368), the pad offset
// (+384), the eye (+336) and both wrestlers' `root` bone (char+84 -> matrix, +48 translation) and base (+240).
namespace {
float GuestF32(uint32_t a) {
  const uint32_t v = GuestU32(a);
  float f;
  std::memcpy(&f, &v, 4);
  return f;
}
// ASH-SINK (2026-10-13): per-frame placement of wrestlers in the spear acts (`log_moves`, called from RRXX_LogSinkApply
// = per-frame motion apply; the first version sat in the hold-camera hook and never ran). "SK" lines every 3rd frame and on act/var change: base (+0xF0) and base quat (+0xE0), world
// root (*(+0x100)), model position (+0x44 -> +0x50), root bone (+84 -> +48), attach mode (+0x24), scale (+0x174),
// side/area bytes (+0x180/+0x182), state flags (+408), motion frame (+0x18).
void LogSinkProbe(uint32_t sara) {
  static std::mutex mutex;
  static uint32_t last_ch[8], last_act[8], last_var[8], calls[8];
  std::lock_guard<std::mutex> lock(mutex);
  const uint32_t ch = GuestU32(sara + 0x20);
  const uint32_t act = ch ? GuestU32(ch + 184) : 0;
  if (act != 9550 && act != 9551 && act != 9542 && act != 9521) {
    return;
  }
  int i = 0;
  while (i < 7 && last_ch[i] && last_ch[i] != ch) {
    ++i;
  }
  last_ch[i] = ch;
  {
    const uint32_t var = GuestU32(ch + 188);
    const bool fresh = act != last_act[i] || var != last_var[i];
    calls[i] = fresh ? 0 : calls[i] + 1;
    last_act[i] = act;
    last_var[i] = var;
    if (calls[i] % 3 != 0) {
      return;
    }
    const uint32_t w = GuestU32(ch + 0x100), m = GuestU32(ch + 84), mdl = GuestU32(ch + 0x44), mc = GuestU32(sara + 0x1C);
    REXLOG_INFO("SK {} char {} {:08X} act {} var {} trk {} f {:.1f} base ({:.2f} {:.2f} {:.2f}) q ({:.3f} {:.3f} {:.3f} "
                "{:.3f}) world ({:.2f} {:.2f} {:.2f}) model ({:.2f} {:.2f} {:.2f}) rootbone ({:.2f} {:.2f} {:.2f}) "
                "att {:08X} scale {:.3f} side {} area {} f408 {:08X}{:08X} partner {:08X} sara {:08X} n {} T0 ({:.2f} {:.2f} "
                "{:.2f}) key ({:.2f} {:.2f} {:.2f}) from ({:.2f} {:.2f} {:.2f})",
                LogMovesMs(), i, ch, act, var, GuestU32(ch + 0xC0), GuestF32(ch + 0x18), GuestF32(ch + 0xF0),
                GuestF32(ch + 0xF4), GuestF32(ch + 0xF8), GuestF32(ch + 0xE0), GuestF32(ch + 0xE4), GuestF32(ch + 0xE8),
                GuestF32(ch + 0xEC), GuestF32(w), GuestF32(w + 4), GuestF32(w + 8), GuestF32(mdl + 0x50),
                GuestF32(mdl + 0x54), GuestF32(mdl + 0x58), GuestF32(m + 48), GuestF32(m + 52), GuestF32(m + 56),
                GuestU32(ch + 0x24), GuestF32(ch + 0x174), GuestU8(ch + 0x180), GuestU8(ch + 0x182),
                GuestU32(ch + 408), GuestU32(ch + 412), GuestU32(ch + 76), sara, GuestU32(sara + 0x24), GuestF32(mc + 0x10),
                GuestF32(mc + 0x14), GuestF32(mc + 0x18), GuestF32(mc + 0x40), GuestF32(mc + 0x44), GuestF32(mc + 0x48),
                GuestF32(mc + 0xA0), GuestF32(mc + 0xA4), GuestF32(mc + 0xA8));
  }
}
}  // namespace

// ASH-SINK 2: sub_821E7CC8 entry = per-frame motion apply, r3 = motion (SARA) object, +0x20 = wrestler, +0x1C = state.
void RRXX_LogSinkApply(PPCRegister& r3) {
  if (REXCVAR_GET(log_moves)) {
    LogSinkProbe(r3.u32);
  }
}

void RRXX_LogHoldCam(PPCRegister& r3) {
  if (!REXCVAR_GET(log_moves)) {
    return;
  }
  const uint32_t c = r3.u32;
  const uint32_t mode = GuestU32(c + 356);
  if (mode != 5 && mode != 6) {
    return;
  }
  static std::mutex mutex;
  static uint32_t calls = 0, last_cam = 0, last_mode = 0;
  std::lock_guard<std::mutex> lock(mutex);
  const float init = GuestF32(c + 412);
  const bool fresh = c != last_cam || mode != last_mode || init == 0.0f;
  calls = fresh ? 0 : calls + 1;
  last_cam = c;
  last_mode = mode;
  if (calls > 1 && calls % 30 != 0) {
    return;
  }
  REXLOG_INFO("VC {} cam {:08X} mode {} init {} free {} live {} act0 {} idx {} {} tgt ({:.2f} {:.2f} {:.2f}) cur ({:.2f} "
              "{:.2f} {:.2f}) off ({:.2f} {:.2f} {:.2f}) eye ({:.2f} {:.2f} {:.2f}) dist {:.2f}",
              LogMovesMs(), c, mode, init, GuestU32(c + 416), GuestU32(c + 280), GuestU32(c + 284),
              int32_t(GuestU32(c + 288)), int32_t(GuestU32(c + 292)), GuestF32(c + 400), GuestF32(c + 404),
              GuestF32(c + 408), GuestF32(c + 368), GuestF32(c + 372), GuestF32(c + 376), GuestF32(c + 384),
              GuestF32(c + 388), GuestF32(c + 392), GuestF32(c + 336), GuestF32(c + 340), GuestF32(c + 344),
              GuestF32(c + 432));
  for (int i = 0; i < 2; ++i) {
    const int32_t idx = int32_t(GuestU32(c + 288 + 4 * i));
    if (idx < 0 || idx > 7) {
      continue;
    }
    const uint32_t ch = GuestU32(0x82720D8C + 4 * idx);
    if (!ch) {
      continue;
    }
    const uint32_t m = GuestU32(ch + 84);
    REXLOG_INFO("VC   char {} {:08X} act {} sub {} frame {:.1f} f408 {:08X} root ({:.2f} {:.2f} {:.2f}) base ({:.2f} "
                "{:.2f} {:.2f}) yaw {:.3f} koshi ({:.2f} {:.2f} {:.2f}) l_toe ({:.2f} {:.2f} {:.2f})",
                idx, ch, GuestU32(ch + 184), GuestU32(ch + 188), GuestF32(ch + 192), GuestU32(ch + 408),
                GuestF32(m + 48), GuestF32(m + 52), GuestF32(m + 56), GuestF32(ch + 240), GuestF32(ch + 244),
                GuestF32(ch + 248), GuestF32(ch + 228), GuestF32(GuestU32(ch + 88) + 48),
                GuestF32(GuestU32(ch + 88) + 52), GuestF32(GuestU32(ch + 88) + 56),
                GuestF32(GuestU32(ch + 148) + 48), GuestF32(GuestU32(ch + 148) + 52),
                GuestF32(GuestU32(ch + 148) + 56));
  }
}

// Strike combo windows for imported acts (AUD-8b).
// sub_821B1040(act) looks the act up in a fixed 49-entry table at 0x82013FC8 ({u16 act,
// u8 0, u8 frame}) and returns frame * 2, or 0 when the act is missing; sub_821BF140 only
// plays the next COMBO/COMBOD hit at that frame, so a neutral strike (DAGEKI0) that is not in
// the table never chains. Missing acts get a frame from the list below, then from
// <mods_root>/moves/combo_windows.txt ("act frame" per line, # comments; same units as the
// table = motion frame index).
namespace {
// Imported neutral strikes of the mod pack's 30 created wrestlers (2026-10-16): window =
// hit-end event 0x5e + 3 frames (RR median).
constexpr std::pair<uint16_t, uint8_t> kComboWindowDefaults[] = {
    {4028, 14}, {4037, 16}, {4040, 12}, {4045, 10}, {4057, 12}, {4060, 15}, {4063, 16},
};

const std::unordered_map<uint32_t, uint32_t>& ComboWindows() {
  static const std::unordered_map<uint32_t, uint32_t> windows = [] {
    std::unordered_map<uint32_t, uint32_t> map;
    for (const auto& [act, frame] : kComboWindowDefaults) {
      map[act] = frame;
    }
    int count = 0;
    const std::string root = rex::cvar::GetFlagByName("mods_root");
    if (!root.empty()) {
      std::ifstream file(rex::to_path(root) / "moves" / "combo_windows.txt");
      std::string line;
      while (file && std::getline(file, line)) {
        if (line.empty() || line[0] == '#') {
          continue;
        }
        unsigned act = 0, frame = 0;
        if (std::sscanf(line.c_str(), "%u %u", &act, &frame) == 2 && act && frame && frame < 128) {
          map[act] = frame;
          ++count;
        }
      }
    }
    REXLOG_INFO("combo windows: {} extra acts ({} from moves/combo_windows.txt)", map.size(), count);
    return map;
  }();
  return windows;
}
}  // namespace

// sub_821B1040 table miss (li r3,0; r3 = act). True = return frame * 2 like a table hit.
bool RRXX_ComboWindow(PPCRegister& r3) {
  const auto& windows = ComboWindows();
  const auto it = windows.find(r3.u32 & 0xFFFF);
  if (it == windows.end()) {
    return false;
  }
  r3.u64 = it->second * 2;
  return true;
}
