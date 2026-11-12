#include <rex/cvar.h>

REXCVAR_DEFINE_STRING(gpu_backend, "vulkan", "GPU", "Graphics backend: vulkan, d3d12 (experimental)")
    .allowed({"d3d12", "vulkan"})
    .lifecycle(rex::cvar::Lifecycle::kRequiresRestart);

// Native renderer switch (RR-NR-4; read once in RumbleRosesXxApp::LatchRenderer, rumble_roses_xx_app.h).
// RR-NR-12: native is the default (user 2026-11-08); emulated = fallback (launcher, exit-3 restart).
REXCVAR_DEFINE_STRING(rrxx_renderer, "native", "Native",
                      "Renderer: native (D3D12 renderer, default; needs the shader cache the launcher "
                      "builds) or emulated (Xenos GPU emulation, fallback)")
    .allowed({"emulated", "native"})
    .lifecycle(rex::cvar::Lifecycle::kRequiresRestart);
REXCVAR_DEFINE_STRING(rrxx_test_config, "", "Native",
                      "Test only: extra .toml loaded before the renderer is chosen");
REXCVAR_DEFINE_BOOL(rrxx_native_test_device_fail, false, "Native",
                    "Test only: rrxx_renderer=native acts as if the host device failed (exit code 3)");
REXCVAR_DEFINE_STRING(rrxx_selftest_keys, "", "Native",
                      "Test only: host key presses for self-tests, KEY@seconds after the overlay drawer "
                      "comes up, comma separated (e.g. F4@75,F4@83 opens and closes the settings overlay)");

REXCVAR_DEFINE_BOOL(pause_on_focus_loss, true, "Window",
                    "Mute audio and ignore pad input while the window is not focused");

REXCVAR_DEFINE_INT32(bloom, 2, "GPU",
                     "Bloom/glow strength.\n"
                     " 0 = Off\n"
                     " 1 = Reduced (one of the two bloom passes)\n"
                     " 2 = Original")
    .range(0, 2)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(blur, true, "GPU",
                    "Background blur (depth-of-field). false = everything sharp")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(unlock_all, false, "Game",
                    "Unlock all characters (incl. alter egos and Lady X) and all\n"
                    "\"Others\" shop items. Unlocks are kept in the save once it saves")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(infinite_money, false, "Game",
                    "Keep RR money topped up at 9,999,999")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(infinite_special, false, "Game",
                    "Keep the finishing-move gauge full (Killer / Lethal always ready)\n"
                    "for human players")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(debug_event_menu, false, "Debug",
                    "Experimental. Open the leftover developer event menu instead of\n"
                    "playing a story cutscene (Play Event Movie / Edit Event Cast & Stage)")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(log_asset_paths, false, "Debug",
                    "Write every archive path the game requests (e.g. /MENU/MAPL)\n"
                    "to the log once, to find unused assets")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(log_lambda_parts, false, "Debug",
                    "Write each character model part the game loads (slot, id, kind)\n"
                    "to the log, to trace created-wrestler (Lambda) models")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_STRING(lambda_model_overrides, "", "Game",
                      "Give created wrestlers (Lambda / EDIT slots) their own model from a\n"
                      "DLC costume id. Comma-separated EDIT number = costume id, e.g.\n"
                      "\"3=480, 5=481\". The costume's bs_waist model replaces the whole\n"
                      "outfit and the Lambda face and hair are hidden (the model brings its\n"
                      "own head).\n"
                      "Add \"+head\" (\"3=480+head\") to keep the Lambda face and hair.\n"
                      "Add \":head\" (\"1=466:head\") to swap only the face and hair (the\n"
                      "costume's bs_head/bs_face/bs_hair) and keep the Customize outfit.\n"
                      "Per select-screen colour: \"1=622/630/631/632\" (colour 1-4; a missing\n"
                      "or 0 entry = colour 1; Customize shows colour 1). Suffixes go per id.\n"
                      "Empty = off. Takes effect the next time the character is loaded")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_STRING(exhibition_stages, "", "Game",
                      "Stages offered by the Exhibition stage select, in order, as stage ids,\n"
                      "e.g. \"0,1,4,2,3\" (Arena, Red Valley, Skyscraper, Island Resort, Street).\n"
                      "Ids above 11 load imported arenas (bg.pac group STG/<id> in the mods\n"
                      "folder). Needs the matching menu.pac from RRXX-tools/arena/stage_menu.py\n"
                      "in mods/pac, or the cards and names will not line up.\n"
                      "Empty = the original three (Arena, Red Valley, Skyscraper)");

REXCVAR_DEFINE_INT32(heap_block_extra_mb, 16, "Game",
                     "Extra MiB for the game's heap block, all of it for the motion pack heap\n"
                     "(m.pac MOT/GAME, originally 33 MiB). The block grows into guest physical\n"
                     "memory the 360 kernel used to own. 0 = original")
    .range(0, 24);

REXCVAR_DEFINE_INT32(default_heap_extra_mb, 64, "Game",
                     "Extra MiB for the game's default heap (heap 30, 32 MiB originally; HD\n"
                     "outfits load whole textures there). The heap moves to its own block in\n"
                     "the guest physical range the game leaves to D3D (233 MiB). 0 = original")
    .range(0, 160);

