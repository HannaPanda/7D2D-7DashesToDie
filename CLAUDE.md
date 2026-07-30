# CLAUDE.md

Project context and workflows for this repo live in **[AGENTS.md](AGENTS.md)** - read it first.

@AGENTS.md

## Quick reminders for Claude Code

- This is a **7 Days to Die V3.0** mod. The repo mirrors a live MO2 deployment at
  `C:\Modlists\Smorgasbord\mods\7 Dashes to Die\SevenDashesToDie\` - keep them in sync.
- Prefer the **`7d2d-modding` skill** for any engine/API question; it interrogates the real
  `Assembly-CSharp.dll` instead of guessing, and its `LEARNINGS.md` records the traps.
- Before deploying the DLL, make sure 7DTD is **not running** (it locks the file).
- Never hand-edit `SevenDashesToDie/Config/Localization.csv`; run
  `python src/gen/gen_localization.py`.
- The dash numbers in `Dash.cs` are **measured, not estimated** (1.0.4). `BaseForceFactor`
  and `ModelCorrection` both come from logged runs - do not "clean them up" to rounder
  values without a new measurement.
- Releases go out via a version tag (`git tag vX.Y.Z && git push origin vX.Y.Z`), which
  triggers CI.
