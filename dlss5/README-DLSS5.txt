Rumble Roses XX Recomp - DLSS 5 add-on (optional)
=================================================

What it does
------------
Runs the game through NVIDIA DLSS 5 neural rendering at 4K, 60 fps. The game renders at 2560x1440
(resolution_scale 2) and DLSS 5 outputs 4K. Your normal launch is not changed; this is a second way to start
the game. Saves and mods are shared with the normal launch.

Tested on: RTX 4090, 4K screen, Windows 11: locked 60 fps in matches, about 10 minutes of play without problems.
Other cards are untested. DLSS 5 is experimental third-party software, not an NVIDIA release.

You need
--------
- An NVIDIA GeForce RTX card (RTX 40 or 50 recommended; RTX 20/30 are much slower) and a current driver.
- The recomp v1.2 or newer, set up with RRXX Launcher.exe (game\assets filled from your ISO).
- Your own nvngx_dlssnr.dll (the DLSS 5 model file). It is NOT included and the setup does not download it:
    * RTX 50: NVIDIA's original 310.8 build (signed by NVIDIA).
    * RTX 20/30/40: the "compatibility" build (also runs on RTX 50). The tested one is 310.9.16,
      SHA-256 CDFA6C1EC784867182164C7836F324F087B5C9F374D0C9FE46F54999FEABF3ED.
  Where these come from and the known hashes: OptiScaler-NR's INSTALL-DLSSNR.md
  (https://github.com/jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass). The setup shows the hash of your file and
  says whether it is a known build.
- NVIDIA Smooth Motion OFF for this game (NVIDIA app > Graphics).
- Internet for the setup (about 195 MB).

Install
-------
1. Unzip this add-on into your recomp folder (the one with "RRXX Launcher.exe" and "game"). You get
   "Setup DLSS 5.bat", "Play with DLSS 5.bat" and a "dlss5" folder.
2. Run "Setup DLSS 5.bat". It asks for your nvngx_dlssnr.dll, then downloads and checks (fixed SHA-256 for every
   file) these parts from their official pages:
     ReShade 6.8.0 with add-on support   https://reshade.me
     DLSS5-Feeder 1.19.0 (MIT)           https://github.com/jlrouzies-fr/DLSS5-Feeder
     OptiScaler-NR 0.8.92 (GPL-3.0)      https://github.com/jlrouzies-fr/OptiScaler-DLSSNR-PreSR-Multipass
     NVIDIA DLSS runtime 310.9.16        https://github.com/NVIDIA/DLSS
     LumeniteFX (motion vectors)         https://github.com/umar-afzaal/LumeniteFX
     ReShade.fxh / ReShadeUI.fxh / DrawText.fxh   https://github.com/crosire/reshade-shaders
   Everything goes into the dlss5 folder. Nothing is installed system-wide, no admin rights, no registry.
3. Start the game with "Play with DLSS 5.bat".

In game
-------
  F9    DLSS 5 on/off (compare)
  F10   DLSS 5 settings panel (OptiScaler menu; click the game again to give it the keyboard back)
  Home  ReShade menu (shows the fps counter settings)
  F4    the recomp's own settings menu, as usual. Settings changed there in a DLSS 5 run are saved to
        dlss5\RumbleRosesReX.toml (the normal launch keeps game\RumbleRosesReX.toml).
The DLSS 5 helper is a second program (dlss5-feed-host64.exe) that starts and stops with the game.
dlss5\show-dlss5-settings.bat brings its window to the front if the panel does not react.

Tested settings (change in the F10 panel or dlss5\host64\OptiScaler.ini [DlssNr]): one pass, working scale 0.5,
transfer strength 1.5, style Standard. On RTX 40 with the compatibility build, changing Passes or Style while
playing hung the DLSS 5 helper twice in testing (the game keeps running without DLSS 5; restart it).

Less than 60 fps?
-----------------
- Close overlays (RivaTuner/RTSS first, then Steam/Discord/OBS overlays).
- Lower WorkingScale in the F10 panel. resolution_scale 2 is what holds 60 on an RTX 4090; scale 3 dropped to 34-51.

Remove
------
Delete the dlss5 folder, "Setup DLSS 5.bat" and "Play with DLSS 5.bat". Nothing else was changed.

Licences
--------
This add-on (scripts and settings) is part of the Rumble Roses XX Recomp by YucaGuapo. It contains no third-party
program files: ReShade (crosire), DLSS5-Feeder (jlrouzies-fr, MIT), OptiScaler-NR (OptiScaler team + jlrouzies-fr,
GPL-3.0; licence copied to dlss5\licenses by the setup, source at the link above), LumeniteFX (Afzaal, AGNYA
licence), NVIDIA DLSS (NVIDIA RTX SDK licence) and nvngx_dlssnr.dll are downloaded or supplied by you, under their
own licences. Not affiliated with or endorsed by NVIDIA, Yuke's or Konami.
