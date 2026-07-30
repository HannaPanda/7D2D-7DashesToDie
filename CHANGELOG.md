# Changelog

## 1.0.2 - 2026-07-30

- **Fixed: dashing always went straight ahead**, whichever way you were actually moving.
  The direction was read from `EntityPlayerLocal.movementInput`, which
  `PlayerMoveController.Update` fills and `MoveByInput` consumes - Unity does not order
  either against our postfix on `EntityPlayerLocal.Update`, and it read back as zero, so
  every dash fell through to the "no input" case. The axes now come from the live action
  set (`playerInput.Move`, X = strafe, Y = forward), which has no ordering dependency.
- The dash now has its own sound (`Resources/dash1.wav`), decoded from plain PCM by the
  DLL, instead of borrowing the game's `swoosh`.
- A dash now obeys the `FlipControls` effect (axes inverted, matching `MoveByInput`) and is
  refused under `DisableMovement`.

## 1.0.1 - 2026-07-30

Fixes the dash key not appearing anywhere in Options > Controls.

- The action set is built during engine startup, about nineteen seconds before mods load
  (`InControl` initialises at 0.2 s, `InitMod` runs at 19.4 s), so the postfix on
  `PlayerActionsLocal.CreateActions` never fired for the set that actually exists. The
  action is now created lazily on first use instead, which InControl allows: 
  `CreatePlayerAction` has no initialisation guard, `Actions` is a live wrapper around the
  backing list, and `LoadData` skips saved names it does not know.
- Registration now also runs from a prefix on `XUiC_OptionsControls.createControlsEntries`,
  so the key shows up in the main menu's Controls dialog with no world loaded.
- No behaviour change to the dash itself.

## 1.0.0 - 2026-07-30

Initial release for 7 Days to Die V3.0.

- Adds a dash, an air dash and a double air dash, unlocked through the new Agility perk
  **Rule 2: Double Tap** under Athletics, with vanilla Agility requirements (1/3/5/7/10).
- The impulse is handed to UFPS' `vp_FPController` via `AddSoftForce`, so it spreads over
  several frames and still passes through the controller's own collision sweep. The force
  is derived from the controller's `MotorJumpForce` rather than hardcoded, so it stays in
  the engine's units.
- An air dash first calls `ScaleFallSpeed(0f)`, which cancels the accumulated fall and makes
  the dash carry flat instead of diagonally down. Air charges refill on landing; rank 5
  grants a second one.
- The dash key appears in Options > Controls and rebinds like a vanilla control, default
  `V`. It is registered in a postfix on `PlayerActionsLocal.CreateActions`;
  `XUiC_OptionsControls` enumerates the action set at runtime and picks it up on its own.
- The perk carries no `passive_effect` by necessity, since `PassiveEffects` is a closed
  engine enum. The DLL reads the rank through `Progression.GetProgressionValue(...).Level`
  and maps it to force, cooldown and air charges itself.
- Dashing is refused while dead, mounted, on a ladder, swimming, in fly mode, or while a UI
  window holds input.
- Optional Gears integration (`ModSettings.xml`): enable, require-perk, force, cooldown,
  stamina cost, volume and a dash-distance debug log, read fresh on every dash so slider
  changes apply without a restart. Gears is resolved by reflection over the loaded
  `GearsAPI` assembly, so the mod runs on its built-in defaults when Gears is absent.
- Localized into 13 languages (EN, DE, ES, FR, IT, JA, KO, PL, PT-BR, RU, TR, ZH-Hans,
  ZH-Hant).

Not play-tested at the time of writing: `BaseForceFactor` is an estimate, and the
`DebugLog` switch exists to replace it with a measured value.
