# Build and release

## Build the DLL

**Bump the version first, then build.** `SevenDashesToDie/ModInfo.xml` and
`src/dll/SevenDashesToDie.csproj` both carry it, and the csproj value is compiled *into* the
assembly. Building before the bump ships a binary stamped with the previous version - which
is how 1.0.2 went out carrying a `1.0.1` assembly, and how the 1.0.3 commit initially
contained a 1.0.1 binary next to a 1.0.3 `ModInfo.xml`. CI now refuses a release in that
state (see "Check the DLL matches ModInfo's version"), but the ordering is still on you.

```
cd src/dll
DOTNET_ROLL_FORWARD=LatestMajor dotnet build -c Release -o out
cp out/SevenDashesToDie.dll ../../SevenDashesToDie/SevenDashesToDie.dll
```

The `.csproj` references the game's assemblies by absolute path
(`C:\Steam\steamapps\common\7 Days To Die\...`), so it only builds on a machine with 7DTD
installed. A clean build proves the API usage against the real DLL; it proves nothing about
runtime behaviour.

**Only the modlist copy needs 7DTD closed.** A running game locks
`C:\Modlists\...\SevenDashesToDie.dll`, not the one in this repo. Keep the two copies as
separate steps - `out/` → repo always runs, repo → modlist waits for the game to exit.
Skipping both because the game is open is what left the repo holding a stale binary.

## Regenerate localization

```
python src/gen/gen_localization.py
```

Never hand-edit `SevenDashesToDie/Config/Localization.csv`.

## Deploy to the live modlist

MO2 "Smorgasbord": `C:\Modlists\Smorgasbord\mods\7 Dashes to Die\SevenDashesToDie\`.
The repo's `SevenDashesToDie/` folder mirrors that path one-to-one - keep them in sync.

## Test

XML and the DLL load at startup, so testing means a full game restart. After the run, grep
the newest `C:\Users\sourc\AppData\Roaming\7DaysToDie\logs\output_log_client__*.txt` for:

```
7 Dashes to Die          # the mod's own lines
WRN|ERR|EXC              # failures
did not apply            # a failed XML patch - these are WRN, not ERR
```

Expected on a good start:

```
[MODS] Loaded Mod: SevenDashesToDie (1.0.0.0)
[7 Dashes to Die] loaded, Harmony patches applied
[7 Dashes to Die] dash key registered (default V)
[7 Dashes to Die] Gears settings connected.       # or "Gears not installed - using built-in defaults."
```

Then, in-game:

1. **Options ▸ Controls ▸ Movement ▸ Player movement** - "Dash" appears at the end of that
   group (after Jump, Crouch, …) and rebinds.
2. **Options ▸ Controller ▸ On Foot** - "Dash" appears there too, **unbound**. Bind it to a
   button and check it survives leaving and re-entering the menu.
3. **Options ▸ Controller ▸ Reset to defaults** - the Dash row must still be there
   afterwards (unbound again). This is the regression the
   `CreateDefaultJoystickBindings` postfix exists for; without it the row disappears until
   the next restart.
4. **Skill tree ▸ Agility ▸ Athletics** - "Rule 2: Double Tap" appears next to Parkour, gated
   at Agility 1.
5. Buy rank 1, press the key while moving - you dash, stamina drops, cooldown applies.
6. Rank 3: dash mid-jump; the fall should cancel and carry you flat.
7. Rank 5: two air dashes per jump, refilling on landing.
8. **Gears ▸ Double tap to dash = On.** Double-tap W, then A while holding W - the second one
   must dash *left*, not forward. Then set it back to Off and confirm double-tapping does
   nothing at all.
9. **Double tap window.** At 150 ms a deliberate tap-tap should be hard to land; at 600 ms
   normal strafing should start producing dashes by itself. If neither is true, the tracker
   is not seeing the actions.

Run it with the testbench so the install is clean and Gears is there:

```
tb run --mod seven --profile gui --visual defer
```

The run ends when *you* close the game; the visual question then sits in the queue until
someone answers it (`tb status --pending`, then `tb verify --run <runId> --visual ok`). An
agent may start the
run and must leave the verdict deferred - it cannot see the screen.

**Gears is required for steps 8-9**, and the mod config lists `quartz` and `gears` under
`dependencies`, so every run mirrors them into `Mods\` and the cleanup leaves them alone.
This is not cosmetic - the old scripts kept only Harmony, so a single smoke test banished
Gears and Quartz from all three installs and the next GUI run had no settings menu at all,
with nothing anywhere reporting a failure. Provisioned is not loaded either: only the
`[MODS] Loaded Mod:` line counts, and the folder name is not the name the mod reports. If a
run flags a dependency as missing or unloaded, any settings test after it is meaningless.

⚠ **Steps 1-3 and 8-9 cannot be smoke-tested.** `-nographics` runs no XUi and no input, so
the headless matrix says nothing about them - it only proves the Harmony targets still
exist. Menus and keys are exactly the part that needs a human.

### Tuning the force

Turn on the Gears switch **Log dash distance** and read the per-dash line:

```
[7 Dashes to Die] dash rank 1 (ground): force 0.396, travelled 4.83 m in 0.8 s, air charges left 0
```

Adjust **Force (%)** until the distance is right, then fold the result into
`BaseForceFactor` in `src/dll/Dash.cs` and rebuild so it becomes the shipped default.

## Version compatibility

Name only versions the mod was **actually launched on with the log checked**. The list is
per mod version, and **any DLL change invalidates it entirely** - which for this mod means
every change, since the whole ability is in the DLL.

The multi-version testbench is the `tb` command (source: `C:\Users\sourc\7D2D-Testbench`,
binaries on `PATH` from `E:\7DTD-Testbench\bin`). This repo's half of the setup is
`test/testbench.mod.json`; everything about the machine lives in the bench's own
`testbench.json`.

```
tb doctor --json                              # before blaming the mod
tb run --mod seven --profile matrix --json    # stage 1, all three versions
tb report --mod seven --json                  # matrix + the TESTED_VERSIONS line
```

`report` refuses to name a version until **both** stages passed *for the current mod
version*, and prints why each one does not count (`kein GUI-Lauf`, `Mod-Version mismatch`,
`Sichtpruefung offen`). Do not assemble that list by hand from stage-1 results. Two limits
that matter here:

- Headless (`-nographics`) covers mod load, Harmony patches and XML, but **not input, not
  the Controls menu and not the dash itself**. Those need a GUI run per version.
- The bench exports and re-imports `HKCU\Software\The Fun Pimps\7 Days To Die` around each
  run, because GamePrefs are shared by every install and a fresh build overwrites them.

## Release

Tag-driven, same as the other mods in this family:

```
git tag v1.0.0
git push origin v1.0.0
```

`.github/workflows/release.yml` then validates the XML, sanity-checks the installable
structure, zips `SevenDashesToDie/` from the repo root and publishes a GitHub Release.

The workflow **packages only** - it cannot compile the DLL (that needs the game's
`Assembly-CSharp.dll`), so `SevenDashesToDie/SevenDashesToDie.dll` stays committed.

### Nexus

The Nexus upload step is skipped until the repo variable `NEXUSMODS_FILE_ID` is set:

- Secret `NEXUSMODS_API_KEY` - personal API key.
- Variable `NEXUSMODS_FILE_ID` - Files tab ▸ "API Info" on the mod page.

The v3 Upload API can only add a version to a file entry that already exists, so the mod page
and the first file have to be created by hand on the website once. Setting or clearing the
variable is the on/off switch for auto-publish.
