# Conventions and gotchas

Verified against the installed game (7DTD 3.0.1) and this machine. Do not guess these.

## Assembly references

| Type used | Assembly |
|---|---|
| `EntityPlayerLocal`, `vp_FPController`, `Progression`, `Stat`, `Mod`, `IModApi` | `Assembly-CSharp.dll` |
| **`PlayerAction`, `PlayerActionSet`, `Key`** | **`InControl.dll`** |
| **`Log`** | **`LogLibrary.dll`** |
| `Harmony`, `HarmonyPatch`, `AccessTools` | `Mods\0_TFP_Harmony\0Harmony.dll` |
| `GameObject`, `Time`, `Mathf`, `Vector3`, `Quaternion` | `UnityEngine.CoreModule.dll` |
| **`AnimationEvent`** (a parameter of `Entity.PlayOneShot`, passed as `null`) | **`UnityEngine.AnimationModule.dll`** |

Two of these are traps rather than choices:

- `Log` living in its own assembly makes a missing reference look like a missing `using`
  (`CS0103: The name 'Log' does not exist`).
- `UnityEngine.AnimationModule` is needed even though the mod never touches animation,
  purely because `Entity.PlayOneShot(string, bool, bool, bool, AnimationEvent, float)` has an
  `AnimationEvent` in its signature. Without it the build fails with
  `CS0012: the type 'AnimationEvent' is defined in an assembly that is not referenced`.

## Localization

- The file **must** be `<mod>/Config/Localization.csv`.
  `Localization.LoadPatchDictionaries` tests exactly `_folder + "/Localization.csv"`; a
  `Localization.txt` is loaded for nothing.
- Real **RFC-4180 CSV**. An unquoted comma inside a value shifts every later language column.
  Generate with `python src/gen/gen_localization.py` (`csv.QUOTE_MINIMAL`), never by hand.
- The header mirrors the game's own `Data/Config/Localization.csv`, which has a
  **`KeepLoaded`** column between `NoTranslate` and `english`. The parser is header-driven,
  so a file without that column also works, but matching vanilla is the safer default.
- Column order after `Context / Alternate Text`: german, spanish, french, italian, japanese,
  koreana, polish, brazilian, russian, turkish, schinese, tchinese.
- `UsedInMainMenu` = `x` for anything visible before a world is loaded. Here: the Gears
  settings **and** the Controls dialog entries. The perk strings only appear in the in-game
  skill tree and leave it blank, matching vanilla's own perk rows.
- The `File` / `Type` columns are documentation, not behaviour - the dictionary is keyed by
  `Key`. Vanilla uses `progression` / `perk Agi` for Agility perks and `UI` /
  `Controls Dialog` for input actions; this mod follows that.

## Gears

- `ModSettings.xml` goes in the **mod root**, not `Config/`.
- The mod is keyed by `ModInfo.xml`'s `<Name>` (`SevenDashesToDie`) - not the folder name and
  not `DisplayName`. That is why `<Name>` is the technical identifier and `<DisplayName>`
  carries the "7 Dashes to Die" wordplay.
- Every control exposes its value as a **string** through `IGlobalValueSetting.CurrentValue`,
  so parse defensively and fall back to the C# default.
- Gears itself requires **Quartz**.

## Progression XML

- Perks live in `/progression/perks`, not directly under `/progression`. Appending to the
  wrong container makes the patch a silent no-op (a failed XML patch logs at **`WRN`**, not
  `ERR`, and a successful one logs nothing at all).
- `PassiveEffects` is a fixed enum, so a mod cannot invent one. Anything a perk should do
  that vanilla has no effect for has to be read out of `Progression` by a DLL.
- Reusing a vanilla `icon=` value (here `ui_game_symbol_run`) avoids shipping an atlas.

## MO2 packaging

MO2 maps the *contents* of `mods\<MO2 mod>\` into the game's Mods folder, so the real 7DTD
mod has to sit one level down:

```
mods\7 Dashes to Die\        <- MO2 mod (meta.ini lives here)
  meta.ini
  SevenDashesToDie\          <- the actual mod
    ModInfo.xml
    ...
```

`meta.ini` is MO2-internal and must never end up in a release zip; CI checks for that.

The MO2 warning *"contains no esp/esm/esl and no asset directory"* is a Bethesda-oriented
check and is expected for every 7DTD mod.

## Packaging and tooling

- **PowerShell 5.1 `Compress-Archive` writes zip entries with backslash separators**, which
  is malformed per the ZIP spec. Build release zips with `zip` (CI does) or Python `zipfile`.
- A **running game locks the DLL**. Close 7DTD before deploying.
- XML and the DLL load at **startup only**. The exception is Gears values, which this mod
  re-reads on every dash.
- The MO2 profile enables a mod through a `+<MO2 mod name>` line in
  `profiles\Smorgasbord\modlist.txt`. That file has **no BOM**; use
  `[System.IO.File]::WriteAllLines` with `UTF8Encoding($false)`, not `Set-Content -Encoding utf8`.
