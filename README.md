# Floating HP Bar 1.2

English | [简体中文](README.zh-CN.md)

Displays enemy names, numeric HP and floating health bars during battle, with smooth animation, a delayed damage trail, hit flashes and low-HP colour changes.

## Configuration

Edit `FloatingHPBar/FloatingHPBar.ini`; changes apply at the next battle. Restarting the game also reloads the settings.

| Setting | Default | Meaning |
| --- | --- | --- |
| Enabled | 1 | Enable the overlay |
| ShowMaxHp | 0 | Show current / maximum HP |
| HpMode | 0 | 0: automatic script reserve handling; 1: raw HP; 2: legacy vanilla subtraction |
| ShowName | 1 | Show enemy names |
| NameFontSize | 15 | Name size at 1080p |
| BarWidth / BarHeight | 156 / 22 | Bar size at 1080p |
| OffsetY | 8 | Additional vertical offset |
| ShowOnlyTargetable | 0 | Hide enemies that cannot be targeted |

Auto mode removes the 10,000 HP script reserve from vanilla enemies identified by Memoria's `IsNonDyingVanillaBoss` predicate, including its encounter exception, when maximum HP exceeds 10,000. Other enemies retain engine logical HP; high HP alone never triggers subtraction. Alternate Fantasy uses `GetLogicalHP`, so flagged padding is removed exactly once. Current displayed HP cannot fall below zero.

Version 1.1 incorrectly assumed `GetLogicalHP` already removed the reserve in vanilla mode; it actually returns raw HP there. This caused some bosses and special enemies to display 10,000 excess HP. Version 1.2 corrects the display only, without modifying HP, damage, or scripts. Large gameplay overhauls may change the encounter assumptions; use `HpMode = 1` to inspect raw HP. Scripted encounters can still end on conditions other than HP.

Compilation and 13 calculation checks passed using the actual HP calculation method with stubbed engine predicates. **In-game boss regression and Alternate Fantasy compatibility remain unverified.**

## Limitations and troubleshooting

- The screen-space overlay does not participate in 3D occlusion. Hidden slave parts are excluded, and off-screen enemies are hidden.
- Enemy names use the game's battle text; long names shrink to fit.
- Vertical placement has not been calibrated across all battles. Adjust `OffsetY` if needed.
- Check `Memoria.log` for `[FloatingHPBar] Version 1.2 initialised` and per-enemy HP diagnostics.
- Disable the game's no-encounter and 9,999-damage boosters when testing encounters and HP changes.

## Requirements and validation status

- Windows Steam edition of FINAL FANTASY IX with [Memoria v2025.07.04, including the 2025-07-13 hotfix](https://github.com/Albeoris/Memoria/releases/tag/v2025.07.04).
- Memoria 2025-07-13 is the current compilation and compatibility baseline. Earlier versions and Canary have not been verified.
- Source compilation and package structure checks have passed. These release candidates have **not yet completed in-game regression testing**. See [the release checklist](RELEASE-CHECKLIST.md), currently written in Chinese.
- Game files, Memoria binaries, translations, Moguri assets and save files are not included. Build references must come from your own game installation.

## Installation

1. Download `FloatingHPBar-v1.2.zip` from Releases and close the game.
2. Extract it into the game directory containing `FF9_Launcher.exe`. The resulting path must be `FloatingHPBar/StreamingAssets/Scripts/Memoria.Scripts.FloatingHPBar.dll` without an extra nested mod folder.
3. Enable the mod in the Memoria launcher's Mods tab and restart the game.
4. If it is not listed, append `"FloatingHPBar"` to the existing `FolderNames` and `Priorities` lists in the `[Mod]` section of `Memoria.ini`. Preserve the other entries and their order.

GitHub's automatic **Source code** archives do not include compiled DLLs. Use a release attachment or build the source yourself.

## Uninstall

Close the game, disable this mod in the launcher or remove its name from `FolderNames` / `Priorities`, then delete the `FloatingHPBar` folder.

## Combining mods

FloatingHPBar and BattleInfoOverlay have cooperative initialisation logic. BattleInfoOverlay alone may appear only after the first battle action resolves.

FloatingHPBar and ChocoboHotCold both implement `IOverloadOnBattleInitScript`. The current engine retains a single implementation, so one mod can override the other's hook. Running all four mods together and their loading order still require in-game verification. If health bars are missing, test FloatingHPBar on its own first.

BattleInfoOverlay has Chinese interface labels, and NeroNames uses fixed Chinese names. Other text depends on the game and installed translation. Compatibility with large gameplay overhauls is not guaranteed.

## Building and packaging

Install a stable .NET SDK, version 8 or newer. Run these commands from the repository root:

```powershell
./build.ps1 -GamePath 'D:/Games/FINAL FANTASY IX'
./package.ps1 -GamePath 'D:/Games/FINAL FANTASY IX'
```

The build reads references from `x64/FF9_Data/Managed` in the supplied game directory and writes the DLL into this repository's `FloatingHPBar/StreamingAssets/Scripts` directory. It does not install the mod or modify the game directory.

An additional Bash build entry point is provided; it has not been run on macOS or Linux:

```bash
bash build.sh '/path/to/FINAL FANTASY IX'
```

Source: `Source/FloatingHPBar.cs`. Packages and SHA-256 checksums: `dist/`. Generated DLLs and `dist/` are ignored by Git; upload the ZIP separately as a Release attachment. Packaging refuses to overwrite an existing ZIP; rename or remove that ZIP before rebuilding a package.

## Reporting problems

Include the mod version, Memoria version, operating system, resolution, game language, active mod order, reproduction steps and relevant `Memoria.log` excerpts. Remove personal paths before sharing logs. Screenshots are helpful for layout problems.

## Author and license

Author: **JustPeace**. Original code and content in this mod are released under the [MIT License](LICENSE). Retain the copyright and license notice when redistributing.

FINAL FANTASY IX, Memoria and third-party mods remain the property of their respective rights holders. This license does not grant rights to their code or assets.