REXCVAR_DEFINE_INT32(heap3_trim_mb, 8, "Game",
                     "MiB taken off game heap 3 (28 MiB, ~10 MiB used in select sweeps +\n"
                     "matches); the heap block shrinks by the same, so the D3D overflow area\n"
                     "below it grows. 0 = original")
    .range(0, 12);

REXCVAR_DEFINE_INT32(mot_heap_mb, 0, "Game",
                     "Minimum size of the motion pack heap (m.pac MOT/GAME), in MiB. Space above\n"
                     "33 + heap_block_extra_mb is taken from the general heap (32 MiB originally).\n"
                     "0 = no minimum")
    .range(0, 64);

REXCVAR_DEFINE_BOOL(log_heaps, false, "Debug",
                    "Every 10 seconds write each game heap's free space (now and lowest seen)\n"
                    "to the log, and log every failed game heap allocation")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(log_moves, false, "Debug",
                    "LETH-BLACK diagnostics: log move starts (act/var, partner, flags),\n"
                    "motion track lookups for acts 10000+ (hit/miss) and camera mode switches")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(log_sobj, false, "Debug",
                    "R5-TORRIE-DOG diagnostics: log every object-pool SOBJ request (id, slot)\n"
                    "and whether /SOBJ/<id> was found")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_DOUBLE(match_cam_min_dist, 49.6, "Game",
                      "Minimum distance of the in-match camera (stock = 3.1 x 16 x the closest\n"
                      "wrestler scale; 49.6 = full body at scale 1.0). Stops tight face shots\n"
                      "when an entrant's scale is below 1. 0 = stock")
    .range(0.0, 400.0)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_INT32(pin_kickout_latch_ms, 0, "Game",
                     "V15-KICKOUT: a pinned wrestler kicks out if they pressed any button in the\n"
                     "last N ms when the referee's count reaches 3 (stock: one of 3 buttons on\n"
                     "exactly that frame). 0 = stock")
    .range(0, 2000)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_INT32(selftest_act, 0, "Debug",
                     "R7-SELFTEST-ACT (dev exe): P1 performs this act on its foe in every match,\n"
                     "selftest_delay_s after P1's first live frame, then every selftest_repeat_s. 0 = off")
    .range(0, 65535)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_DOUBLE(selftest_delay_s, 20.0, "Debug",
                      "R7-SELFTEST-ACT: seconds from P1's first live match frame to the first start")
    .range(0.0, 3600.0)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_DOUBLE(selftest_repeat_s, 15.0, "Debug",
                      "R7-SELFTEST-ACT: seconds between starts (0 = once per match)")
    .range(0.0, 3600.0)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(selftest_foe_down, false, "Debug",
                    "R7-SELFTEST-ACT: lay the foe down (act 0 var 3) right before each start")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_STRING(selftest_kind, "auto", "Debug",
                      "R7-SELFTEST-ACT start path: lethal = sub_82198AB0 (Lethal start after its intro),\n"
                      "two = sub_821B4BF0 (grapple two-person start), solo = sub_820D50A8 (one-person act),\n"
                      "auto = lethal for 6500-6999 / 9500-9999 / 12000-18999, else two")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_DOUBLE(selftest_down_near, 0.0, "Debug",
                      "R7-TOPROPE-CORNER-b: selftest_kind = down (only keep the foe lying) also puts the foe\n"
                      "this far from P1 toward the ring centre (0 = off)")
    .range(0.0, 50.0)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_INT32(selftest_down_act, 0, "Debug",
                     "CENA-TEST-c: selftest_kind = down lays the foe as this act var 0 and treats it as lying\n"
                     "(1 = face-down, for leg-side ground Lethals like STF); 0 = stock face-up (act 0 var 3)")
    .range(0, 30)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_DOUBLE(selftest_run_gap, 21.7, "Debug",
                      "R7-ALICIA-FIN-g: selftest_kind = lethal with a running Lethal (9500-9999) first puts the foe\n"
                      "this far in front of P1, facing her (natural trigger spacing 20-23), then starts it through the real\n"
                      "Lethal task sub_8219B668 (20403 intro, snapshot/restore of both bases). 0 = old direct sub_82198AB0 start.\n"
                      "V15-ALICIA-DIST: < 0 = the natural side (foe at base - |gap|*(sin, cos)(yaw), as user logs)")
    .range(-60.0, 60.0)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_INT32(log_pair_act, 0, "Debug",
                     "R7-BETH-GS-HOLD-f: log both wrestlers every frame (PAIR lines: base, yaw, the 33 joint\n"
                     "matrices' world translation) while either one plays an act in [this, this+999] (0 = off)")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_INT32(selftest_corner, 0, "Debug",
                     "R7-TOPROPE-CORNER-c: selftest_kind = down also puts idle P1 on a corner stand (x, z = +-25):\n"
                     "1 = (+,+), 2 = (-,+), 3 = (-,-), 4 = (+,-), 0 = off (walk there by input)")
    .range(0, 4)
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_BOOL(log_music_scenes, false, "Debug",
                    "R7-MUSIC-BGM-b: log every music scene switch (sub_820828B8: scene, caller) and the rematch check")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);
