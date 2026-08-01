# Mechanisms

The things in this mod that are not obvious from the outside. Each is recorded here with the
evidence it was derived from - all of it read out of the installed `Assembly-CSharp.dll`
with Mono.Cecil, none of it from memory. Version-sensitive claims are marked with the builds
they were checked against.

| File | Mechanism |
|---|---|
| `src/dll/Dash.cs` | The impulse, the charge/cooldown state machine, the guards |
| `src/dll/DashInput.cs` | Registering a rebindable key **and** a rebindable controller button |
| `src/dll/DashDoubleTap.cs` | The optional double-tap trigger |
| `src/dll/SevenDashesMod.cs` | Entry point and the Gears-by-reflection bridge |
| `SevenDashesToDie/Config/progression.xml` | The perk and its Agility gate |

---

## 1. The impulse goes through `vp_FPController`, not through `Entity.motion`

**The trap:** `Entity.motion` is public, `Entity.AddVelocity`/`SetVelocity` are public
virtual, and vanilla even has a precedent - `BlockJumpPad.OnEntityWalking`'s entire body is
`entity.motion.y = 3f`. It looks like the obvious lever. For the **local player** it is the
wrong one.

`EntityPlayerLocal.MoveEntityHeaded` resolves `get_vp_FPController()` at the top and
branches through the controller. The `Entity.motion` arithmetic further down the same method
- the clamp to ±0.11 followed by `motion *= 0.545` - sits in the **ladder** branch
(`isLadderAttached`, and it is followed by `distanceClimbed`), not in the normal
ground/air path.

Likewise, `EntityAlive.DefaultMoveEntity` damps x/z by `0.91` per tick, but its only caller
is `EntityAlive.MoveEntityHeaded`, which `EntityPlayerLocal` **overrides**. That constant
governs NPCs, not the player.

**What the controller offers instead** (all public):

| Member | Use here |
|---|---|
| `AddSoftForce(Vector3 force, float frames)` | The dash. Spreads one impulse over N frames. |
| `ScaleFallSpeed(float)` | Air dash: `0f` cancels the accumulated fall for a flat glide. |
| `Grounded` | When to refill air charges. |
| `MotorJumpForce` | Calibration reference for the dash force. |

`AddSoftForce` semantics, from its IL: it divides `force` by `Time.timeScale`, clamps
`frames` to 1..120, applies `force / frames` immediately via `AddForceInternal`, and queues
`force / frames` into each of `frames` entries of `m_SmoothForceFrame`. So the **`force`
argument is the total impulse**, not a per-frame value.

Because the controller performs the actual movement, its own collision sweep applies. A raw
`motion` write would bypass it and could push the player through thin geometry at dash
speed.

**Calibration.** The dash force is `MotorJumpForce * BaseForceFactor * rankMultiplier *
forceSetting`. Deriving it from the jump keeps it in whatever force units the engine
currently uses, instead of hardcoding a number that a retune would silently invalidate.
`BaseForceFactor = 2.2` is a starting estimate, not a measured value - the `DebugLog`
switch exists to replace it with a measured one.

### Momentum Lite

`FixedMove` opens with `m_MoveDirection = m_MoveDirection + m_ExternalForce`, where
`m_MoveDirection` is the motor (walking, sprinting) and `m_ExternalForce` is the pot
`AddSoftForce` pays into. A raw impulse therefore **stacks fully on top of sprinting**. Play
testing found the sprint bonus worth far more than a rank step, which buried the
1.00 → 1.12 progression under it - five ranks nobody can feel are five wasted skill points.

Deleting the momentum instead reads as hitting a wall, so it is rationed:

```
along  = max(0, dot(horizontalVelocity, dashDir))
target = min(dashSpeed + along * 0.25, dashSpeed * 1.2)
gain   = max(0, target - along)
```

Two details carry the design:

- **Only the component already travelling the dash direction counts.** Sprinting forward and
  dashing forward stays the fastest option; sprinting forward and dashing sideways no longer
  inherits the whole sprint into the dodge.
