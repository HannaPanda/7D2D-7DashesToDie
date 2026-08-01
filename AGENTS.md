# AGENTS.md - 7 Dashes to Die (7 Days to Die V3.0 mod)

Onboarding for AI agents working on this repo. Read this first.

## What this project is

A movement mod for **7 Days to Die V3.0**: a dash, an air dash and a double air dash,
unlocked through a new Agility perk (**Rule 2: Double Tap**). Several things make it more
than a keybind:

- **The impulse goes through UFPS' `vp_FPController`**, which is what actually drives the
  local player - not the generic `Entity.motion` path that looks like the obvious lever and
  is not.
- **A rebindable key registered with the vanilla input system**, so it appears in Options ▸
  Controls on its own - plus a rebindable **controller** button, which is a *separate*
  registration in `PlayerActionsBase.ControllerRebindableActions`, not a flag on the same one.
- **An optional double-tap trigger** on the game's own movement actions, off by default.
- **A real perk in the real skill tree**, with vanilla Agility requirements, read back into
  C# because `PassiveEffects` is a closed enum a mod cannot extend.
- **Optional Gears integration** read purely by reflection, so the mod degrades to hard
  defaults when Gears is absent. 7DTD 3.0.1 has no dependency system at all, so "optional
  dependency" has to be handled inside the mod, never declared.

## Status

Play-tested on 7DTD 3.0.1 as of 1.0.4. `BaseForceFactor` (0.88, ~10 m per dash) and
`ModelCorrection` (1.74) are both **measured** values from logged runs, not estimates -
treat them as data and re-measure rather than re-guess. Every API claim in `docs/` was read
out of the installed `Assembly-CSharp.dll`.

1.1.0 (controller button + optional double tap) passed **both stages on 3.0.0, 3.0.1 and
3.1.0** (2026-08-01: row binds, survives "Reset to defaults", double tap fires on each).
`tb report --mod seven` is the authority on that list - it names only versions where both
stages passed *for the current mod version*, and **any DLL change empties it again**. Do not
widen it by hand from headless results.

## Repository layout

| Path | What |
|---|---|
| `SevenDashesToDie/` | The deployable 7DTD mod: `ModInfo.xml`, `ModSettings.xml` (Gears), `SevenDashesToDie.dll`, `Config/progression.xml`, `Config/Localization.csv` |
| `src/dll/` | C# Harmony source + `.csproj` |
| `test/testbench.mod.json` | Per-mod config for the shared multiversion testbench (`tb`, see below) |
| `src/gen/` | The localization generator |
| `nexus/` | Nexus mod page description (BBCode) and its notes |
| `.github/workflows/release.yml` | CI: tag `v*` → validate → zip → GitHub Release → Nexus upload |

## Docs map

- [`docs/architecture/mechanisms.md`](docs/architecture/mechanisms.md) - every custom
  mechanism with the IL evidence for each, plus the guard table.
- [`docs/conventions/modding.md`](docs/conventions/modding.md) - the verified 7DTD
  conventions and traps this mod depends on: assembly references, `Localization.csv` naming
  and quoting, the `progression.xml` container, MO2 nesting, zip packaging.
- [`docs/build-and-release.md`](docs/build-and-release.md) - building, the in-game test
  checklist, tuning the force, the tag-driven CI release and the Nexus setup.

Keep these in sync with the code: any change to behavior, structure or conventions updates
the matching `docs/` file in the same commit.

## Environment (this machine)

