# 7 Dashes to Die - 7 Days to Die (V3.0)

A **dash**, an **air dash** and a **double air dash**, unlocked through a new Agility perk:
**Rule 2: Double Tap**.

- **Tap the dash key** for a burst of speed in the direction you are moving. No input means
  forward.
- **Air dash** cancels your fall, so it carries you flat across a gap instead of into it.
- **Double air dash** at the last rank: two dashes per jump before you touch down again.
- **Gated behind Agility**, using the same level requirements as vanilla's Agility perks
  (1 / 3 / 5 / 7 / 10). The perk sits next to Parkour under Athletics.
- **Rebindable key** that shows up in the game's own Controls menu, under
  **Movement ▸ Player movement** next to Jump and Crouch. Default: **V**.
- **Configurable in-game** through [Gears](https://www.nexusmods.com/7daystodie/mods/4017):
  force, cooldown, stamina cost, volume, and whether the perk is required at all.
- Localized into **13 languages** (EN, DE, ES, FR, IT, JA, KO, PL, PT-BR, RU, TR, ZH-Hans,
  ZH-Hant).

## Status

**Not yet play-tested.** Everything here was written against the installed
`Assembly-CSharp.dll` (3.0.1) and compiles clean, but no in-game run has confirmed the
feel of the dash yet. The one number that needs tuning by hand is the force - see
[Tuning](#tuning).

## Requirements

- **EasyAntiCheat must be OFF** - this mod ships a Harmony DLL. Works in single-player and
  on private servers.
- **[Gears](https://www.nexusmods.com/7daystodie/mods/4017)** - *optional*, only needed for
  the in-game settings menu.
- **Multiplayer:** install on the client for the ability, and on the server too, so the perk
  exists in the skill tree. Movement in 7DTD is client-authoritative, so the dash itself
  works from the client; other players see it as fast movement, not as an impulse.

## Installation

1. Install the zip with Vortex or MO2, or extract the `SevenDashesToDie` folder into your
   `7 Days To Die/Mods/` folder.
2. Launch with EAC disabled.
3. Spend a point on **Agility ▸ Athletics ▸ Rule 2: Double Tap**, or turn *Require perk* off
   in the Gears settings.

## The perk

| Rank | Agility | What it adds |
|---|---|---|
| 1 | 1 | Ground dash |
| 2 | 3 | Stronger dash, shorter cooldown |
| 3 | 5 | **Air dash** (one charge, refills on landing) |
| 4 | 7 | Stronger dash, shorter cooldown |
| 5 | 10 | **Double air dash** (two charges) |

A dash costs stamina and has a cooldown; higher ranks shorten it. Air charges refill the
moment you touch the ground.

## Settings

With Gears installed: **main menu or ESC ▸ Mods ▸ 7 Dashes to Die ▸ Dash**.

| Setting | Default | Range | Without Gears |
|---|---|---|---|
| Enabled | On | On / Off | On |
| Require perk | On | On / Off | On |
| Force (%) | 100 | 25-300, step 5 | 100 |
| Cooldown (s) | 1.5 | 0.2-10, step 0.1 | 1.5 |
| Stamina cost | 10 | 0-50, step 1 | 10 |
| Volume (%) | 100 | 0-100, step 5 | 100 |
| Log dash distance | Off | On / Off | Off |

Values are read fresh on every dash, so moving a slider takes effect on the next one.

## Tuning

The dash impulse is derived from the controller's own `MotorJumpForce`, not from a
hardcoded speed, so it stays in the engine's units. The remaining unknown is the
multiplier, which starts at `2.2` (rank 1).

Turn on **Log dash distance** and dash a few times. Each dash writes a line like:

```
[7 Dashes to Die] dash rank 1 (ground): force 0.396, travelled 4.83 m in 0.8 s, air charges left 0
```

Set **Force (%)** until that distance is what you want, then, if you like the value, move it
into `BaseForceFactor` in `src/dll/Dash.cs` so it becomes the new default.

## How it works

- **The impulse** - the local player is driven by UFPS' `vp_FPController`, not by the
  generic `Entity.motion` path. `AddSoftForce(force, frames)` spreads one impulse over
  several frames, which is a burst rather than a jolt, and the controller's own collision
  sweep still runs, so a dash cannot push you through geometry. An air dash additionally
  calls `ScaleFallSpeed(0f)` so it reads as a flat glide.
- **The key** - `XUiC_OptionsControls.createControlsEntries` enumerates
  `PlayerActionSet.Actions` at runtime and groups each action by its `UserData`. There is no
  hardcoded list, so an action created in a postfix on `PlayerActionsLocal.CreateActions`
  appears in the Controls menu by itself, rebinding included.
- **The perk** - plain vanilla `progression.xml`. It carries no `passive_effect`:
  `PassiveEffects` is a fixed engine enum (`Count = 203`), so a mod cannot add one. The DLL
  reads the rank directly with `Progression.GetProgressionValue(...).Level`.
- **Settings** - Gears is read purely by reflection over the loaded `GearsAPI` assembly, so
  the DLL neither references nor requires it.

See [`docs/architecture/mechanisms.md`](docs/architecture/mechanisms.md) for the details and
the IL evidence behind each claim.

## Building from source

**DLL** (`src/dll/`): references the game's `Assembly-CSharp.dll`, `LogLibrary.dll`,
`InControl.dll`, `0Harmony.dll` and `UnityEngine*.dll` by absolute path, so it only builds
on a machine with 7DTD installed.

```
cd src/dll
DOTNET_ROLL_FORWARD=LatestMajor dotnet build -c Release -o out
```

Copy `out/SevenDashesToDie.dll` into `SevenDashesToDie/`.

**Localization**: `python src/gen/gen_localization.py` regenerates
`SevenDashesToDie/Config/Localization.csv` with correct RFC-CSV quoting.

## AI-generated content

Code, XML and the translations were written with the help of Anthropic's Claude and reviewed
before release. The dash sound (`SevenDashesToDie/Resources/dash1.wav`) was generated with
**ElevenLabs** text-to-sound-effect. No other generated assets are included.

## Credits

- "Double Tap" is Rule 2 from *Zombieland* (2009); vanilla 7DTD already has Rule 1: Cardio.
- Harmony by Andreas Pardeike.
- [Gears](https://www.nexusmods.com/7daystodie/mods/4017) by Laydor.
- UFPS / `vp_FPController` ships with the game.

## License

MIT - see [LICENSE](LICENSE). The license covers the code and configuration. The dash sound
was generated on a paid ElevenLabs plan, which covers commercial use and redistribution.
