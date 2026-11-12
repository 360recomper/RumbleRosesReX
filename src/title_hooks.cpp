// RumbleRosesReX - title-specific runtime hooks

#include "generated/default/rumble_roses_xx_init.h"

#include <rex/cvar.h>
#include <rex/filesystem.h>
#include <rex/memory.h>
#include <rex/system/kernel_state.h>
#include <rex/system/thread_state.h>

#include <algorithm>
#include <chrono>
#include <cmath>
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
#include <thread>
#include <type_traits>
#include <vector>

namespace {

std::atomic_bool g_warned_null_cleanup_slot{false};
std::atomic_bool g_warned_invalid_cleanup_object{false};
std::atomic_bool g_warned_invalid_cleanup_owner{false};

constexpr uint32_t kMinimumGuestPointer = 0x10000;

// Upper bound leaves room for the small field offsets the hooks add (no wrap past 4 GiB).
constexpr uint32_t kMaximumGuestPointer = 0xFFFF0000;

bool IsLikelyGuestPointer(uint32_t address) {
  return address >= kMinimumGuestPointer && address < kMaximumGuestPointer;
}

// R7-FIX-EXE #4: the data addresses below (game data, heap table, stage id, music manager) are
// fixed to the base XEX. A different default.xex (title update) keeps the game running but turns
// off every hook that writes guest data (cheats, heaps, stages, music); one log line says so.
constexpr uint64_t kBaseXexFnv1a = 0xDBC7BBD7F5A11762ull;  // assets/default.xex, 5124096 bytes
std::atomic_bool g_xex_matches{true};

bool XexMatches() {
  return g_xex_matches.load(std::memory_order_relaxed);
}

}  // namespace

void RRXX_CheckXexImage(const std::filesystem::path& xex_host) {
  std::ifstream in(xex_host, std::ios::binary);
  if (!in) {
    REXLOG_WARN("XEX check: cannot read {}, data hooks stay on", xex_host.string());
    return;
  }
  uint64_t hash = 0xCBF29CE484222325ull;
  char buffer[1 << 16];
  while (in.read(buffer, sizeof(buffer)) || in.gcount() > 0) {
    for (std::streamsize i = 0; i < in.gcount(); ++i) {
      hash = (hash ^ uint8_t(buffer[i])) * 0x100000001B3ull;
    }
  }
  if (hash == kBaseXexFnv1a) {
    REXLOG_INFO("XEX check: base default.xex ({:016X}), data hooks on", hash);
    return;
  }
  g_xex_matches = false;
  REXLOG_ERROR("XEX check: default.xex hash {:016X} is not the base XEX ({:016X}, title update?): "
               "cheats, heap, stage and music hooks are off", hash, kBaseXexFnv1a);
}

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

// R7-E13-CRASH: the render thread replays queued Resolve packets (type 42, sub_8216A2D0 ->
// sub_82070F48 = D3DDevice::Resolve). At the VS screen one is sometimes queued with a null
// destination texture (r6) and the read of +0x24 crashes. Returns true to skip that Resolve.
bool RRXX_SkipNullResolve(PPCRegister& r6) {
  if (r6.u32 != 0) {
    return false;
  }
  static std::atomic<uint32_t> skipped{0};
  uint32_t n = ++skipped;
  if (n <= 8 || (n & (n - 1)) == 0) {
    REXLOG_WARN("Resolve with null dest texture skipped (#{})", n);
  }
  return true;
}

// Cheats. sub_8216DFA0 returns the static game data object (the save file is a
// raw copy of it, so save offsets == object offsets). It runs on the game thread
// right before the game reads its data, so patching here avoids races.
REXCVAR_DECLARE(bool, unlock_all);
REXCVAR_DECLARE(bool, infinite_money);

namespace {

constexpr uint32_t kGameData = 0x8275AF78;
constexpr uint32_t kMoneyBase = kGameData + 0x33A28;       // BE u32 RR cash, slot 0
constexpr uint32_t kMoneyStride = 0x2F00;
constexpr uint32_t kMoneySlots = 55;
constexpr uint32_t kCurrentSlot = kGameData + 0x33798;     // BE u32 current profile slot
constexpr uint32_t kMoneyMax = 9999999;
constexpr uint32_t kPersonas = kGameData + 0x29565;        // u8[20], 3 = unlocked
constexpr uint32_t kLadyXFlags = kGameData + 0x31188;      // bit0 = Lady X (personas 20/21)
constexpr uint32_t kItemsBegin = kGameData + 0x311A0;      // 0x20-byte shop item records
constexpr uint32_t kItemsEnd = kGameData + 0x32680;
constexpr uint32_t kItemOwnedAll = 0x001FFFFF;             // +0x08 owned mask
constexpr uint32_t kItemTable = kGameData + 0x31180;       // row 0 of the item records
constexpr uint32_t kSaleRow = 58;                          // on-sale masks, one row per profile slot

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
  if ((!unlock && !money) || !XexMatches()) {
    return;
  }
  uint8_t* base = rex::system::kernel_state()->memory()->virtual_membase();

  if (money) {
    // Money is per profile slot (sub_820EDEB8); top up every slot so the shop
    // is full whichever slot is current.
    for (uint32_t slot = 0; slot < kMoneySlots; ++slot) {
      uint8_t* p = base + kMoneyBase + slot * kMoneyStride;
      const uint32_t old = LoadBE32(p);
      if (old >= kMoneyMax) {
        continue;
      }
      static int money_logs = 0;
      if (money_logs < 8) {
        ++money_logs;
        REXLOG_INFO("RRXX infinite_money: slot {} (current {}) money {} -> {}", slot,
                    LoadBE32(base + kCurrentSlot), old, kMoneyMax);
      }
      StoreBE32(p, kMoneyMax);
    }
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
      if (owned != 0 && (owned & kItemOwnedAll) != kItemOwnedAll) {
        StoreBE32(r + 0x08, owned | kItemOwnedAll);  // keep bought swimsuit bits (items 21+)
        StoreBE32(r + 0x18, 0xFFFFFFFF);
        StoreBE32(r + 0x1C, 0xFFFFFFFF);
      }
    }
    // Shop swimsuit page (sub_820F79D0): items 21,25..97 show "???" unless their bit is set in
    // the on-sale mask of row 58+slot (u32[4] at +0x08). Row 113+slot = sold, left alone.
    for (uint32_t slot = 0; slot < kMoneySlots; ++slot) {
      uint8_t* mask = base + kItemTable + (kSaleRow + slot) * 0x20 + 0x08;
      StoreBE32(mask, LoadBE32(mask) | 0xFFE00000);
      StoreBE32(mask + 4, 0xFFFFFFFF);
      StoreBE32(mask + 8, 0xFFFFFFFF);
      StoreBE32(mask + 12, 0xFFFFFFFF);
    }
  }
}

// Cut content / debug (research notes).
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

// Created wrestlers ("Lambda"), research notes.
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

constexpr int kLambdaColours = 4;  // select-screen colour slots (loader +20)

struct LambdaOverride {
  int32_t costume_id = 0;  // 0 = none
  bool keep_head = false;  // "+head": outfit only, Lambda keeps her face and hair
  bool head_only = false;  // ":head": face and hair only, Customize outfit stays
};

// One EDIT slot: an override per colour ("N=A/B/C/D"; "N=ID" = all colours).
struct LambdaSlotOverride {
  LambdaOverride colour[kLambdaColours];
};

std::string Trim(const std::string& s) {
  const size_t b = s.find_first_not_of(" 	");
  const size_t e = s.find_last_not_of(" 	");
  return b == std::string::npos ? std::string() : s.substr(b, e - b + 1);
}

// One colour entry: "480", "480+head" or "480:head". False if not a costume id.
bool ParseLambdaColour(std::string item, LambdaOverride* entry) {
  *entry = {};
  const size_t plus = item.find('+');
  if (plus != std::string::npos) {
    entry->keep_head = Trim(item.substr(plus + 1)) == "head";
    item = Trim(item.substr(0, plus));
  }
  const size_t colon = item.find(':');
  if (colon != std::string::npos) {
    entry->head_only = Trim(item.substr(colon + 1)) == "head";
    item = Trim(item.substr(0, colon));
  }
  const int id = std::atoi(item.c_str());
  if (id < 200 || id > 999) {
    *entry = {};
    return false;
  }
  entry->costume_id = id;
  return true;
}

// Re-parsed only when the setting text changes (it is hot-reloadable).
// "3=480, 5=481+head, 1=622/630/0/632:head": a missing, 0 or bad colour entry
// falls back to colour 1.
LambdaOverride GetLambdaOverride(uint32_t lambda_index, uint32_t colour) {
  static std::mutex mutex;
  static std::string parsed_text;
  static LambdaSlotOverride table[kLambdaSlots];
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
      const std::string item = Trim(text.substr(pos, comma - pos));
      pos = comma + 1;
      if (item.empty()) {
        continue;
      }
      const size_t eq = item.find('=');
      const int slot = eq == std::string::npos ? 0 : std::atoi(item.substr(0, eq).c_str());
      LambdaSlotOverride entry;
      int count = 0;
      if (eq != std::string::npos) {
        const std::string ids = item.substr(eq + 1);
        size_t start = 0;
        while (count < kLambdaColours && start <= ids.size()) {
          size_t slash = ids.find('/', start);
          if (slash == std::string::npos) {
            slash = ids.size();
          }
          const std::string one = Trim(ids.substr(start, slash - start));
          start = slash + 1;
          if (!ParseLambdaColour(one, &entry.colour[count]) && one != "0" && !one.empty()) {
            REXLOG_WARN("lambda_model_overrides: EDIT{} colour {}: ignoring \"{}\"", slot,
                        count + 1, one);
          }
          ++count;
        }
      }
      if (slot < 1 || slot > kLambdaSlots || entry.colour[0].costume_id == 0) {
        REXLOG_WARN(
            "lambda_model_overrides: ignoring \"{}\" (want EDIT 1-30 = costume id 200-999, "
            "or EDIT=A/B/C/D per colour)",
            item);
        continue;
      }
      for (int c = 1; c < kLambdaColours; ++c) {
        if (entry.colour[c].costume_id == 0) {
          entry.colour[c] = entry.colour[0];  // "N=ID" or a missing / 0 colour
        }
      }
      table[slot - 1] = entry;
      for (int c = 0; c < (count == 1 ? 1 : kLambdaColours); ++c) {
        const LambdaOverride& ov = entry.colour[c];
        REXLOG_INFO("lambda_model_overrides: EDIT{}{} -> costume {}{}", slot,
                    count == 1 ? std::string() : fmt::format(" colour {}", c + 1), ov.costume_id,
                    ov.keep_head   ? " (keeps Lambda head)"
                    : ov.head_only ? " (head only, keeps outfit)"
                                   : "");
      }
    }
  }
  if (lambda_index >= kLambdaSlots) {
    return LambdaOverride{};
  }
  return table[lambda_index].colour[colour < kLambdaColours ? colour : 0];
}