- **`along` is clamped at zero.** A backdash out of a forward sprint has a negative dot
  product, and the unclamped formula would make it the *weakest* dash available - precisely
  in the moment it is the panic button.

**⚠ What this does not do:** the motor keeps running during the dash. `m_MoveDirection` is
motor *plus* external force, so a sideways dash while still holding W remains diagonal - the
sideways component is now clean, but the player's own sprint continues underneath it. Only
the dash's contribution is governed here, never the player's input.

**⚠ `SpeedPerImpulse` is a model, not a measurement.** `UpdateForces` shrinks the external
force each tick with `m_ExternalForce /= 1 + PhysicsForceDamping * AdjustedTimeScale` - a
division, so per-tick retention is `1/(1+damping)` - while `AddSoftForce` feeds
`impulse/frames` in per tick, giving a truncated geometric build-up before the decay. Speed
is that peak over the tick length. What the model cannot see is `SmoothMove`, which passes
`m_MoveDirection` through a `vp_PlayerEventHandler.Move` message and rescales by
`Time.deltaTime`; reverse-engineering that chain blind is not worth it.

So the estimate is treated as one. The computed impulse is clamped to
`[0.3, 1.0] × the plain impulse`, meaning a wrong model degrades to "roughly like before"
rather than to a twitch or a catapult, and `DebugLog` prints predicted against measured peak
speed on every dash. The ratio between those two is the correction factor.

## 2. A mod can add a key to the vanilla Controls menu

`XUiC_OptionsControls.createControlsEntries` builds its list at runtime: it walks the five
action sets (`PlayerActionsLocal`, `Vehicle`, `Permanent`, `GUI`, `PlayerActionsGlobal`),
iterates `PlayerActionSet.Actions`, and casts each action's `UserData` to
`PlayerActionData.ActionUserData`, skipping anything with `doNotDisplay` or
`appliesToInputType == None`. There is no hardcoded list of actions anywhere in that method.

So creating an action is enough to make it appear, rebinding included. `ActionUserData`'s
constructor takes, in order:

```
actionNameKey, actionDescKey, actionGroup, appliesToInputType,
allowRebind, allowMultipleBindings, doNotDisplay, defaultOnStartup
```

with the groups and tabs available as statics on `PlayerActionData`
(`GroupPlayerControl`, `TabMovement`, …).

**Where the entry lands.** From `PlayerActionData..cctor`:

```
TabMovement        = ActionTab("inpTabPlayerControl", 0)
GroupPlayerControl = ActionGroup("inpGrpPlayerControlName", null, 0, TabMovement)
```

Both have priority `0`, so `GroupPlayerControl` is the first group of the first tab - where
Forward, Jump and Crouch live. Within the group, order is `PlayerActionSet.Actions` order,
i.e. creation order, so an action added in a postfix appears at the end of the vanilla
entries.

**⚠ The field names and the label keys are crossed.** `TabMovement`'s key is
`inpTabPlayerControl` and `GroupPlayerControl`'s key is `inpGrpPlayerControlName`, and the
strings those resolve to are the other way round again:

| Static | Localization key | Shown as (EN / DE) |
|---|---|---|
| `TabMovement` | `inpTabPlayerControl` | **Movement** / Bewegung |
| `GroupPlayerControl` | `inpGrpPlayerControlName` | **Player movement** / Spielerbewegung |

So the user-facing path is *Options ▸ Controls ▸ **Movement** ▸ **Player movement***. Reading
it off the C# identifiers gives "Player Control", which is not a label that exists in the UI.
Resolve the key, do not trust the field name.

`ActionTab` and `ActionGroup` both implement `IComparable<T>`, so a mod *could* define its
own tab or group and have it sort correctly. Reusing the vanilla statics is simpler and puts
the dash where a player looks for a movement control.

**⚠ Timing - this is what broke v1.0.0.** `PlayerActionsBase..ctor` calls `InitActionSet()`,
which runs `CreateActions()` → `CreateDefaultKeyboardBindings()` →
`CreateDefaultJoystickBindings()`. `PlayerActionsLocal` itself is constructed by
`Platform.PlayerInputManager..ctor`, which `Factory.CreateInstances` runs during engine
startup. Measured on this machine:

