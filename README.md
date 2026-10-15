# Rumble Roses XX Recomp

A native Windows PC port of **Rumble Roses XX** (Xbox 360), made by static recompilation.
It runs the original game code natively. It is not an emulator.

**This download contains no game files.** You need your own legally obtained copy of the game
(a disc image `.iso`). DLC is optional and also comes from your own copy.

## Features

- Runs natively at any resolution (up to 8K internal render scale)
- Bloom setting (off / reduced / original) and an option to turn off the background blur
- **F4 in game:** quick settings menu and Quit
- Fixed saving ("Failed saving data" no longer happens)
- Working DLC costumes (they appear under Others > Locker Room)
- **Mods folder:** drop in replacement files without touching your game files
- **Custom costume slots:** add community costumes as *new* costumes instead of replacing official ones
- Optional cheats: unlock everything, infinite money (off by default)

## Install

1. Unzip anywhere, for example `C:\Games\Rumble Roses XX Recomp\`.
2. Run **RRXX Launcher.exe**.
3. First-time setup (the **Setup** tab opens by itself):
   - **Game:** choose your `Rumble Roses XX.iso`. The launcher checks it's the right version and copies the game files (about 3.6 GB).
   - **DLC (optional):** choose the folder with your Xbox 360 DLC packages.
4. Pick your settings in the **Settings** tab (press **Save settings**), then press **Play**.

Tick **"Skip the launcher next time"** to go straight into the game from then on. To get the launcher back,
hold **Shift** while starting, or double-click **Launcher Settings.bat** (created next to the launcher when you tick the box).

## Controls

An Xbox controller is recommended. Press **F4** at any time for quick settings and Quit.

## Settings

| Setting | What it does |
|---|---|
| Render scale | Internal resolution. 1 = 720p, 3 = 4K-class, 6 = 8K. Higher is sharper but slower. |
| Bloom | 0 = off, 1 = reduced, 2 = original glow |
| Background blur | Off makes the arena sharp; on matches the original |
| Anisotropic filtering | Texture sharpness at angles |
| Unlock all | Unlocks costumes, items and characters while on. Anything already saved stays unlocked. |
| Infinite money | Keeps money at 9,999,999 |

Settings are saved in `game\RumbleRosesReX.toml`. The graphics backend, sharpening and the Mods switch are also in the F4 menu.

## Mods

### Replacement files
Put files in `mods\` using the same path they have inside the game files.
Example: `mods\pac\ed\ex00.pac` replaces the game's `pac\ed\ex00.pac`. Your original files are never changed.
You can turn all mods off with the switch at the top of the **Mods** tab (or in the F4 menu).

### Costumes
Put each costume folder (an extracted DLC-style costume with `cos.dat`) into `mods\costumes\`.
The launcher gives every costume its own free slot, so it shows up as an extra costume and doesn't
replace or clash with official DLC. You can switch each costume on or off in the **Mods** tab; press **Apply** to install or remove them.

### Mod packs
A mod pack (`.rrxxpack`) only holds changes. In the **Mod packs** tab press **Install a pack...** and pick the file:
the launcher checks that your game files come from the right ISO version, rebuilds the pack's files from them into
`mods\` and `saves\`, and records everything it changed. **Uninstall selected** puts it all back (your save progress stays).
If a pack edits save slots and you have not played yet, start the game once, quit, and open the launcher again.

## Files and folders

```
RRXX Launcher.exe     launcher / setup
launcher.json         launcher's own settings
game\                 the game program and your copied game files
mods\                 your mods
modpacks\             installed mod packs (what they changed, for Uninstall)
saves\                saves, installed DLC and the shader cache
save-backups\         backups made by the launcher or F4
```

The **Saves** tab opens the folder and makes a one-click backup (`save-backups\`). Back up `saves\` to keep your progress. The whole folder can be moved; all paths are relative.

## Troubleshooting

- **Game won't start:** open the launcher and run setup again. The logs are in `game\logs\`.
- **Wrong game version:** the recomp is built for one specific release. A different disc version won't work.
- **A costume doesn't show:** check the Mods tab. Costumes must contain a `cos.dat` file.

## For developers

This repository is a fork of [wowjinxy/RumbleRosesReX](https://github.com/wowjinxy/RumbleRosesReX) (the ReXGlue
recompilation project for Rumble Roses XX). It contains the project, the title hooks, the launcher source and an
SDK patch. It does not contain game files, generated recompiled code or build products.
Build instructions: [BUILDING.md](BUILDING.md).

## Credits

See [CREDITS.md](CREDITS.md). Third-party licences are in [licenses](licenses).

## Legal

Rumble Roses XX is © Konami. This project is not affiliated with or endorsed by Konami or Microsoft.
No game code, assets or DLC are distributed. Please support official releases.
