#include <rex/cvar.h>

REXCVAR_DEFINE_STRING(gpu_backend, "vulkan", "GPU", "Graphics backend: vulkan, d3d12 (experimental)")
    .allowed({"d3d12", "vulkan"})
    .lifecycle(rex::cvar::Lifecycle::kRequiresRestart);

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
                      "Empty = off. Takes effect the next time the character is loaded")
    .lifecycle(rex::cvar::Lifecycle::kHotReload);

REXCVAR_DEFINE_STRING(exhibition_stages, "", "Game",
                      "Stages offered by the Exhibition stage select, in order, as stage ids,\n"
                      "e.g. \"0,1,4,2,3\" (Arena, Red Valley, Skyscraper, Island Resort, Street).\n"
                      "Ids above 11 load imported arenas (bg.pac group STG/<id> in the mods\n"
                      "folder). Needs a matching modded menu.pac\n"
                      "in mods/pac, or the cards and names will not line up.\n"
                      "Empty = the original three (Arena, Red Valley, Skyscraper)");

REXCVAR_DEFINE_INT32(heap_block_extra_mb, 16, "Game",
                     "Extra MiB for the game's heap block, all of it for the motion pack heap\n"
                     "(m.pac MOT/GAME, originally 33 MiB). The block grows into guest physical\n"
                     "memory the 360 kernel used to own. 0 = original")
    .range(0, 24);

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
