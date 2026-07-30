# Build and release

## Build the DLL

```
cd src/dll
DOTNET_ROLL_FORWARD=LatestMajor dotnet build -c Release -o out
cp out/SevenDashesToDie.dll ../../SevenDashesToDie/SevenDashesToDie.dll
```

The `.csproj` references the game's assemblies by absolute path
(`C:\Steam\steamapps\common\7 Days To Die\...`), so it only builds on a machine with 7DTD
installed. A clean build proves the API usage against the real DLL; it proves nothing about
runtime behaviour.

**Close 7DTD first** - a running game locks the DLL and the copy fails with "Permission
denied".

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

1. **Options ▸ Controls** - "Dash" appears under Player Control and rebinds.
2. **Skill tree ▸ Agility ▸ Athletics** - "Rule 2: Double Tap" appears next to Parkour, gated
   at Agility 1.
3. Buy rank 1, press the key while moving - you dash, stamina drops, cooldown applies.
4. Rank 3: dash mid-jump; the fall should cancel and carry you flat.
5. Rank 5: two air dashes per jump, refilling on landing.

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

The multi-version smoke test lives at `E:\7DTD-Testbench\` (`Invoke-SmokeTest.ps1 -Version
<v>`, `Invoke-TestMatrix.ps1`). It boots each install headless and greps the log. Two limits
that matter here:

- Headless (`-nographics`) covers mod load, Harmony patches and XML, but **not input, not
  the Controls menu and not the dash itself**. Those need a GUI run per version.
- The smoke-test scripts export and re-import `HKCU\Software\The Fun Pimps\7 Days To Die`
  around each run, because GamePrefs are shared by every install and a fresh build
  overwrites them.

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