- **Game**: `C:\Steam\steamapps\common\7 Days To Die` - `Assembly-CSharp.dll`,
  `LogLibrary.dll` and `InControl.dll` under `7DaysToDie_Data\Managed\`, Harmony under
  `Mods\0_TFP_Harmony\`.
- **Live deployment** (MO2 "Smorgasbord" modlist):
  `C:\Modlists\Smorgasbord\mods\7 Dashes to Die\SevenDashesToDie\`
  - this is the running copy; the repo's `SevenDashesToDie/` mirrors it. **Keep them in sync.**
  - MO2 maps the *contents* of `mods\7 Dashes to Die\` into the game Mods folder, which is
    why the real mod sits one level down. The live tree also has a `meta.ini`; it belongs in
    neither the repo nor a release zip.
- **Optional at runtime**: Gears (Nexus mod 4017, by Laydor), installed in the modlist as
  `00000-Gears`. Gears itself requires Quartz.
- **Python 3** for the localization generator. No other tooling.
- **The `7d2d-modding` skill** is the right tool for any engine/API question: it interrogates
  the real `Assembly-CSharp.dll` instead of guessing, and its `LEARNINGS.md` records the
  traps. Never answer an API question from memory.

## How the four mechanisms are wired

1. **Dash** - `src/dll/Dash.cs`, Harmony postfix on `EntityPlayerLocal.Update`. Reads the
   dash action, checks rank / cooldown / stamina / air charges / guards, then calls
   `vp_FPController.AddSoftForce(dir * force, frames)`. In the air it first calls
   `ScaleFallSpeed(0f)` and spends a charge. Charges refill on the frame the controller
   reports `Grounded`.
2. **Key** - `src/dll/DashInput.cs`, Harmony postfix on `PlayerActionsLocal.CreateActions`.
   Creates a `PlayerAction` (via `AccessTools`, since `CreatePlayerAction` is protected),
   attaches a `PlayerActionData.ActionUserData`, and adds `Key.V` as the default binding.
   `XUiC_OptionsControls` picks it up on its own.
2b. **Controller button** - same file. The controller screen (`XUiC_OptionsController`)
   ignores `appliesToInputType` entirely and reads the public field
   `PlayerActionsBase.ControllerRebindableActions`, so the action is appended there too -
   and re-appended in a postfix on `CreateDefaultJoystickBindings`, which **clears** that
   list on every controller "Reset to defaults". Ships unbound.
2c. **Double tap** - `src/dll/DashDoubleTap.cs`. Polls the vanilla
   `MoveForward/Back/Left/Right` actions for press → release → press inside the window,
   measured from the first press. Off by default; feeds the same `TryDash`.
3. **Perk** - `SevenDashesToDie/Config/progression.xml` appends `perkRuleTwoDoubleTap` to
   `/progression/perks`. `Dash.GetRank` reads it via
   `Progression.GetProgressionValue(...).Level`.
4. **Settings** - `Settings.EnsureGears` finds the `GearsAPI` assembly in
   `AppDomain.CurrentDomain.GetAssemblies()` and walks
   `GearsSettingsManager.GetGearsMod(name) → GlobalSettings → GetTab → GetCategory →
   GetSetting`, reading `IGlobalValueSetting.CurrentValue`. The tab/category/setting names
   must match `SevenDashesToDie/ModSettings.xml`, and the mod name must match
   `ModInfo.xml`'s `<Name>`.

## Common tasks

- **Retune the dash** → `BaseForceFactor`, `SoftForceFrames`, `RankForce`, `RankCooldown` in
  `src/dll/Dash.cs`. Measure first with the `DebugLog` switch; do not tune by feel alone.
- **Change a default** → the `Default*` constants in `src/dll/SevenDashesMod.cs` **and** the
  `defaultValue` attributes in `SevenDashesToDie/ModSettings.xml`. They are two independent
  sources; a user without Gears only ever sees the C# ones.
- **Add or rename a setting** → `ModSettings.xml` (name, displayKey, tooltipKey), the
  matching keys in `src/gen/gen_localization.py`, regenerate the CSV, then the reader in
  `Settings`.
- **Add/fix a translation** → `src/gen/gen_localization.py`, then
  `python src/gen/gen_localization.py`. Never hand-edit the CSV: unquoted commas shift every
  later language column.
- **Change the default key or the action name** → `src/dll/DashInput.cs`. Renaming
  `ActionName` **resets every user's binding**, because InControl serialises by name - the
  controller binding included, since both live under that one name.
- **Retune the double tap** → `DefaultDoubleTapWindowMs` in `src/dll/SevenDashesMod.cs` and
  the `DoubleTapWindow` slider in `ModSettings.xml`. The detection rule itself is in
  `src/dll/DashDoubleTap.cs`; read the comment before changing it - the "measure from the
  first press" choice is load-bearing.
- **Smoke-test across versions** → `tb run --mod seven --profile matrix --json`, then
  `tb report --mod seven --json` for the matrix and the `TESTED_VERSIONS` line. Start with
  `tb doctor --json` whenever something looks wrong; it checks the installs, the dependency
  sources and the shared GamePrefs before you go log-hunting.
  **Headless proves nothing about menus or input** - anything touching the Controls or
  Controller screens, or the keys themselves, needs a GUI run on top:
  `tb run --mod seven --profile gui --visual defer`. An agent may start that run but may
  never set `--visual ok`; only a human can say how it looked.
