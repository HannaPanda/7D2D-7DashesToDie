# Changelog

## 1.1.0 - 2026-07-31

Two ways to ask for a dash that were not there before. The ability itself is untouched -
same force, same cooldown, same ranks.

- **The dash is now bindable to a controller button.** Options ▸ Controller ▸ On Foot has a
  "Dash" row, rebindable like any vanilla one. It ships **unbound**: every face button,
  bumper, trigger, stick click and d-pad direction bar one is already taken by vanilla, and
  quietly claiming the leftover would be worse than an empty row you can fill yourself.
  - Worth knowing for other modders: the controller screen is a **separate list** from the
    keyboard one. `XUiC_OptionsControls` walks `PlayerActionSet.Actions`;
    `XUiC_OptionsController` ignores that and reads the public field
    `PlayerActionsBase.ControllerRebindableActions`. Being in `Actions` with
    `EAppliesToInputType.Both` gets you a key row and no gamepad row - which is exactly what
    1.0.4 shipped.
  - `CreateDefaultJoystickBindings` **clears** that list before refilling it, so "Reset to
    defaults" in the controller options used to be enough to lose a modded row. There is now
    a postfix that puts it back.
- **Optional double tap.** Tap a movement key twice and you dash that way - so a double tap
  of A dodges left even while W is held. **Off by default**, because a false detection costs
  stamina and moves you; turn it on under Gears ▸ 7 Dashes to Die.
  - New setting **Double tap window (ms)**, default **300**, range 150-600. Windows' own
    double-click default of 500 ms is far too slack for a movement key - ordinary strafe
    corrections start dashing on their own. Competitive dodge windows sit at 200-250 ms,
    which is reliable only if you practise it. 300 ms is reachable without aiming for it and
    still short enough that walk-stop-walk does not trip it. Phantom dashes → go down to
    200; cannot land one → go up.
  - The taps are read from the game's own `MoveForward/Back/Left/Right` actions, so they
    follow your rebound keys instead of assuming WASD.
  - The rule is a double *click*: press, release, press, with both presses inside the
    window. Measuring from the first press is what stops "hold W across the map, let go,
    press again" from counting - the hold itself eats the window.
- **Headless-clean on V 3.0.0, V 3.0.1 and V 3.1.0**: mod loaded, Harmony patches applied,
  0 ERR / 0 EXC / 0 XML problems on each. The Harmony hit is the meaningful part - it proves
  `XUiC_OptionsController.createControlsEntries` and
  `PlayerActionsLocal.CreateDefaultJoystickBindings` still exist on all three, and their IL
  still uses `ControllerRebindableActions` the same way.
- **GUI-verified on all three** (2026-08-01): on V 3.0.0, V 3.0.1 and V 3.1.0 the controller
  row appears under Options ▸ Controller ▸ On Foot, binds, and is still there after "Reset to
  defaults"; the double tap is configurable in Gears and fires. Both features are menu and
  input behaviour, which `-nographics` runs not at all, so this is the part that had to be
  watched rather than reasoned from the IL.

## 1.0.4 - 2026-07-31

Play-tested tuning pass. First release whose numbers come from measurement rather than estimate.

- **The dash is now about a quarter as strong.** `BaseForceFactor` 2.2 -> 0.88. The old
  value carried ~25 m from a standstill in 0.8 s - a whole street. Dialling the in-game
  Force slider found 33% short and 40% right, so that setting is now the default and the
  slider reads 100% again. Expect roughly 10 m on the flat.
- **`SpeedPerImpulse` corrected by x1.74**, from 15 logged dashes (median 1.74, min 1.43,
  max 2.09, sigma 0.16). The analytic model under-predicted peak speed; the measurement is
  the credible side, since ~34 m/s could not have produced the 25 m actually covered.
- Consequence worth knowing: the correction makes Momentum Lite **less** aggressive, because
  a correctly-sized dash speed makes the sprint term relatively smaller. At ~10 m/s entry the
  reduction moves from ~53% to ~30%. If sprint dashes now feel long, `MomentumShare` is the
  knob, not the force.
- Momentum Lite also matters more at this force than it did at the old one: the dash is now
  ~2x sprint speed instead of ~5x, which is the range the mechanic was designed for.
- The 1.2x momentum cap is confirmed dead weight - it needs ~29 m/s of entry speed and the
  measured maximum was 12.1. Left in as a ceiling for modded movement speeds.
- Anyone who liked the old strength: set the Force slider to **250%**.

## 1.0.3 - 2026-07-30

"Momentum Lite": a sprint dash is no longer a plain speed multiplier.

- `vp_FPController.FixedMove` opens with `m_MoveDirection = m_MoveDirection + m_ExternalForce`,
  so the dash impulse stacked fully on top of sprinting. The sprint bonus was worth far more
  than a rank step, which made the 1.00 -> 1.12 progression invisible under it.
- Momentum is now kept but rationed: only the component already travelling the dash
  direction counts, at 25%, capped at 1.2x the plain dash speed. Sprint-and-dash-forward is
  still the fastest option; sprint-and-dash-sideways no longer inherits the full sprint.
- The `along` term is clamped at zero, so a backdash out of a forward sprint keeps full
  strength. It is the panic button and must not be the weakest dash in the game.
- The plain standstill dash is deliberately unchanged, so this release changes exactly one
  variable.
- `SpeedPerImpulse` is a model of UFPS' force handling, not a measurement, so the computed
  impulse is clamped to between 30% and 100% of the unmodified one. With `DebugLog` on, each
  dash now logs entry speed, predicted speed and the peak actually reached, and the ratio
  between the last two is the correction factor for the model.

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