| | Time |
|---|---|
| `INF InControl (version 1.8.9 …)` | **0.2 s** |
| `INF [MODS] Initializing mod SevenDashesToDie` → `InitMod` | **19.4 s** |

So the action set is fully built roughly nineteen seconds before any mod can patch anything.
A postfix on `CreateActions` never fires for the set that actually exists, and the symptom is
a key that appears in no tab, with no error anywhere in the log - the patch is applied, it
just has nothing left to intercept.

**The fix: create the action lazily**, the first time anything asks for it. Everything
InControl does with a set tolerates that:

| Method | Behaviour |
|---|---|
| `CreatePlayerAction` | Body is exactly `new PlayerAction(name, this)` - no initialisation guard, callable at any point. |
| `Actions` | A `ReadOnlyCollection` wrapping the live `actions` list, assigned once in the ctor, so a late action does appear in it. |
| `LoadData` | Looks each saved name up via `actionsByName.TryGetValue` and skips misses, so bindings saved before this mod existed load fine and the new action keeps its default. **The same skip is what loses the dash's own saved binding - see §2a.** |
| `AddPlayerAction` | **Throws `InControlException` on a duplicate name** - so look the action up before creating it. |

There is no static route to `Platform.PlayerInputManager` (checked: no static field or method
anywhere in `Assembly-CSharp` returns one), so the set is reached through the two paths that
do exist:

- **In-game** - `EntityPlayerLocal.playerInput`, from the per-frame tick.
- **Main menu** - a prefix on `XUiC_OptionsControls.createControlsEntries`, via
  `__instance.xui.playerUI.playerInput` (all three accessors public). Needed because Options ▸
  Controls is reachable with no world loaded, so the player cannot be relied on.

The `CreateActions` postfix is kept as well, for a set genuinely built after mod load (a
second local player, a set recreated on relog). For the startup set it is simply a no-op.

**Access.** `PlayerActionSet.CreatePlayerAction(string)` is `protected` (InControl), so it is
invoked through `AccessTools`. `PlayerAction.AddDefaultBinding(Key[])` is public.

**Default key.** `V`. Extracted the key constants out of
`PlayerActionsLocal.CreateDefaultKeyboardBindings` and checked `V` (`InControl.Key.V` = 57)
against them; it is unbound in that set.

**Caveat:** InControl serialises bindings by action name, so changing
`DashInput.ActionName` resets every user's key.

---

## 2a. A late action loses its saved binding on every launch

Being created lazily buys the menu entry (§2) and costs the persistence, and 1.1.0 paid that
price without noticing. Reported symptom: *"After every restart of the game, I have to bind
the key to controller again, it doesn't save between sessions."*

**Saving was never the problem.** `XUiC_OptionsControlsBase.afterChangesSaved` →
`GameOptionsControls.Save()` walks `PlayerActionSet.actions` - which contains the dash by then
- and writes one base64 blob per action set into `SdPlayerPrefs`, keyed
`GameOptionsControls.cActionSetSavePrefix + set.Name`, i.e. `ActionSet_local`. Decoding that
value out of `HKCU\Software\The Fun Pimps\7 Days To Die` shows the dash entry with its
bindings, exactly as saved.

**Loading is.** `GameManager.Awake` does both of these, in this order, in that one method:

| IL offset in `GameManager.Awake` | Call |
|---|---|
| `IL_0123` | `GameOptionsControls.Load()` - reads each `ActionSet_*` blob into `PlayerActionSet.Load` |
| `IL_035d` | `ModManager.LoadMods()` |

`PlayerActionSet.LoadData` resolves every saved entry through `actionsByName.TryGetValue` and
**skips misses**. At `IL_0123` the mod does not exist, so the dash's saved entry is read,
matched against nothing, and dropped. Then the mod loads, the action is created, and
`AddDefaultBinding` gives it `V` and no gamepad button. Every launch, silently - a skipped
entry is the *normal* case for a binding written by a build that has since removed the action,
so InControl logs nothing.

Measured on 3.0.1 (`output_log_client__2026-08-01__12-31-10.txt`):