// Which character the composite being built belongs to: the select / Locker
// Room loader (sub_82180920 keeps itself in r21; +12 mode, +16 index) or the
// Customize builder G (record copy at G+8600: +0 mode, +4 index).
// Mode 0 = Lambda, 1 = built-in wrestler.
// The loader also keeps the select-screen colour (+20, picked code % 10);
// Customize always gets colour 0 (colour 1 attire).
bool FindPartOwner(uint32_t composite, uint32_t caller, uint32_t r21, uint32_t* mode,
                   uint32_t* index, uint32_t* colour) {
  if (caller >= kEditLoaderBegin && caller < kEditLoaderEnd && IsLikelyGuestPointer(r21)) {
    *mode = GuestU32(r21 + 12);
    *index = GuestU32(r21 + 16);
    *colour = GuestU32(r21 + 20);
    return true;
  }
  const uint32_t g = GuestU32(kCharBuilderPtr);
  if (g != 0 && composite == GuestU32(g + 4)) {
    *mode = GuestU32(g + 8600);
    *index = GuestU32(g + 8604);
    *colour = 0;
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
  uint32_t colour = 0;
  if (!FindPartOwner(r3.u32, r12.u32, r21.u32, &mode, &index, &colour) || mode != 0) {
    return false;
  }
  const LambdaOverride ov = GetLambdaOverride(index, colour);
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
    LogLambdaOnce(fmt::format("override EDIT{} colour={} slot={} -> {}", index + 1, colour + 1,
                              slot,
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

// Exhibition stage select (research notes section 10). The picker on the
// character-select screen (object = char select + 11752, cursor at +144) is built
// for 3 stages: sub_8214CAC8 maps the cursor to a stage (0, 1, 4), sub_8214CA80
// maps a stage back to the cursor, the input handler sub_8214D0B0 (on the +28
// list sub-object, cursor at +116) wraps at 2, and sub_8214CB80 creates the list
// with a count of 3. exhibition_stages replaces that fixed list. The card strip and
// stage names come from menu.pac MENU/CSLL, which must be built for the same list
// (RRXX-tools/arena/stage_menu.py).
REXCVAR_DECLARE(std::string, exhibition_stages);

namespace {

// Returns a copy under a lock: F4 hot-reload can change the cvar while the picker runs.
std::vector<int32_t> ExhibitionStages() {
  static std::mutex mutex;
  static std::string parsed_text;
  static std::vector<int32_t> stages;
  std::lock_guard<std::mutex> lock(mutex);
  if (!XexMatches()) {
    return {};
  }
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
  if (stages.empty() || !IsLikelyGuestPointer(r31.u32)) {
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
  if (stages.empty() || !IsLikelyGuestPointer(r31.u32)) {
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
  if (!XexMatches()) {
    return;
  }
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
REXCVAR_DECLARE(int32_t, default_heap_extra_mb);
REXCVAR_DECLARE(int32_t, heap3_trim_mb);

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
// R4-HEAP30-FREEZE: with default_heap_extra_mb the moved heap 30 gets its original size
// back on top, so the block leaves out most of the old heap 30 (it stays ~1 MiB) and that
// low memory is free for D3D overflow (RRXX_D3DAllocSpill).
constexpr uint32_t kHeap30Reclaim = 31u << 20;
uint32_t g_heap_block_shrink = 0;
// R7-MEM-ENGINE: heap 3 (28 MiB) held at most ~10 MiB (log_heaps: 7.4 MiB in the 10-16 FINAL
// session, 10.2 MiB + a refused 2 MiB request with 12 MiB in a select-sweep soak); heap3_trim_mb
// (default 8, heap 3 = 20 MiB) takes it off heap 3 and the block, so the block starts higher and
// the D3D spill area below it (RRXX_D3DAllocSpill) gets the memory.
constexpr uint32_t kTrimHeap = 3;
uint32_t g_heap_trim = 0;
uint32_t HeapBlockSize() {
  return kHeapBlockSize + g_heap_block_extra - g_heap_block_shrink - g_heap_trim;
}
uint32_t g_heap_block = 0;        // the block's address (RRXX_HeapBlockGot)
// The game's allocator table (sub_82062F48) gives D3D (ids 128/129) and id 0 the
// physical range 0x11300000-0x1FC00000; the moved default heap comes off its top.
constexpr uint32_t kD3DRangeLow = 0x11300000;
constexpr uint32_t kD3DRangeHigh = 0x1FC00000;
constexpr uint32_t kPhysView64K = 0xA0000000;  // 64 KiB page view of physical memory
constexpr uint32_t kMiscRangeLow = 0x10900000;  // XMemAlloc ids without their own range
// R7-MEM-ENGINE: the misc range (10 MiB, filled top-down) peaked at ~1.2 MiB in a two-diva match;
// D3D may spill into its bottom 6 MiB, the top 4 MiB stays for the misc ids (they have no spill).
constexpr uint32_t kMiscSpillHigh = 0x10F00000;
constexpr uint32_t kSpillLowPhys = 0x100000;    // keep physical 0 (= null to the GPU) unused
}  // namespace

// sub_82061828 after "lis r3,3488" (3rd sub_82065DC8 = MmAllocatePhysicalMemoryEx):
// r3 = size 0xDA00000, r4 = lowest physical address 0x2E00000, r5 = highest 0x10800000.
// The allocation is top-down, so a bigger size and a lower minimum grow the block down.
void RRXX_HeapBlockAlloc(PPCRegister& r3, PPCRegister& r4) {
  g_heap_block_extra = 0;
  g_heap_block_shrink = 0;
  g_heap_trim = 0;
  if (r3.u32 != kHeapBlockSize || !XexMatches()) {
    return;
  }
  const int32_t mib = REXCVAR_GET(heap_block_extra_mb);
  uint32_t extra = mib > 0 ? uint32_t(mib) << 20 : 0;
  if (extra && r4.u32 < kLowestBlockPhys + extra) {
    extra = r4.u32 > kLowestBlockPhys ? (r4.u32 - kLowestBlockPhys) & ~0xFFFFFu : 0;
    REXLOG_WARN("heap_block_extra_mb {}: limited to {} MiB", mib, extra >> 20);
  }
  g_heap_block_extra = extra;
  g_heap_block_shrink = REXCVAR_GET(default_heap_extra_mb) > 0 ? kHeap30Reclaim : 0;
  g_heap_trim = uint32_t(std::clamp(REXCVAR_GET(heap3_trim_mb), 0, 12)) << 20;  // heap 3 keeps >= 16 MiB
  r3.u64 = HeapBlockSize();
  r4.u64 = r4.u32 - extra;
}

// After "mr r31,r3": r31 = the heap block (0 = failed; the game stops before this).
void RRXX_HeapBlockGot(PPCRegister& r31) {
  g_heap_block = r31.u32;
  REXLOG_INFO("heap block at {:#010x}, {:#x} bytes (+{} MiB, -{} MiB old heap 30, -{} MiB heap {})",
              r31.u32, HeapBlockSize(), g_heap_block_extra >> 20, g_heap_block_shrink >> 20,
              g_heap_trim >> 20, kTrimHeap);
}

// After "lis r4,3488": r4 = bytes the game clears (sub_82307FE8) = whole block.
void RRXX_HeapBlockClear(PPCRegister& r4) {
  r4.u64 = r4.u32 + g_heap_block_extra - g_heap_block_shrink - g_heap_trim;
}

// sub_82061828 after "addi r5,r11,3000": r4 = block end (block + 0xDA00000, the game
// subtracts 512 next), r5 = heap size table.
void RRXX_HeapSizes(PPCRegister& r4, PPCRegister& r5) {
  if (!XexMatches()) {
    return;
  }
  const uint32_t extra = g_heap_block_extra;
  r4.u64 = r4.u32 + extra - g_heap_block_shrink - g_heap_trim;
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
  // With default_heap_extra_mb the block already leaves out kHeap30Reclaim of heap 30
  // (it stays ~1 MiB in the block), so the guard checks what is really left there.
  const uint32_t shrink_kib = g_heap_block_shrink / 1024;
  const uint32_t in_block_kib = default_kib > shrink_kib ? default_kib - shrink_kib : 0;
  if (mib > 0 && uint32_t(mib) * 1024 > want) {
    taken = uint32_t(mib) * 1024 - want;
    if (in_block_kib < taken + kMinDefaultHeapKiB) {
      REXLOG_WARN("mot_heap_mb {}: default heap would drop below 16 MiB, ignored", mib);
      taken = 0;
    }
  }
  want += taken;
  // The block left out g_heap_trim, so heap 3 must shrink by it too (else heap 30 loses it).
  const uint32_t trim_kib = g_heap_trim / 1024;
  if (want == kib[kMotHeap] && !trim_kib) {
    return;
  }
  kib[kMotHeap] = want;
  kib[kTrimHeap] -= trim_kib;
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
  REXLOG_INFO("heap sizes: MOT heap {} = {} KiB, heap {} = {} KiB, default heap = {} KiB (original {})",
              kMotHeap, want, kTrimHeap, kib[kTrimHeap], default_kib - taken, default_kib);
}

namespace {
// Largest run of unused guest physical memory, at 64 KiB resolution.
uint32_t PhysicalLargestFreeKiB() {
  auto* heap = rex::system::kernel_state()->memory()->GetPhysicalHeap();
  const uint32_t base = heap->heap_base();
  uint32_t run = 0, best = 0;
  for (uint32_t off = 0; off < heap->heap_size(); off += 0x10000) {
    rex::memory::HeapAllocationInfo info{};
    const bool used = heap->QueryRegionInfo(base + off, &info) && info.state != 0;
    run = used ? 0 : run + 64;
    best = std::max(best, run);
  }
  return best;
}

uint32_t PhysicalFreeMiB() {
  auto* memory = rex::system::kernel_state()->memory();
  return uint32_t((uint64_t(memory->GetPhysicalHeap()->GetUnreservedPageCount()) * 4096) >> 20);
}

// Used guest physical ranges, merged ("start-end" in MiB at 64 KiB resolution).
std::string PhysicalMap() {
  auto* heap = rex::system::kernel_state()->memory()->GetPhysicalHeap();
  const uint32_t base = heap->heap_base();
  const uint32_t size = heap->heap_size();
  std::string out;
  uint32_t run_start = 0;
  bool in_run = false;
  for (uint32_t off = 0; off < size; off += 0x10000) {
    rex::memory::HeapAllocationInfo info{};
    const bool used = heap->QueryRegionInfo(base + off, &info) && info.state != 0;
    if (used && !in_run) {
      run_start = off;
      in_run = true;
    } else if (!used && in_run) {
      out += fmt::format(" {:#x}-{:#x}", run_start, off);
      in_run = false;
    }
  }
  if (in_run) {
    out += fmt::format(" {:#x}-{:#x}", run_start, size);
  }
  return out;
}

// log_heaps: log the physical map whenever it changes (checked every 5 s).
void StartPhysicalMapLogger() {
  static std::atomic<bool> started{false};
  if (!REXCVAR_GET(log_heaps) || started.exchange(true)) {
    return;
  }
  std::thread([] {
    std::string last;
    for (;;) {
      std::string map = PhysicalMap();
      if (map != last) {
        REXLOG_INFO("guest physical: {} MiB free, used:{}", PhysicalFreeMiB(), map);
        last = std::move(map);
      }
      std::this_thread::sleep_for(std::chrono::seconds(5));
    }
  }).detach();
}
}  // namespace

namespace {
void WatchHeapEndPad(uint32_t end) {
  std::thread([end] {
    const uint8_t* pad = GuestBase() + end;
    for (;;) {
      std::this_thread::sleep_for(std::chrono::seconds(5));
      const uint32_t w0 = LoadBE32(pad), w1 = LoadBE32(pad + 4), w2 = LoadBE32(pad + 8);
      if (w0 || w1 || w2) {
        REXLOG_INFO("heap 30 end pad written at {:#010x}: {:#x} {:#x} {:#x} (the write the pad "
                    "absorbs; without it this hit the page above)", end, w0, w1, w2);
        return;
      }
    }
  }).detach();
}
}  // namespace

// R3-CRASH-HEAP30: heap 30 (default heap) is the remainder of the block, ~32 MiB, too
// small once HD outfits load (Christy 632 waist.tex alone = 13.4 MB). The block cannot
// grow more (24 MiB down to the GPU writeback area), so default_heap_extra_mb moves heap
// 30 to its own physical block of original + extra, taken top-down from the D3D range.
// sub_82090318 at loc_8209055C, after carving heaps 0-29: r23 = heap 30 start (after the
// descriptor table at the block start), r30 = its end. The game then writes heap 30's
// descriptor and free list from r23/r30, so only these two change. The old ~32 MiB stays
// unused inside the block. Nothing reads the block end the carver stores (0x8270C6FC).
// R4-HEAP30-FREEZE: the heap writes one word just past its end. Free (sub_8208FFC8,
// merge with a free previous block) and realign (sub_8208FCC0) store the previous index
// in word 1 of the "next" record without checking that next is the end marker, so
// freeing the heap's last block writes end+4. In the original layout that is heap 29's
// unused header word (heap 0 keeps 512 bytes of slack for it). The moved heap ended at
// phys 0x1FC00000, right below a page allocated at boot (top of the D3D range): that
// write froze the game after load. So the moved heap keeps a 64 KiB pad of its own above
// its end, and a watcher logs once when the game writes there (proof of the cause).
void RRXX_DefaultHeapMove(PPCRegister& r23, PPCRegister& r30) {
  StartPhysicalMapLogger();
  const int32_t mib = REXCVAR_GET(default_heap_extra_mb);
  if (mib <= 0 || !XexMatches() || !g_heap_block || r23.u32 < g_heap_block || r30.u32 <= r23.u32 ||
      r30.u32 > g_heap_block + HeapBlockSize()) {
    return;
  }
  // The block left out kHeap30Reclaim of the old heap 30; the moved heap gets it back.
  const uint32_t orig = (r30.u32 - r23.u32 + g_heap_block_shrink) & ~0xFFFFu;
  constexpr uint32_t kHeapEndPad = 0x10000;  // owns the word the heap writes past its end
  auto* memory = rex::system::kernel_state()->memory();
  auto* heap = memory->LookupHeapByType(true, 64 * 1024);
  const uint32_t protect = rex::memory::kMemoryProtectRead | rex::memory::kMemoryProtectWrite;
  const uint32_t type =
      rex::memory::kMemoryAllocationReserve | rex::memory::kMemoryAllocationCommit;
  // Ask for the full extra; if the range is short, step down 16 MiB at a time.
  for (uint32_t extra = uint32_t(mib) << 20; extra; extra = extra > (16u << 20) ? extra - (16u << 20) : 0) {
    uint32_t address = 0;
    if (heap->AllocRange(kPhysView64K + kD3DRangeLow, kPhysView64K + kD3DRangeHigh - 1,
                         orig + extra + kHeapEndPad, 64 * 1024, type, protect, true,
                         &address)) {
      REXLOG_INFO("default heap 30 moved to {:#010x}: {} KiB + 64 KiB end pad (original {} KiB, "
                  "+{} MiB); guest physical free now {} MiB", address, (orig + extra) / 1024,
                  orig / 1024, extra >> 20, PhysicalFreeMiB());
      if (extra != uint32_t(mib) << 20) {
        REXLOG_WARN("default_heap_extra_mb {}: limited to {} MiB", mib, extra >> 20);
      }
      r23.u64 = address;
      r30.u64 = address + orig + extra;
      WatchHeapEndPad(address + orig + extra);
      return;
    }
  }
  REXLOG_ERROR("default_heap_extra_mb {}: no physical room, heap 30 stays {} KiB", mib,
               (r30.u32 - r23.u32) / 1024);
}

// D3D overflow. sub_82063188 = the game's physical XMemAlloc: MmAllocatePhysicalMemoryEx
// (sub_82065DC8) in the id's range from the table at 0x8270AA30; D3D ids 128/129 and id 0
// get 0x11300000-0x1FC00000. On failure it only probes free memory (sub_82062F78) and
// returns 0, and the game reads through the null (R4 Sable vs Christy crash, the moved
// heap 30 sits in that range). The spill retries a failed D3D-range request below the
// heap block (GPU writeback ends at 0x1000000), where the reclaimed old heap 30 now is.
namespace {
struct PhysAllocArgs {
  uint32_t size, min, max, align, protect;
};
thread_local PhysAllocArgs t_phys_alloc{};
}  // namespace

// 0x820631C0, before "bl 0x82065dc8": r3 size, r4/r5 physical range, r6 alignment, r7 flags.
void RRXX_D3DAllocArgs(PPCRegister& r3, PPCRegister& r4, PPCRegister& r5, PPCRegister& r6,
                       PPCRegister& r7) {
  t_phys_alloc = {r3.u32, r4.u32, r5.u32, r6.u32, r7.u32};
}

// 0x820631E8, the failure path: true = r3/r29 hold a spilled block, continue at the success
// tail 0x820631D0 (its accounting uses r28/r30/r31, untouched).
bool RRXX_D3DAllocSpill(PPCRegister& r3, PPCRegister& r29) {
  const PhysAllocArgs a = t_phys_alloc;
  if (a.min != kD3DRangeLow || !g_heap_block || !a.size) {
    return false;
  }
  uint32_t page = 4096;
  if (a.protect & 0x20000000u) {  // X_MEM_LARGE_PAGES
    page = 64 * 1024;
  } else if (a.protect & 0x80000000u) {  // X_MEM_16MB_PAGES
    page = 16 * 1024 * 1024;
  }
  uint32_t protect = 0;
  if (a.protect & 0x2u) {  // X_PAGE_READONLY
    protect = rex::memory::kMemoryProtectRead;
  } else if (a.protect & 0x4u) {  // X_PAGE_READWRITE
    protect = rex::memory::kMemoryProtectRead | rex::memory::kMemoryProtectWrite;
  }
  if (a.protect & 0x200u) {  // X_PAGE_NOCACHE
    protect |= rex::memory::kMemoryProtectNoCache;
  }
  if (a.protect & 0x400u) {  // X_PAGE_WRITECOMBINE
    protect |= rex::memory::kMemoryProtectWriteCombine;
  }
  auto* heap = rex::system::kernel_state()->memory()->LookupHeapByType(true, page);
  const uint32_t view = heap->heap_base() & 0xE0000000u;  // physical views map phys 0 here
  const uint32_t block_phys = g_heap_block & 0x1FFFFFFFu;
  const uint32_t align = std::max((a.align + page - 1) & ~(page - 1), page);
  const uint32_t size = (a.size + page - 1) & ~(page - 1);
  // Free physical memory outside the D3D range: below the heap block (the reclaimed old
  // heap 30; pages the SDK holds there are skipped) and the gap above it up to the misc
  // XMemAlloc range and on into that range's unused bottom (kMiscSpillHigh).
  const uint32_t areas[2][2] = {{kSpillLowPhys, block_phys},
                                {block_phys + HeapBlockSize(), kMiscSpillHigh}};
  uint32_t address = 0;
  bool ok = false;
  for (const auto& area : areas) {
    if (!ok && area[1] > area[0] + size) {
      ok = heap->AllocRange(view + area[0], view + area[1] - 1, size, align,
                            rex::memory::kMemoryAllocationReserve |
                                rex::memory::kMemoryAllocationCommit,
                            protect, true, &address);
    }
  }
  static std::atomic<uint32_t> spills{0}, spilled_kib{0};
  if (ok) {
    const uint32_t n = spills.fetch_add(1) + 1;
    const uint32_t kib = spilled_kib.fetch_add(size / 1024) + size / 1024;
    if (n <= 20 || n % 100 == 0) {
      REXLOG_INFO("D3D alloc spill #{}: {} KiB at {:#010x} (D3D range full); {} KiB spilled in "
                  "all, guest physical free {} MiB", n, a.size / 1024, address, kib,
                  PhysicalFreeMiB());
    }
  } else {
    // Always logged: the game crashes right after this.
    REXLOG_ERROR("D3D alloc of {} KiB (flags {:#x}, align {:#x}) failed in the D3D range and the "
                 "spill areas; guest physical free {} MiB, largest free run {} KiB, {} spills "
                 "before", a.size / 1024, a.protect, a.align, PhysicalFreeMiB(),
                 PhysicalLargestFreeKiB(), spills.load());
  }
  if (!ok) {
    return false;
  }
  r3.u64 = address;
  r29.u64 = address;
  return true;
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
  REXLOG_INFO("heaps free:{} | guest physical free {} MiB", line, PhysicalFreeMiB());
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

// Extra entrance tracks (MU-4, research notes). sub_820858A0(name, &found)
// scans a fixed 63-entry table at 0x820057E8 (20 bytes: bank name, cue name, title,
// artist, album) for the cue name built from the music number: "ENTRANCE_%03d", or
// "MENU_%03d" with number - 46 for numbers >= 61 (61/62 = MENU_015/016). The player
// then asks XACT for the cue by name. Numbers 63+ get an entry here pointing at cue
// ENTRANCE_NNN in bank MUSIC_ENTRANCE; RRXX-tools/audio/add_tracks.py adds those cues
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
// line (MU-4d; written by RRXX-tools/audio/add_tracks.py --titles). Missing = "Custom Track N".
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
  if (!XexMatches() || !IsLikelyGuestPointer(r3.u32)) {
    return false;
  }
  const char* name = reinterpret_cast<const char*>(GuestBase() + r3.u32);
  const int track = ExtraTrackFromName(name);
  if (track < 0) {
    return false;
  }
  const uint32_t entry = ExtraTrackEntry(track);
  if (!entry || !IsLikelyGuestPointer(r4.u32)) {
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

// R7-TOPROPE-CORNER-c: set while selftest_kind = down re-lays the foe (every frame), keeps LB/TRC lines out of the log.
static bool g_selftest_relay = false;
static bool g_selftest_relay_seen = false;  // a re-lay ran this session: skip the 0 <-> 20 trk lines

// sub_821B5C10 entry: a wrestler object starts a move (obj +184 act, +188 var, +76 partner).
void RRXX_LogMoveStart(PPCRegister& r3) {
  if (!REXCVAR_GET(log_moves) || g_selftest_relay) {
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
namespace {
void TopRopeTrack(uint32_t sara);
}  // namespace

void RRXX_LogSinkApply(PPCRegister& r3) {
  if (REXCVAR_GET(log_moves)) {
    LogSinkProbe(r3.u32);
    TopRopeTrack(r3.u32);
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

// Strike combo windows for imported acts (AUD-8b, research notes "AUD-8").
// sub_821B1040(act) looks the act up in a fixed 49-entry table at 0x82013FC8 ({u16 act,
// u8 0, u8 frame}) and returns frame * 2, or 0 when the act is missing; sub_821BF140 only
// plays the next COMBO/COMBOD hit at that frame, so a neutral strike (DAGEKI0) that is not in
// the table never chains. Missing acts get a frame from the list below, then from
// <mods_root>/moves/combo_windows.txt ("act frame" per line, # comments; same units as the
// table = motion frame index; written by RRXX-tools/moves/fullset/combo_check.py --emit).
namespace {
// Imported neutral strikes of the 30 EDIT divas (combo_check.py, 2026-10-16): window =
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

// R3-TRS (2026-10-25): top-rope Lethal vs a standing foe (act ids 6500-6599). sub_8219D080's 65xx branch only takes a
// groggy foe (sub_821A7900), so in play it never fires. After that call (r3 = result, r31 = attacker, foe at +1112):
// also accept a free standing foe in the ring (stance 3000-3499, walk 2000-2097, guard 8500-8539, dash 50/51, steps
// 82-89). The stock 10.0 < distance < 40.0 window still follows. Logs each rejected foe act once.
void RRXX_TopRopeStandingLethal(PPCRegister& r3, PPCRegister& r31) {
  if (r3.u32 != 0) {
    return;
  }
  const uint32_t foe = GuestU32(r31.u32 + 1112);
  if (!foe || GuestU8(foe + 386) != 0) {
    return;
  }
  const uint32_t act = GuestU32(foe + 184);
  const bool standing = (act >= 3000 && act <= 3499) || (act >= 2000 && act <= 2097) ||
                        (act >= 8500 && act <= 8539) || act == 50 || act == 51 || (act >= 82 && act <= 89);
  if (standing) {
    r3.u64 = 1;
    return;
  }
  static std::mutex mutex;
  static std::unordered_set<uint32_t> logged;
  std::lock_guard<std::mutex> lock(mutex);
  if (logged.insert(act).second) {
    REXLOG_INFO("top-rope standing Lethal: foe act {} not accepted", act);
  }
}

// V15-KICKOUT-b (2026-11-01): referee pin count sub_821AB8E8 (r31 referee: +0x30 frames, +0x34 count 0-3, +0x40 pinner
// slot in the wrestler table 0x82720D8C, +0x44 mode). Kick-out check at 0x821ABA4C: pinner +390 == 0, mode != 1 and the
// pinned wrestler's (pinner +76) +608 new-press flags != 0 ON the frame the count is 3 (or count 2 + a 3/256 roll), so a
// human mashing one button almost never hit it. Latch: any button newly pressed in the last pin_kickout_latch_ms counts.
REXCVAR_DECLARE(int32_t, pin_kickout_latch_ms);

namespace {

// sub_820974A0: component lookup by tag (children via +16, then the node itself, then siblings via +12).
uint32_t FindComponent(uint32_t node, uint32_t tag) {
  if (!IsLikelyGuestPointer(node)) {
    return 0;
  }
  for (uint32_t c = GuestU32(node + 16), n = 0; IsLikelyGuestPointer(c) && n < 64; c = GuestU32(c + 16), ++n) {
    if (GuestU32(c + 4) == tag) {
      return c;
    }
  }
  if (GuestU32(node + 4) == tag) {
    return node;
  }
  for (uint32_t c = GuestU32(node + 12), n = 0; IsLikelyGuestPointer(c) && n < 64; c = GuestU32(c + 12), ++n) {
    if (GuestU32(c + 4) == tag) {
      return c;
    }
  }
  return 0;
}

// Pinned wrestler's pad (pinned-act path 0x82199E74: component 'COPE' at +464, +20, +44): +288 now, +284 last frame.
uint32_t WrestlerPad(uint32_t w) {
  const uint32_t comp = FindComponent(w + 464, 0x434F5045);
  const uint32_t ctrl = comp ? GuestU32(comp + 20) : 0;
  const uint32_t pad = IsLikelyGuestPointer(ctrl) ? GuestU32(ctrl + 44) : 0;
  return IsLikelyGuestPointer(pad) ? pad : 0;
}

constexpr uint32_t kPadButtons = 0xFF000000;  // stock kick-out buttons = bits 24-26 (+608 |1/|4/|2)

std::mutex g_kickout_mutex;
std::unordered_map<uint32_t, uint64_t> g_kickout_press_ms;  // pinned wrestler -> last button press
uint64_t g_kickout_latched = 0;                             // log: presses the latch added

}  // namespace

void RRXX_PinRefLog(PPCRegister& r6, PPCRegister& r29, PPCRegister& r31) {
  if (!REXCVAR_GET(log_moves)) {
    return;
  }
  const uint32_t ref = r31.u32;
  const uint32_t pinner = GuestU32(r29.u32 + 4 * (GuestU32(ref + 64) & 7));
  const uint32_t foe = IsLikelyGuestPointer(pinner) ? GuestU32(pinner + 76) : 0;
  const uint32_t pad = IsLikelyGuestPointer(foe) ? WrestlerPad(foe) : 0;
  const uint32_t count = GuestU32(ref + 52), flags = GuestU32(foe + 608), word = pad ? GuestU32(pad + 288) : 0;
  const uint64_t key = (uint64_t(count) << 56) ^ (uint64_t(r6.u32 & 0xFF) << 48) ^ (uint64_t(GuestU8(pinner + 390)) << 40) ^
                       (uint64_t(flags & 0xFF) << 32) ^ (word & kPadButtons) ^ (uint64_t(foe) << 8);
  static uint64_t last = ~0ull;
  if (key == last) {
    return;
  }
  last = key;
  REXLOG_INFO("PIN {} ref {:08X} frames {} count {} mode {} pinner {:08X} act {} +390 {} f408 {:016X} foe {:08X} act {} "
              "+608 {} pad {:08X} latched {}",
              LogMovesMs(), ref, GuestU32(ref + 48), count, r6.s32, pinner, GuestU32(pinner + 184), GuestU8(pinner + 390),
              (uint64_t(GuestU32(pinner + 408)) << 32) | GuestU32(pinner + 412), foe, GuestU32(foe + 184), flags, word,
              g_kickout_latched);
}

void RRXX_PinKickoutLatch(PPCRegister& r6, PPCRegister& r7, PPCRegister& r31) {
  const int32_t window = REXCVAR_GET(pin_kickout_latch_ms);
  if (window <= 0) {
    return;
  }
  const uint32_t foe = r7.u32;
  const uint32_t pad = WrestlerPad(foe);
  const bool pressed = r6.u32 != 0 ||
                       (pad && (GuestU32(pad + 288) & ~GuestU32(pad + 284) & kPadButtons) != 0);
  const uint64_t now = LogMovesMs();
  std::lock_guard<std::mutex> lock(g_kickout_mutex);
  uint64_t& last = g_kickout_press_ms[foe];
  if (pressed) {
    last = now;
  }
  if (r6.u32 == 0 && last && now - last <= uint64_t(window)) {
    const int32_t count = int32_t(GuestU32(r31.u32 + 52));
    if (count >= 2) {
      r6.u64 = 1;  // only the count 2/3 checks read it
      if (count == 3) {
        ++g_kickout_latched;
      }
    }
  }
}

// R3-FACECAM-HOOK (2026-10-25): the in-match camera (mode 0, sub_8225F9B0, r31 = camera) puts its distance at +780 =
// 3.1 x 16 x merged wrestler scale (+372), so an entrant scale below 1 frames chest/face in every shot. At
// loc_82260188 (before the distance is read) clamp +780 to >= match_cam_min_dist (0 = stock). `log_moves`: each live
// wrestler's act and scale (wrestler array 0x82720D8C, 8 slots) whenever the scale set changes, with the distance.
REXCVAR_DECLARE(double, match_cam_min_dist);

void RRXX_MatchCamMinDist(PPCRegister& r31) {
  const uint32_t cam = r31.u32;
  if (!IsLikelyGuestPointer(cam)) {
    return;
  }
  uint8_t* p = rex::system::kernel_state()->memory()->virtual_membase() + cam + 780;
  const uint32_t raw = LoadBE32(p);
  float dist;
  std::memcpy(&dist, &raw, 4);
  const float min_dist = float(REXCVAR_GET(match_cam_min_dist));
  const bool clamp = min_dist > 0.0f && dist < min_dist;
  if (clamp) {
    uint32_t v;
    std::memcpy(&v, &min_dist, 4);
    StoreBE32(p, v);
  }
  if (!REXCVAR_GET(log_moves)) {
    return;
  }
  constexpr uint32_t kWrestlers = 0x82720D8C;
  static std::mutex mutex;
  static std::string last;
  std::string scales;
  for (uint32_t i = 0; i < 8; ++i) {
    const uint32_t w = GuestU32(kWrestlers + 4 * i);
    if (w) {
      scales += fmt::format(" w{} {:08X} scale {:.3f}", i, w, GuestF32(w + 372));
    }
  }
  std::lock_guard<std::mutex> lock(mutex);
  if (scales == last) {
    return;
  }
  last = scales;
  REXLOG_INFO("MATCHCAM {} dist {:.2f}{} min {:.2f}{}", LogMovesMs(), dist, clamp ? " clamped" : "", min_dist, scales);
}

// R4-GOROUND-INGAME (2026-10-26): sub_82198AB0 (Lethal act start, r3 attacker, r4 foe, r5 act) sends 6500-6898 down the
// one-person dive path: only the attacker plays the act, the foe is put in act 0 var 3 (lying) = the foe drops at once.
// Our 65xx Lethals are built as 69xx two-person acts (RR 6902 layout), so route 6500-6599 to the 69xx start at
// 0x82198D6C (sub_821B4BF0 plays both tracks) and do its 69xx-only store: attacker+244 = float at 0x82007E14.
bool RRXX_TopRopeStandingStart(PPCRegister& r27, PPCRegister& r30) {
  const uint32_t act = r27.u32;
  if (act < 6500 || act > 6599 || !IsLikelyGuestPointer(r30.u32)) {
    return false;
  }
  uint8_t* base = GuestBase();
  StoreBE32(base + r30.u32 + 244, LoadBE32(base + 0x82007E14u));
  const uint32_t foe = GuestU32(r30.u32 + 1112);
  REXLOG_INFO("top-rope standing Lethal: act {} two-person start, foe act {}", act, foe ? GuestU32(foe + 184) : 0);
  return true;
}

// R7-TOPROPE-CORNER probe (2026-10-27, `log_moves`): top-rope Lethals (65xx/69xx) jump both wrestlers to one fixed
// corner. Log attacker (r30) + foe (r31) base (+240), yaw (+228), act/sub/frame and bytes 384-386 at the two-person
// start (loc_82198DE4, before sub_821B4BF0) and right after it (0x82198E04); TopRopeTrack logs the next frames.
namespace {
bool IsTopRopeTraceAct(uint32_t act) {
  return (act >= 6500 && act <= 6999) || (act >= 20400 && act <= 20499) || (act >= 150 && act <= 159);
}

void LogTopRopeChar(const char* tag, const char* who, uint32_t ch) {
  if (!IsLikelyGuestPointer(ch)) {
    return;
  }
  const uint32_t m = GuestU32(ch + 84);
  REXLOG_INFO("TRC {} {} {} {:08X} act {} sub {} frame {:.1f} base ({:.2f} {:.2f} {:.2f}) yaw {:.3f} root ({:.2f} "
              "{:.2f} {:.2f}) b384 {} b385 {} b386 {}",
              LogMovesMs(), tag, who, ch, GuestU32(ch + 184), GuestU32(ch + 188), GuestF32(ch + 192),
              GuestF32(ch + 240), GuestF32(ch + 244), GuestF32(ch + 248), GuestF32(ch + 228), GuestF32(m + 48),
              GuestF32(m + 52), GuestF32(m + 56), GuestU8(ch + 384), GuestU8(ch + 385), GuestU8(ch + 386));
}

void LogTopRopeStart(const char* tag, uint32_t act, uint32_t att, uint32_t foe) {
  if (!REXCVAR_GET(log_moves) || act < 6500 || act > 6999) {
    return;
  }
  REXLOG_INFO("TRC {} {} two-person start act {}", LogMovesMs(), tag, act);
  LogTopRopeChar(tag, "att", att);
  LogTopRopeChar(tag, "foe", foe);
}

// Per-frame (R7-TOPROPE-CORNER-b: from RRXX_SelfTestAct = pad update of the human wrestler; sub_821E7CC8 never runs):
// log the wrestler + its foe on every act change (any act, to see the corner-climb path) and, while in a corner /
// Lethal intro / top-rope act, on sub change and every 4th frame.
void TopRopeTrackChar(uint32_t ch) {
  if (!IsLikelyGuestPointer(ch)) {
    return;
  }
  static std::mutex mutex;
  static std::unordered_map<uint32_t, uint64_t> last;
  const uint32_t act = GuestU32(ch + 184);
  const int32_t step = (act >= 20400 && act <= 20499) ? 1 : 4;  // -c: every frame of the Lethal intro (base restore)
  const uint64_t key = (uint64_t(act) << 32) | (uint64_t(GuestU32(ch + 188) & 0xFF) << 24) |
                       uint64_t(int32_t(GuestF32(ch + 192)) / step & 0xFFFFFF);
  {
    std::lock_guard<std::mutex> lock(mutex);
    auto& k = last[ch];
    const uint32_t prev = uint32_t(k >> 32);
    if (k == key || (prev == act && !IsTopRopeTraceAct(act)) ||
        ((act == 0 || act == 20) && (prev == 0 || prev == 20) && g_selftest_relay_seen)) {  // -c re-lay loop
      return;
    }
    k = key;
  }
  LogTopRopeChar("trk", "chr", ch);
}

void TopRopeTrack(uint32_t sara) {
  if (IsLikelyGuestPointer(sara)) {
    TopRopeTrackChar(GuestU32(sara + 0x20));
  }
}
}  // namespace

// R7-TOPROPE-CORNER-c fix (2026-10-28): during the Lethal intro 20403 the attacker's base reads (0,0,0) and its root is
// the absolute intro spot (4.98, 5.45), so the engine's corner index (+385, refreshed from the position) becomes 2 = the
// (+x,+z) post every time. loc_82198DE4 then copies it to the foe and the 69xx starts both at that post (Ivory/Molly
// "teleport to one fixed corner"). Base is still the real post here: recompute +385 from its quadrant first
// (from logs: (+,+) 2, (+,-) 3, (-,+) 1, (-,-) 0; +384 stays right).
void RRXX_TopRopeCornerBefore(PPCRegister& r27, PPCRegister& r30, PPCRegister& r31) {
  const uint32_t act = r27.u32, att = r30.u32;
  if (act >= 6900 && act <= 6999 && IsLikelyGuestPointer(att)) {
    const float x = GuestF32(att + 240), z = GuestF32(att + 248);
    if (std::fabs(x) >= 20.0f && std::fabs(z) >= 20.0f) {  // on a corner post (+-28.8)
      const uint8_t corner = x > 0.0f ? (z > 0.0f ? 2 : 3) : (z > 0.0f ? 1 : 0);
      uint8_t* b = GuestBase() + att + 385;
      if (*b != corner) {
        if (REXCVAR_GET(log_moves)) {
          REXLOG_INFO("TRC top-rope Lethal {} corner byte {} -> {} (att base {:.2f} {:.2f})", act, *b, corner, x, z);
        }
        *b = corner;
      }
    }
  }
  LogTopRopeStart("before", r27.u32, r30.u32, r31.u32);
}

void RRXX_TopRopeCornerAfter(PPCRegister& r27, PPCRegister& r30, PPCRegister& r31) {
  LogTopRopeStart("after", r27.u32, r30.u32, r31.u32);
}

// R5-TORRIE-DOG-b (2026-10-26): entrance props (SOBJ 2040/2042/2049) are not drawn. The only '/SOBJ/%04d' path is
// sub_821DE958 <- sub_82157060 (object pool, r4 = request: u16 id +0, u8 slot +2). `log_sobj` logs each request and
// whether the SOBJ was found (0 = missing); a stock prop (e.g. 2116) shows what a working request looks like.
REXCVAR_DECLARE(bool, log_sobj);

static uint32_t LoadBE16(const uint8_t* p) {
  return (uint32_t(p[0]) << 8) | p[1];
}

// POST-REL-PROPS-2P: event op 0x72 NN attaches an object to participant NN (r8 = table[NN], about to go to state+76;
// r9 = NN*4, r18 = table 0x82720D8C, r27 = event object). Solo entrance data says `72 00`, but a diva entering as 2P is
// participant 1, so her prop stuck to 1P. A prop's participant 0 in a solo entrance now means the entrant.
// Playing event id: captured at its EVNT load (RRXX_EventStart). Queue (sub_821A0FC0) = manager+0x100+16*i, 8 entries:
// u16 id, +4 participant, +8 partner (-1 = solo). The manager pointer is only live while entrances are being set up, so
// RRXX_EntranceQueuePick keeps it. Events not in the queue (not an entrance) are left alone.
// Two entries with one id (same entrance on both sides): the second load of that id is the second entrant.
static std::atomic<uint32_t> g_event_id{0xFFFF};
static std::atomic<uint32_t> g_event_seq{0};
static std::atomic<uint32_t> g_entrance_mgr{0};

void RRXX_EntranceQueuePick(PPCRegister& r3, PPCRegister& r4) {
  g_entrance_mgr.store(r3.u32);
  if (r4.u32 == 0) {
    g_event_id.store(0xFFFF);
    g_event_seq.store(0);
  }
}

void RRXX_EventStart(PPCRegister& r3) {
  const uint32_t id = r3.u32 & 0xFFFF;
  if (g_event_id.exchange(id) == id) {
    g_event_seq.fetch_add(1);
  } else {
    g_event_seq.store(0);
  }
}

void RRXX_PropParticipant(PPCRegister& r8, PPCRegister& r9, PPCRegister& r18, PPCRegister& r20, PPCRegister& r27) {
  uint8_t* base = GuestBase();
  const uint32_t nn = r9.u32 / 4;
  const uint32_t objchr = IsLikelyGuestPointer(r27.u32) ? LoadBE32(base + r27.u32 + 32) : 1;
  const uint32_t id = g_event_id.load();
  const uint32_t seq = g_event_seq.load();
  const uint32_t mgr = g_entrance_mgr.load();
  uint32_t who = 0xFFFFFFFF, partner = 0, hits = 0;
  if (IsLikelyGuestPointer(mgr) && id != 0xFFFF) {
    for (uint32_t i = 0; i < 8; ++i) {
      const uint32_t e = mgr + 0x100 + 16 * i;
      if (LoadBE16(base + e) == id) {
        if (hits <= seq) {
          who = LoadBE32(base + e + 4);
          partner = LoadBE32(base + e + 8);
        }
        ++hits;
      }
    }
  }
  uint32_t out = r8.u32;
  if (nn == 0 && objchr == 0 && who < 8 && partner == 0xFFFFFFFF && LoadBE32(base + r20.u32 - 844) == 6) {
    const uint32_t h = LoadBE32(base + r18.u32 + who * 4);
    if (IsLikelyGuestPointer(h)) {
      out = h;
    }
  }
  if (REXCVAR_GET(log_sobj)) {
    REXLOG_INFO("PROP72 nn {} in {:08X} out {:08X} obj {:08X} objchr {:08X} id {} seq {} hits {} who {} partner {:08X}",
                nn, r8.u32, out, r27.u32, objchr, id, seq, hits, who, partner);
  }
  r8.u64 = out;
}

void RRXX_LogSobjRequest(PPCRegister& r4) {
  if (!REXCVAR_GET(log_sobj) || !IsLikelyGuestPointer(r4.u32)) {
    return;
  }
  uint8_t* base = GuestBase();
  REXLOG_INFO("SOBJ request id {} slot {}", LoadBE16(base + r4.u32), base[r4.u32 + 2]);
}

void RRXX_LogSobjResult(PPCRegister& r3, PPCRegister& r27) {
  if (!REXCVAR_GET(log_sobj) || !IsLikelyGuestPointer(r27.u32)) {
    return;
  }
  uint8_t* base = GuestBase();
  REXLOG_INFO("SOBJ lookup id {} -> {:08X}{}", LoadBE16(base + r27.u32), r3.u32, r3.u32 ? "" : " (not found)");
}

// R6-PROP-HOOK2 (2026-10-26): entrance props never pass sub_82157060. sub_82092538(path, out) is the archive lookup
// behind every '/SOBJ/%04d', 'EVTS/%d' and '/EVNT/%d' path; log those paths and what the lookup returned.
static thread_local char t_lookup_path[64];

void RRXX_LogPathLookup(PPCRegister& r3) {
  t_lookup_path[0] = 0;
  if (!REXCVAR_GET(log_sobj) || !IsLikelyGuestPointer(r3.u32)) {
    return;
  }
  const char* path = reinterpret_cast<const char*>(GuestBase() + r3.u32);
  size_t n = 0;
  while (n < sizeof(t_lookup_path) - 1 && path[n] >= 0x20 && path[n] < 0x7F) {
    t_lookup_path[n] = path[n];
    ++n;
  }
  t_lookup_path[n] = 0;
  if (!strstr(t_lookup_path, "SOBJ") && !strstr(t_lookup_path, "EVTS") && !strstr(t_lookup_path, "EVNT")) {
    t_lookup_path[0] = 0;
  }
}

void RRXX_LogPathResult(PPCRegister& r3) {
  if (!t_lookup_path[0]) {
    return;
  }
  REXLOG_INFO("path lookup {} -> {:08X}{}", t_lookup_path, r3.u32, r3.u32 ? "" : " (not found)");
  t_lookup_path[0] = 0;
}

// R6-REMATCH-BGM (2026-10-26): match BGM = MUSIC_MATCH playlist on music player mgr+108 (mgr = *0x8270C638), loaded by
// music scene 4 and started by scene 7, both only from scene "A"; a Rematch builds only the match flow (sub_822BCFA0
// -> match scene), so after match 1's scene 8 stop only the crowd plays. In sub_822BCFA0 after bl sub_821B5258
// (r3 != 0 = rematch) call sub_820828B8(mgr, 4, 0) and (mgr, 7, 0). r3 is ctx.r3, so the guest context sits around it;
// it is saved and restored whole so the following cmpwi sees the original r3.
// R7-MUSIC-BGM (2026-10-27): the 0x82A8A6B0 flag also stays set after "New characters" (entrances play, BGM then ran
// over them). A real Rematch loads no entrance event, so also require the current event (sub_821A09B0 =
// *(*(*0x82A8A31C + 8) + 12), what scene 20 checks) to be null.
// R7-FIX-EXE #5: RematchBGM and SelfTestAct rebuild the whole guest context from &r3. That is only
// right while the codegen passes ctx.r3 by reference ("RRXX_X(ctx.r3)"); a copy would make the cast
// point into the host stack. Trust it only when &r3 is the r3 of this thread's own context.
static_assert(std::is_same_v<decltype(PPCContext::r3), PPCRegister>, "hooks take PPCRegister r3");
PPCContext* ContextFromR3(PPCRegister& r3, const char* hook) {
  auto* ts = rex::runtime::ThreadState::Get();
  PPCContext* ctx = ts ? ts->context() : nullptr;
  if (ctx && &ctx->r3 == &r3) {
    return ctx;
  }
  static std::atomic_bool warned{false};
  if (!warned.exchange(true)) {
    REXLOG_ERROR("{}: r3 is not this thread's context register (codegen change?), hook skipped", hook);
  }
  return nullptr;
}

REXCVAR_DECLARE(bool, log_music_scenes);
// R7-MUSIC-BGM-b (2026-10-30, self-test trace): the results-screen Rematch never reaches sub_822BCFA0 (no scene 20/14).
// Match 1 ends with scene 8 (stop player 108, release the match banks); the Rematch flow sub_822BCB70 then calls only
// scene 7 (play 108) -> nothing loaded, silence. Match 1 ran 4 (MATCH playlist on 108), 5 (load the banks), 7.
// Hook at that scene-7 call (0x822BCBAC, r3 = mgr): when the last scene (mgr+276) is 8, run scene 4 first. Scene 5
// (banks) is left out: its reload on the Rematch failed a 9 MiB heap-27 alloc and crashed (self-test r7bgmb-2).
void RRXX_RematchBGM(PPCRegister& r3) {
  if (!XexMatches()) {
    return;
  }
  const uint32_t mgr = r3.u32;
  if (!IsLikelyGuestPointer(mgr) || GuestU32(mgr + 276) != 8) {
    return;
  }
  PPCContext* ctx_ptr = ContextFromR3(r3, "RRXX_RematchBGM");
  if (!ctx_ptr) {
    return;
  }
  PPCContext& ctx = *ctx_ptr;
  uint8_t* base = GuestBase();
  const PPCContext saved = ctx;
  ctx.r3.u64 = mgr;
  ctx.r4.u64 = 4;
  ctx.r5.u64 = 0;
  sub_820828B8(ctx, base);
  ctx = saved;
  REXLOG_INFO("rematch: match BGM playlist reloaded (music scene 4 before 7)");
}

// R7-MUSIC-BGM-b: log_music_scenes traces the music scene switch sub_820828B8(mgr, scene, 0) at its entry
// (scene, the last scene at mgr+276, caller = lr) to find what the results-screen Rematch runs.
void RRXX_LogMusicScene(PPCRegister& r3, PPCRegister& r4) {
  if (!REXCVAR_GET(log_music_scenes) || !XexMatches()) {
    return;
  }
  auto* ts = rex::runtime::ThreadState::Get();
  PPCContext* ctx = ts ? ts->context() : nullptr;
  const uint32_t lr = (ctx && &ctx->r3 == &r3) ? static_cast<uint32_t>(ctx->lr) : 0;
  const uint32_t mgr = r3.u32;
  const uint32_t last = IsLikelyGuestPointer(mgr) ? GuestU32(mgr + 276) : 0xFFFFFFFF;
  const uint32_t flag = GuestU32(0x82A8A6B0);
  // BGM player mgr+108, state +28 (sub_8218E438; 3 = playing, what scene 7 checks).
  const uint32_t bgm = IsLikelyGuestPointer(mgr) ? GuestU32(mgr + 108) : 0;
  const int32_t bgm_state = IsLikelyGuestPointer(bgm) ? static_cast<int32_t>(GuestU32(bgm + 28)) : -1;
  REXLOG_INFO("music scene {} (last {}) from {:08X} mgr {:08X} rematch flag {:08X} bgm state {}", r4.s32,
              static_cast<int32_t>(last), lr, mgr, flag, bgm_state);
}

// R7-MUSIC-BGM-c: log_music_scenes also logs every music-player play (sub_8218E540 at loc_8218E5F0, r31 = player):
// state 3 is set there even when the cue lookup failed (r5 = 0xFFFF, handle +4 stays 0) = "playing" but silent.
void RRXX_LogMusicPlay(PPCRegister& r3, PPCRegister& r5, PPCRegister& r31) {
  if (!REXCVAR_GET(log_music_scenes) || !XexMatches() || !IsLikelyGuestPointer(r31.u32)) {
    return;
  }
  const uint32_t p = r31.u32;
  const uint32_t list = GuestU32(p + 12);
  const uint32_t idx = GuestU32(p + 20);
  const uint32_t entry = list + idx * 20;
  auto str = [](uint32_t a) -> std::string {
    if (!IsLikelyGuestPointer(a)) return "?";
    const char* c = reinterpret_cast<const char*>(GuestBase() + a);
    return std::string(c, strnlen(c, 40));
  };
  const bool ok = IsLikelyGuestPointer(list);
  REXLOG_INFO("music play: player {:08X} entry {}/{} {}:{} cue idx {:04X} r3 {:08X} handle {:08X} mode {} state {}",
              p, idx, GuestU32(p + 16), ok ? str(GuestU32(entry)) : "?", ok ? str(GuestU32(entry + 4)) : "?",
              r5.u32 & 0xFFFF, r3.u32, GuestU32(p + 4), GuestU32(p + 32), static_cast<int32_t>(GuestU32(p + 28)));
}

// R7-MUSIC-BGM-d (2026-11-01): cue handles are slots in the bank's cue-instance array (stride 0x78) and get reused.
// In r7bgmd-1 every Rematch whose BGM got slot A5C7E3B4 (an earlier entrance cue's slot) was stopped ~22 s in by
// someone else holding that slot, with no music scene in between = the silent rematch. Guard at the entry of cue Stop
// (sub_821A3508) and cue Destroy (sub_821A2AA8), r12 = caller: a call on the current match-BGM handle (player
// *(*0x8270C638 + 108), handle +4) is skipped unless it comes from a music-player function (0x8218E2C8-0x8218E7D0)
// working on that BGM player (r31 at entry = the caller's player). The BGM player's own stop/advance still runs.
static bool GuardBgmCue(const char* what, uint32_t handle, uint32_t caller, uint32_t r31) {
  if (!XexMatches() || handle == 0) {
    return false;
  }
  const uint32_t mgr = GuestU32(0x8270C638);
  if (!IsLikelyGuestPointer(mgr)) {
    return false;
  }
  const uint32_t bgm = GuestU32(mgr + 108);
  if (!IsLikelyGuestPointer(bgm) || GuestU32(bgm + 4) != handle) {
    return false;
  }
  const bool own = caller >= 0x8218E2C8 && caller < 0x8218E7D0 && r31 == bgm;
  if (own) {
    if (REXCVAR_GET(log_music_scenes)) {
      REXLOG_INFO("music cue {} on BGM handle {:08X} from {:08X} (BGM player, allowed)", what, handle, caller);
    }
    return false;
  }
  REXLOG_INFO("music cue {} on BGM handle {:08X} from {:08X} r31 {:08X} skipped (stale owner)", what, handle, caller,
              r31);
  return true;
}

bool RRXX_GuardBgmCueStop(PPCRegister& r3, PPCRegister& r12, PPCRegister& r31) {
  if (!GuardBgmCue("stop", r3.u32, r12.u32, r31.u32)) {
    return false;
  }
  r3.u64 = 0;
  return true;
}

bool RRXX_GuardBgmCueDestroy(PPCRegister& r3, PPCRegister& r12, PPCRegister& r31) {
  if (!GuardBgmCue("destroy", r3.u32, r12.u32, r31.u32)) {
    return false;
  }
  r3.u64 = 0;
  return true;
}

// R7-SELFTEST-ACT (2026-10-27): play any act in game without a controller (dev exe, selftest_act = 0 = off).
// sub_821C2E40 = pad controller update (r3 = controller, wrestler = +20; it calls the Lethal trigger sub_8219D080 with
// that wrestler, whose foe is +1112). P1 = the first wrestler seen there after a 3 s gap (= new match); its first live
// frame (+408 top bit, the same gate the update uses) starts the clock. Starts go through the engine's own paths:
// lethal = sub_82198AB0(att, foe, act) (what the Lethal task calls after the 20403 intro: 65xx-68xx dive, 69xx / corner /
// ropes two-person), two = sub_821B4BF0(att, foe, act) (grapple start), solo = sub_820D50A8(att, act, 0, 0, 0.0).
// A due start waits until neither wrestler is linked to a partner (+76). Each start logs "selftest: start ...".
REXCVAR_DECLARE(int32_t, selftest_act);
REXCVAR_DECLARE(double, selftest_delay_s);
REXCVAR_DECLARE(double, selftest_repeat_s);
REXCVAR_DECLARE(bool, selftest_foe_down);
REXCVAR_DECLARE(std::string, selftest_kind);
REXCVAR_DECLARE(double, selftest_down_near);
REXCVAR_DECLARE(int32_t, selftest_down_act);
REXCVAR_DECLARE(int32_t, selftest_corner);
REXCVAR_DECLARE(double, selftest_run_gap);
// infinite_special (same hook, human wrestlers only): finishing gauge kept full. fill code before loc_821B29E8: +756 =
// partial (stock at 100), +748 = stocks (each Killer/Lethal/H-move does -1, Lethal trigger needs > 0), +760 = shown bars (cap 4).
REXCVAR_DECLARE(bool, infinite_special);
// R7-BETH-GS-HOLD-f: per-frame pair log. char+84+4i = model matrix of joint i of the 33-name list at 0x82495138
// (root, koshi, mune, ..), +48 = world translation. "PAIRN" names the joints once; "PAIR" = one line per wrestler.
REXCVAR_DECLARE(int32_t, log_pair_act);
namespace {
void LogPairChar(const char* who, uint32_t ch, uint64_t frame_no) {
  if (!IsLikelyGuestPointer(ch)) {
    return;
  }
  std::string j;
  for (uint32_t i = 0; i < 33; ++i) {
    const uint32_t m = GuestU32(ch + 84 + 4 * i);
    if (IsLikelyGuestPointer(m)) {
      j += fmt::format(" {:.2f},{:.2f},{:.2f}", GuestF32(m + 48), GuestF32(m + 52), GuestF32(m + 56));
    } else {
      j += " -";
    }
  }
  REXLOG_INFO("PAIR {} {} {:08X} act {} sub {} f {:.1f} base {:.2f},{:.2f},{:.2f} yaw {:.3f} J{}", frame_no, who, ch,
              GuestU32(ch + 184), GuestU32(ch + 188), GuestF32(ch + 192), GuestF32(ch + 240), GuestF32(ch + 244),
              GuestF32(ch + 248), GuestF32(ch + 228), j);
}

void LogPair(uint32_t att) {
  const int32_t lo = REXCVAR_GET(log_pair_act);
  if (lo <= 0 || !IsLikelyGuestPointer(att)) {
    return;
  }
  const uint32_t foe = GuestU32(att + 1112);
  auto in = [lo](uint32_t ch) {
    if (!IsLikelyGuestPointer(ch)) {
      return false;
    }
    const uint32_t a = GuestU32(ch + 184);
    return a >= uint32_t(lo) && a < uint32_t(lo) + 1000;
  };
  if (!in(att) && !in(foe)) {
    return;
  }
  static bool named = false;
  if (!named) {
    named = true;
    std::string n;
    for (uint32_t i = 0; i < 33; ++i) {
      const uint32_t s = GuestU32(0x82495138 + 4 * i);
      n += ' ';
      n += IsLikelyGuestPointer(s) ? std::string(reinterpret_cast<const char*>(GuestBase() + s)) : std::string("?");
    }
    REXLOG_INFO("PAIRN{}", n);
  }
  static uint64_t frame_no = 0;
  ++frame_no;
  LogPairChar("att", att, frame_no);
  LogPairChar("foe", foe, frame_no);
}
}  // namespace

void RRXX_SelfTestAct(PPCRegister& r3) {
  const uint32_t att = GuestU32(r3.u32 + 20);
  LogPair(att);
  if (att && REXCVAR_GET(log_moves)) {
    TopRopeTrackChar(att);
    TopRopeTrackChar(GuestU32(att + 1112));
  }
  if (!att || !(GuestU32(att + 408) & 0x80000000u)) {
    return;
  }
  if (REXCVAR_GET(infinite_special) && IsLikelyGuestPointer(att)) {
    if (int32_t(GuestU32(att + 748)) < 4) {
      StoreBE32(GuestBase() + att + 748, 4);
    }
    StoreBE32(GuestBase() + att + 760, 4);
  }
  const int32_t act = REXCVAR_GET(selftest_act);
  if (act <= 0) {
    return;
  }
  static uint32_t p1 = 0;
  static uint64_t last_ms = 0, next_ms = 0;
  static int count = 0;
  const uint64_t now = LogMovesMs();
  if (!p1 || now - last_ms > 3000) {
    p1 = att;
    count = 0;
    next_ms = now + uint64_t(REXCVAR_GET(selftest_delay_s) * 1000.0);
    REXLOG_INFO("selftest: match, P1 obj {:08X}, act {} in {:.1f} s", att, act, REXCVAR_GET(selftest_delay_s));
  }
  if (att != p1) {
    return;
  }
  last_ms = now;
  const uint32_t foe = GuestU32(att + 1112);
  const bool down_mode = REXCVAR_GET(selftest_kind) == "down";  // every frame after the delay, no repeat wait
  if ((now < next_ms && !(down_mode && count)) || !foe || GuestU32(att + 76) || GuestU32(foe + 76)) {
    return;
  }
  const double repeat = REXCVAR_GET(selftest_repeat_s);
  next_ms = repeat > 0.0 ? now + uint64_t(repeat * 1000.0) : UINT64_MAX;

  std::string kind = REXCVAR_GET(selftest_kind);
  if (kind != "lethal" && kind != "two" && kind != "solo" && kind != "down") {
    const bool leth = (act >= 6500 && act <= 6999) || (act >= 9500 && act <= 9999) || (act >= 12000 && act <= 18999);
    kind = leth ? "lethal" : "two";
  }
  PPCContext* ctx_ptr = ContextFromR3(r3, "RRXX_SelfTestAct");
  if (!ctx_ptr) {
    return;
  }
  PPCContext& ctx = *ctx_ptr;
  uint8_t* base = GuestBase();
  const PPCContext saved = ctx;
  const double zero = GuestF32(0x82000BAC);  // the 0.0 constant sub_82198AB0 passes to sub_820D50A8
  if (kind == "down") {  // R7-TOPROPE-CORNER-b: only keep the foe lying (real climb + Lethal input from the script)
    // R7-TOPROPE-CORNER-c: re-lay at the get-up's first frame (act 20) so the foe never stands; place it ONCE
    // `selftest_down_near` units toward the ring centre when P1 stands at a corner stand (+-25, +-25).
    static bool placed = false;
    static float hold[2];  // placed foe x/z: written back after each re-lay (the get-up's frame 0 root drifts ~0.02/f)
    const uint32_t fa = GuestU32(foe + 184);
    float ax = GuestF32(att + 240), az = GuestF32(att + 248);
    const int32_t corner = REXCVAR_GET(selftest_corner);
    const uint32_t p1_act = GuestU32(att + 184);
    if (corner > 0 && !placed && (p1_act == 3001 || p1_act < 20) && (std::fabs(ax) < 21.0f || std::fabs(az) < 21.0f)) {
      const float c[2] = {(corner == 1 || corner == 4) ? 25.0f : -25.0f, corner <= 2 ? 25.0f : -25.0f};
      const uint32_t m = GuestU32(att + 84);  // root node: base is rebuilt from it every frame, move both
      for (int j = 0; j < 2; ++j) {
        uint32_t v;
        std::memcpy(&v, &c[j], 4);
        StoreBE32(base + att + 240 + 8 * j, v);
        if (IsLikelyGuestPointer(m)) {
          StoreBE32(base + m + 48 + 8 * j, v);
        }
      }
      REXLOG_INFO("selftest: down P1 {:08X} act {} moved ({:.2f} {:.2f}) -> corner {} ({:.0f} {:.0f})", att, p1_act, ax, az,
                  corner, c[0], c[1]);
      ax = c[0];
      az = c[1];
    }
    const bool at_corner = std::fabs(ax) >= 21.0f && std::fabs(az) >= 21.0f;
    if (!at_corner && (std::fabs(ax) < 15.0f || std::fabs(az) < 15.0f)) {
      placed = false;  // P1 left the corner (e.g. after a Lethal): place again at the next corner
    }
    const double near_d = REXCVAR_GET(selftest_down_near);
    if (near_d > 0.0 && at_corner && !placed && (p1_act == 3001 || p1_act < 100)) {  // P1 idle/walk, not in a move
      placed = true;
      const float len = std::sqrt(ax * ax + az * az);
      const float k = float(near_d) / len;
      const float pos[2] = {ax - ax * k, az - az * k};
      hold[0] = pos[0];
      hold[1] = pos[1];
      for (int j = 0; j < 2; ++j) {
        uint32_t v;
        std::memcpy(&v, &pos[j], 4);
        StoreBE32(base + foe + 240 + 8 * j, v);
      }
      REXLOG_INFO("selftest: down place foe {:08X} at ({:.2f} {:.2f}), P1 act {} base ({:.2f} {:.2f} {:.2f}) yaw {:.3f}", foe,
                  pos[0], pos[1], GuestU32(att + 184), ax, GuestF32(att + 244), az, GuestF32(att + 228));
    }
    // Climb: with selftest_corner, once the foe is placed start P1's own climb chain (2452 -> 168 climb -> 153 wait on
    // top, as in the user's play log) facing the post; the scripted LB then fires the real Lethal from 153.
    static bool climbed = false;
    static uint64_t climb_ms = 0;
    if (!placed) {
      climbed = false;
    }
    if (climbed && climb_ms && now - climb_ms > 500) {  // 2452 did not lead into the climb: start 168 itself
      climb_ms = 0;
      if (p1_act != 168 && p1_act != 153 && p1_act != 2153 && p1_act != 154) {
        ctx.r3.u64 = att;
        ctx.r4.u64 = 168;
        ctx.r5.u64 = 0;
        ctx.r6.u64 = 0;
        ctx.f1.f64 = zero;
        sub_820D50A8(ctx, base);
        ctx = saved;
        REXLOG_INFO("selftest: down climb fallback P1 was act {} -> act 168 (now {}) at ({:.2f} {:.2f})", p1_act,
                    GuestU32(att + 184), ax, az);
      }
    }
    if (corner > 0 && placed && !climbed && (p1_act == 3001 || p1_act < 20)) {
      climbed = true;
      climb_ms = now;
      const float yaw = std::atan2(ax, az);  // facing the post (+-28.8, +-28.8)
      uint32_t v;
      std::memcpy(&v, &yaw, 4);
      StoreBE32(base + att + 228, v);
      ctx.r3.u64 = att;
      ctx.r4.u64 = 2452;
      ctx.r5.u64 = 0;
      ctx.r6.u64 = 0;
      ctx.f1.f64 = zero;
      sub_820D50A8(ctx, base);
      ctx = saved;
      REXLOG_INFO("selftest: down climb P1 {:08X} act 2452 at ({:.2f} {:.2f}) yaw {:.3f} -> now act {}", att, ax, az, yaw,
                  GuestU32(att + 184));
    }
    // CENA-TEST-c: selftest_down_act > 0 lays the foe as that act var 0 (1 = face-down) instead of act 0 var 3 (face-up)
    const uint32_t down_act = uint32_t(std::max(REXCVAR_GET(selftest_down_act), 0));
    if (fa == down_act) {  // lying (a linked two-person move already returned above)
      return;
    }
    ctx.r3.u64 = foe;
    ctx.r4.u64 = down_act;
    ctx.r5.u64 = down_act ? 0 : 3;
    ctx.r6.u64 = 0;
    ctx.f1.f64 = zero;
    g_selftest_relay = g_selftest_relay_seen = true;
    sub_820D50A8(ctx, base);
    g_selftest_relay = false;
    ctx = saved;
    if (placed) {
      for (int j = 0; j < 2; ++j) {
        uint32_t v;
        std::memcpy(&v, &hold[j], 4);
        StoreBE32(base + foe + 240 + 8 * j, v);
      }
    }
    if (++count % 200 == 1) {
      REXLOG_INFO("selftest: down #{} foe {:08X} (was act {}) att act {} base ({:.2f} {:.2f} {:.2f}) foe base ({:.2f} {:.2f})",
                  count, foe, fa, GuestU32(att + 184), ax, GuestF32(att + 244), az, GuestF32(foe + 240),
                  GuestF32(foe + 248));
    }
    return;
  }
  if (REXCVAR_GET(selftest_foe_down)) {
    ctx.r3.u64 = foe;
    ctx.r4.u64 = 0;
    ctx.r5.u64 = 3;
    ctx.r6.u64 = 0;
    ctx.f1.f64 = zero;
    sub_820D50A8(ctx, base);
  }
  if (kind == "solo") {
    ctx.r3.u64 = att;
    ctx.r4.u64 = uint32_t(act);
    ctx.r5.u64 = 0;
    ctx.r6.u64 = 0;
    ctx.f1.f64 = zero;
    sub_820D50A8(ctx, base);
  } else {
    ctx.r3.u64 = att;
    ctx.r4.u64 = foe;
    ctx.r5.u64 = uint32_t(act);
    const double gap = REXCVAR_GET(selftest_run_gap);
    // V15-ALICIA-DIST: gap < 0 = the NATURAL side (user 9589/9591 logs: foe at base - 22*(sin, cos)(att yaw), facing her);
    // gap > 0 = the R7-ALICIA-FIN-g side (only right for a move that runs +Z, which RR running Lethals do not).
    if (kind == "lethal" && act >= 9500 && act <= 9999 && gap != 0.0) {
      // R7-ALICIA-FIN-g: natural running Lethal = sub_8219D080 -> sub_8219B668(att, foe, act). The task (sub_8219ACC0)
      // saves att/foe +224..+255 (angles x,yaw,z + base) at the trigger; after the 20403 intro (sub_8219AF40,
      // 0x8219B4A4) it writes them back (+ base into [ch+256]) and calls sub_82199058 -> sub_82198AB0. So the start
      // spacing is where both stood at the trigger: foe ~21 units ahead along the move's travel, facing her (yaw + pi).
      // A standing P1's 95xx travels toward base + (sin, cos)(yaw) (run 1: root went that way); a running attacker's
      // +228 reads flipped (user 9589: foe at base - 22.9*(sin, cos)), same layout. Root-root gap at f0: 17.8 vs 18.6.
      // Foe kept inside the ropes (|x|, |z| <= 21): both are shifted toward the centre when needed.
      const float ay = GuestF32(att + 228);
      float apos[2] = {GuestF32(att + 240), GuestF32(att + 248)};
      float pos[2] = {apos[0] + float(gap) * std::sin(ay), apos[1] + float(gap) * std::cos(ay)};
      for (int j = 0; j < 2; ++j) {
        const float lim = 21.0f;
        const float d = pos[j] > lim ? lim - pos[j] : (pos[j] < -lim ? -lim - pos[j] : 0.0f);
        pos[j] += d;
        apos[j] += d;
      }
      float fy = ay + 3.14159265f;
      if (fy > 3.14159265f) {
        fy -= 6.2831853f;
      }
      auto put = [base](uint32_t ch, const float* xz) {
        const uint32_t m = GuestU32(ch + 84);  // root node: keep it on the base too
        for (int j = 0; j < 2; ++j) {
          uint32_t v;
          std::memcpy(&v, &xz[j], 4);
          StoreBE32(base + ch + 240 + 8 * j, v);
          if (IsLikelyGuestPointer(m)) {
            StoreBE32(base + m + 48 + 8 * j, v);
          }
        }
      };
      put(foe, pos);
      put(att, apos);
      uint32_t v;
      std::memcpy(&v, &fy, 4);
      StoreBE32(base + foe + 228, v);
      REXLOG_INFO("selftest: run place foe {:08X} at ({:.2f} {:.2f}) yaw {:.3f}, att base ({:.2f} {:.2f}) yaw {:.3f} gap {:.1f}",
                  foe, pos[0], pos[1], fy, GuestF32(att + 240), GuestF32(att + 248), ay, gap);
      sub_8219B668(ctx, base);
    } else if (kind == "lethal") {
      sub_82198AB0(ctx, base);
    } else {
      sub_821B4BF0(ctx, base);
    }
  }
  ctx = saved;
  REXLOG_INFO("selftest: start #{} {} act {} att {:08X} (act {} var {}) foe {:08X} (act {} var {}){}", ++count, kind, act,
              att, GuestU32(att + 184), GuestU32(att + 188), foe, GuestU32(foe + 184), GuestU32(foe + 188),
              REXCVAR_GET(selftest_foe_down) ? " foe downed first" : "");
}
