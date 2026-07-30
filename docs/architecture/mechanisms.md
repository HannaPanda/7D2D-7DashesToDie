# Mechanisms

Four things in this mod are not obvious from the outside. Each is recorded here with the
evidence it was derived from - all of it read out of the installed `Assembly-CSharp.dll`
(3.0.1) with Mono.Cecil, none of it from memory.

| File | Mechanism |
|---|---|
| `src/dll/Dash.cs` | The impulse, the charge/cooldown state machine, the guards |
| `src/dll/DashInput.cs` | Registering a rebindable key with the vanilla Controls menu |
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
| `LoadData` | Looks each saved name up via `actionsByName.TryGetValue` and skips misses, so bindings saved before this mod existed load fine and the new action keeps its default. |
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

State (`airCharges`, `nextDashTime`, `wasGrounded`) is tied to the `EntityPlayerLocal`
instance it belongs to and reset when the instance changes, because the local player is
recreated on respawn and relog.