| | Time |
|---|---|
| `INF Awake` (so `GameOptionsControls.Load()` a few ms later) | **9.06 s** |
| `Loaded assembly SevenDashesToDie` | **10.13 s** |
| `Harmony patches applied` | **19.67 s** |
| `dash key registered (default V)` - first actual use | **187.46 s** |

No hook can be early enough: at `IL_0123` the patches do not exist yet, so patching
`GameOptionsControls.Load` itself would not fire either.

**The fix - replay the blob once, after the action exists.** `DashInput.RestoreSavedBindings`
runs at the end of `Register()`: read `SdPlayerPrefs.GetString("ActionSet_" + set.Name)` and
hand it back to `PlayerActionSet.Load`. That is the game's own call with the game's own string,
so the binary format stays TFP's problem. Two limits are deliberate:

- **Only when the blob names our action.** `PlayerAction.Save` writes the name with
  `BinaryWriter.Write(string)` (7-bit length prefix, then raw UTF-8), so a byte search of the
  decoded blob answers it. A player who never rebound the dash never has their set touched.
- **`PlayerActionSet.Load` falls back to `Reset()`** - every action in the set to defaults - if
  the blob does not parse. Accepted, because it is the exact string the game itself parsed
  successfully seconds to minutes earlier in the same session.

**Both bindings live in that one blob under the one action name**, so this restores keyboard
and gamepad together. Only the gamepad side was ever reported because the keyboard side
resets to `V`, which is what most players have anyway; anyone who moved it off `V` was losing
that too.

---

## 2b. The controller screen is a different list, not a different flag

The obvious assumption - that `EAppliesToInputType.Both` on the `ActionUserData` gets you
both a keyboard row and a gamepad row - is **wrong**, and it is wrong silently. 1.0.4 set
`Both` and still had no controller entry.

The two screens are sibling overrides of `XUiC_OptionsControlsBase.createControlsEntries`
that share no code:

| Screen | Class | Source of its rows | Reads `appliesToInputType`? |
|---|---|---|---|
| Options ▸ Controls | `XUiC_OptionsControls` | `PlayerActionSet.Actions` over five action sets | **Yes** - skips `None` and `ControllerOnly` |
| Options ▸ Controller | `XUiC_OptionsController` | the public field `PlayerActionsBase.ControllerRebindableActions` over three action sets | **No** - never touched |

From `XUiC_OptionsControls.createControlsEntries`, the filter that defines "keyboard screen":

```
ldfld  EAppliesToInputType ActionUserData::appliesToInputType
brfalse.s <skip>        // None
ldc.i4.2 / beq.s <skip> // ControllerOnly
```

and from `XUiC_OptionsController.createControlsEntries`, the entirely different source:

```
ldfld  List`1<PlayerAction> PlayerActionsBase::ControllerRebindableActions
callvirt List`1<PlayerAction>::GetEnumerator()
```

Both then feed `XUiC_OptionsControlsBase/XUiC_BindingEntry::set_Action`, so a row obtained
this way is a real, rebindable row - not a read-only label.

Three consequences the code depends on:

- **`ControllerRebindableActions` is a public instance field**, so appending to it needs no
  reflection.
- **⚠ `CreateDefaultJoystickBindings` clears it** (`…ControllerRebindableActions::Clear()`)
  before refilling it with the vanilla actions. It runs inside `InitActionSet()` at engine
  startup - i.e. before mods, same nineteen-second problem as above - and again through
  `ResetControllerBindings` → `AsyncResetControllerBindings`. Without a postfix, one press of
  "Reset to defaults" in the controller options drops the modded row until the next restart.
- **The rows are a fixed grid.** `Data/Config/XUi_Menu/windows.xml` gives `optionsController`
  four `<options_bindings_tab for_controller="true" />` tabs, and the `options_bindings_tab`
  template lays out `rows="22"` binding entries; the assignment loop is
  `entries[i].Action = i < list.Count ? list[i] : null`, so a list longer than 22 would be
  truncated silently. Vanilla fills far fewer, so appending is safe.

**Which tab it lands in.** The controller screen re-labels one tab: any action whose
`actionTab.tabNameKey` is `inpTabPlayerControl` is moved into the list keyed
`inpTabPlayerOnFoot`. `GroupPlayerControl` sits under `TabMovement`, whose key is exactly
`inpTabPlayerControl`, so the dash appears under **On Foot** - the counterpart of *Movement ▸
Player movement* on the key screen.

**No default gamepad binding, on purpose.** Extracting the `InputControlType` constants from
`PlayerActionsLocal.CreateDefaultJoystickBindings` leaves `DPadRight` (14) as the only
unclaimed control on a standard pad. Taking it would rebind something the player never asked
about; an empty, clearly-labelled row is the better default.

**Checked against 3.0.0, 3.0.1 and 3.1.0** - same class, same field, same `Clear()`, on all
three.

---

## 2c. The double tap is read from the game's own move actions

`DashDoubleTap` polls `PlayerActionsLocal.MoveForward / MoveBack / MoveLeft / MoveRight`
(each a real `PlayerAction`, so `WasPressed` / `WasReleased` come from InControl) rather than
Unity's raw keyboard. That inherits the player's rebound keys for free and avoids the trap
already documented for `Dash.DashDirection`: `EntityPlayerLocal.movementInput` reads back as
zero from a postfix on `EntityPlayerLocal.Update`, because Unity does not order
`PlayerMoveController.Update` against it.

The rule is a double *click*, not two presses: **press → release → press, both presses inside
the window**, measured from the *first press*. Measuring from the release instead would let a
long hold ("run across the map, stop, walk on") supply the first half of a tap. The required
release in between means a key repeat or a stuck key cannot chain, and the first press slot
is cleared on a hit so three taps are one dash, not two.

⚠ `Poll()` runs every frame and `Settings.*` goes through reflection into Gears, so the
tracking is pure local arithmetic and the settings are read only on the frame a genuine
second press lands.

## 3. The perk carries no passive effect, by necessity

`PassiveEffects` is a plain engine enum running `None = 0` … `HeadshotDamageModifier = 202`,
`Count = 203`. A mod cannot add a value to it, so there is no way to express "dash distance"
as a `<passive_effect>`. There is also no `MinEventAction` for velocity - the full catalog is
72 types and none of them touch movement impulse - which is why this mod needs a DLL at all
and cannot be pure XML.

The perk is therefore a pure gate: `progression.xml` defines it with the vanilla Agility
`level_requirements` (1/3/5/7/10, mirroring `perkArchery`) and nothing but
`effect_description` entries in its `effect_group`. The DLL reads the rank with
`player.Progression.GetProgressionValue("perkRuleTwoDoubleTap").Level` and maps it to force,
cooldown and air charges itself.

The perk is appended to `/progression/perks` (perks live in that container, lines 875-4872 of
vanilla `progression.xml`, not directly under `/progression`).

## 4. Guards

A dash is refused when any of these hold. Each was picked because it is a state where an
impulse would be wrong or would fight the engine:

| Check | Why |
|---|---|
| `IsDead()` / `!IsAlive()` | Obvious. |
| `AttachedToEntity != null` | In a vehicle or turret; the player is not driving movement. |
| `isLadderAttached` | The ladder branch owns `motion`; an impulse there fights it. |
| `IsSwimming()` | Swimming has its own motion handling. |
| `IsFlyMode.Value` | Creative flight; a dash is meaningless. |
| `!fpc.enabled` | The controller is not driving the player right now. |
| `windowManager.IsInputActive()` | A UI window has input; the keypress is not for us. |

All three triggers - dash key, controller button, double tap - go through the same
`TryDash`, so the guards, the cooldown, the stamina cost and the perk rank apply identically
to all of them. A double tap is not a cheaper dash.

State (`airCharges`, `nextDashTime`, `wasGrounded`) is tied to the `EntityPlayerLocal`
instance it belongs to and reset when the instance changes, because the local player is
recreated on respawn and relog. `DashDoubleTap`'s half-finished taps are tied to the
`PlayerActionsLocal` instance for the same reason.
