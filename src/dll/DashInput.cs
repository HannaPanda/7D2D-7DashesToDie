using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using InControl;

namespace SevenDashesToDie
{
    // ---------------------------------------------------------------------------------
    // Registers a rebindable "Dash" key with the game's own input system.
    //
    // XUiC_OptionsControls.createControlsEntries() enumerates PlayerActionSet.Actions at
    // runtime and groups each action by its PlayerAction.UserData (a
    // PlayerActionData.ActionUserData). It has no hardcoded list, so an action that exists
    // in the set shows up in Options > Controls by itself, complete with rebinding.
    //
    // ⚠ THE ACTION SET IS BUILT BEFORE MODS LOAD, so it cannot be created from a postfix on
    // CreateActions. Chain: PlayerActionsBase..ctor -> InitActionSet() -> CreateActions(),
    // and PlayerActionsLocal is constructed by Platform.PlayerInputManager..ctor, which
    // Factory.CreateInstances runs during engine startup. Measured on this machine:
    // InControl initialises at 0.2 s, IModApi.InitMod runs at 19.4 s. A postfix there never
    // fires, which is exactly how v1.0.0 shipped a key that appeared in no tab.
    //
    // So the action is created LAZILY instead, the first time anything asks for it. That is
    // safe against everything InControl does with the set:
    //   - PlayerActionSet.CreatePlayerAction is just `new PlayerAction(name, this)` - no
    //     initialisation guard, callable at any point in the set's life.
    //   - PlayerActionSet.Actions is a ReadOnlyCollection wrapping the live `actions` list,
    //     assigned once in the constructor, so a late action really does appear in it.
    //   - PlayerActionSet.LoadData looks each saved name up with
    //     actionsByName.TryGetValue and skips misses, so saved bindings written before this
    //     mod existed load fine and our action simply keeps its default.
    //   - PlayerActionSet.AddPlayerAction THROWS on a duplicate name, hence the lookup
    //     before creating.
    //
    // ⚠ THAT SAME "SKIPS MISSES" IS ALSO THE PRICE OF BEING LATE, and it cost 1.1.0 a bug:
    // the saved bindings are applied before mods load too, so the dash's own saved key was
    // read, not matched, and dropped on every launch. See RestoreSavedBindings below.
    //
    // There is no static route to Platform.PlayerInputManager, so the set is reached through
    // the two paths that do exist: the local player (in-game) and the options dialog's own
    // XUi chain (main menu).
    //
    // ⚠ THE CONTROLLER SCREEN IS A SEPARATE LIST. Options > Controls
    // (XUiC_OptionsControls.createControlsEntries) walks PlayerActionSet.Actions and skips
    // anything whose appliesToInputType is None or ControllerOnly - that is the keyboard
    // screen. Options > Controller (XUiC_OptionsController.createControlsEntries) ignores
    // appliesToInputType entirely and instead enumerates the public field
    // PlayerActionsBase.ControllerRebindableActions. An action that is only in Actions gets
    // a key row and no gamepad row. So the dash is put in BOTH, and:
    //   - PlayerActionsLocal.CreateDefaultJoystickBindings CLEARS ControllerRebindableActions
    //     before refilling it with the vanilla actions. It runs at startup (before mods, so
    //     the postfix below is not what registers us the first time) and again on
    //     ResetControllerBindings, i.e. every "Reset to defaults" in the controller options
    //     would otherwise silently drop the dash row. Hence the postfix that re-adds it.
    //   - the controller screen lays the list into a fixed grid of 22 XUiC_BindingEntry rows
    //     per tab (Data/Config/XUi_Menu/templates.xml, options_bindings_tab), and vanilla
    //     fills far fewer, so there is room. The row order is the list order, so appending
    //     puts the dash at the end of "On Foot" - the same place it sits on the key screen.
    // ---------------------------------------------------------------------------------
    public static class DashInput
    {
        // InControl serialises bindings by action name; changing this resets everyone's key.
        public const string ActionName = "SevenDashesDash";

        // Default key. V is unbound in vanilla PlayerActionsLocal (checked against
        // CreateDefaultKeyboardBindings) and sits next to WASD.
        const Key DefaultKey = Key.V;

        // No default gamepad button, deliberately. Every face button, bumper, trigger,
        // stick click and d-pad direction except DPadRight is already taken by
        // PlayerActionsLocal.CreateDefaultJoystickBindings, and claiming the one leftover
        // would be worse than leaving the row empty: a dash bound over something the player
        // did not ask for is a bug report, an empty row in Options > Controller is an
        // invitation. The row is there, rebindable, from the first launch.

        static PlayerActionsLocal cachedOwner;
        static PlayerAction cachedAction;
        static PlayerActionsLocal failedOwner;

        /// <summary>
        /// The dash action for this input set, created on first sight. Null only if creation
        /// failed, in which case it is not retried for that set.
        /// </summary>
        public static PlayerAction Get(PlayerActionsLocal _input)
        {
            if (_input == null) return null;
            if (ReferenceEquals(_input, cachedOwner) && cachedAction != null) return cachedAction;
            if (ReferenceEquals(_input, failedOwner)) return null;

            foreach (PlayerAction a in _input.Actions)
            {
                if (a.Name != ActionName) continue;
                cachedOwner = _input;
                cachedAction = a;
                return a;
            }

            PlayerAction created = Register(_input);
            if (created == null)
            {
                failedOwner = _input;
                return null;
            }
            cachedOwner = _input;
            cachedAction = created;
            return created;
        }

        /// <summary>
        /// Get(), plus the controller screen's separate list. Kept off Get() itself because
        /// Get() is on the per-frame dash path and this walks a list; the callers below are
        /// the moments the list can actually have changed.
        /// </summary>
        public static void EnsureRegistered(PlayerActionsLocal _input)
        {
            PlayerAction action = Get(_input);
            if (action == null) return;

            List<PlayerAction> rebindable = _input.ControllerRebindableActions;
            if (rebindable == null || rebindable.Contains(action)) return;
            rebindable.Add(action);
            Log.Out(SevenDashesMod.LogPrefix + "dash added to the controller bindings list (unbound by default).");
        }

        static PlayerAction Register(PlayerActionsLocal _input)
        {
            try
            {
                // CreatePlayerAction is protected on PlayerActionSet.
                var create = AccessTools.Method(typeof(PlayerActionSet), "CreatePlayerAction",
                                                new Type[] { typeof(string) });
                if (create == null)
                {
                    Log.Warning(SevenDashesMod.LogPrefix +
                                "PlayerActionSet.CreatePlayerAction not found - no dash key registered.");
                    return null;
                }

                var action = (PlayerAction)create.Invoke(_input, new object[] { ActionName });

                // Argument order matches PlayerActionData.ActionUserData's ctor:
                // nameKey, descKey, group, appliesToInputType, allowRebind,
                // allowMultipleBindings, doNotDisplay, defaultOnStartup.
                //
                // GroupPlayerControl and its TabMovement both have priority 0, so the entry
                // lands in the first group of the first tab, after the vanilla movement keys.
                // The labels are not what the C# names suggest: TabMovement's key is
                // "inpTabPlayerControl" ("Movement") and GroupPlayerControl's is
                // "inpGrpPlayerControlName" ("Player movement").
                action.UserData = new PlayerActionData.ActionUserData(
                    "inpActSevenDashesDashName",
                    "inpActSevenDashesDashDesc",
                    PlayerActionData.GroupPlayerControl,
                    PlayerActionData.EAppliesToInputType.Both,
                    true,   // allowRebind
                    false,  // allowMultipleBindings
                    false,  // doNotDisplay
                    true);  // defaultOnStartup

                action.AddDefaultBinding(new Key[] { DefaultKey });

                Log.Out(SevenDashesMod.LogPrefix + "dash key registered (default " + DefaultKey +
                        ") in action set '" + _input.Name + "'");

                // The action exists now, so the saved bindings can finally be matched to it.
                RestoreSavedBindings(_input, action);
                return action;
            }
            catch (Exception e)
            {
                Log.Error(SevenDashesMod.LogPrefix + "failed to register the dash key: " + e);
                return null;
            }
        }

        // ---------------------------------------------------------------------------------
        // ⚠ THE SAVED BINDINGS ARE APPLIED BEFORE MODS LOAD, TOO. A lazily created action is
        // therefore not in actionsByName when its own saved entry is read, LoadData skips it,
        // and the action falls back to its default on every single launch. That is the 1.1.0
        // bug report "after every restart I have to bind the key to the controller again".
        //
        // GameManager.Awake does both of these, in this order, in that one method:
        //   IL_0123  GameOptionsControls.Load()  - reads SdPlayerPrefs "ActionSet_<setname>"
        //                                          and hands each blob to PlayerActionSet.Load
        //   IL_035d  ModManager.LoadMods()       - us
        // Measured on 3.0.1: "INF Awake" at 9.06 s, our assembly loaded at 10.13 s, Harmony
        // patches applied at 19.67 s, action created at first use later still. Nothing can be
        // patched early enough to be present for that load - the patches do not exist yet.
        //
        // Nothing is logged when it happens, because a saved entry with no matching action is
        // the normal case for a binding written by a build that has since dropped the action.
        //
        // SAVING works: XUiC_OptionsControlsBase.afterChangesSaved -> GameOptionsControls.Save
        // walks PlayerActionSet.actions, which by then contains the dash. The data really is on
        // disk - it just never comes back.
        //
        // So replay the very same blob once, now that the action exists. PlayerActionSet.Load
        // is what the game itself calls with that string, which keeps the binary format TFP's
        // problem rather than ours. Two deliberate limits:
        //   - it only runs when the blob actually names our action, so a player who never
        //     rebound the dash never has their action set touched at all;
        //   - PlayerActionSet.Load falls back to PlayerActionSet.Reset() - every action in the
        //     set back to defaults - if the blob does not parse. Acceptable, because this is
        //     the exact string the game itself parsed successfully seconds to minutes earlier.
        //
        // Keyboard and gamepad bindings both live in that one blob under the one action name,
        // so this restores both. Only the controller side was ever reported: the keyboard side
        // looked healthy to anyone who left it on the default V, and silently reset for anyone
        // who did not.
        // ---------------------------------------------------------------------------------
        static void RestoreSavedBindings(PlayerActionsLocal _input, PlayerAction _action)
        {
            try
            {
                // Same key GameOptionsControls builds, from the same public const.
                string pref = GameOptionsControls.cActionSetSavePrefix + _input.Name;
                if (!SdPlayerPrefs.HasKey(pref)) return;

                string blob = SdPlayerPrefs.GetString(pref);
                if (string.IsNullOrEmpty(blob) || !MentionsAction(blob)) return;

                _input.Load(blob);

                Log.Out(SevenDashesMod.LogPrefix + "restored the saved dash binding from '" +
                        pref + "': " + DescribeBindings(_action));
            }
            catch (Exception e)
            {
                Log.Error(SevenDashesMod.LogPrefix + "could not restore the saved dash binding: " + e);
            }
        }

        /// <summary>
        /// Whether a saved action-set blob holds an entry for our action. PlayerAction.Save
        /// writes the name with BinaryWriter.Write(string) - a 7-bit-encoded length prefix and
        /// then the raw UTF-8 bytes - so the name appears verbatim in the decoded data, and a
        /// byte search is enough to answer "is it worth reloading the set for this".
        /// </summary>
        static bool MentionsAction(string _blob)
        {
            byte[] data = Convert.FromBase64String(_blob);
            byte[] needle = Encoding.UTF8.GetBytes(ActionName);
            for (int i = 0; i <= data.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && data[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }

        /// <summary>
        /// Bindings of an action, for the log - the next report about a lost key. Reads
        /// UnfilteredBindings (regularBindings), not Bindings (visibleBindings): the latter
        /// only lists sources whose IsValid holds right now, so a gamepad button on an
        /// unplugged pad would read as "unbound" when it is in fact restored.
        /// </summary>
        static string DescribeBindings(PlayerAction _action)
        {
            if (_action.UnfilteredBindings.Count == 0) return "unbound";

            var sb = new StringBuilder();
            foreach (BindingSource b in _action.UnfilteredBindings)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(b.Name);
            }
            return sb.ToString();
        }

        /// <summary>Shared body of the two options-dialog hooks.</summary>
        static void EnsureFromDialog(XUiController _dialog, string _which)
        {
            try
            {
                if (_dialog == null || _dialog.xui == null) return;
                LocalPlayerUI ui = _dialog.xui.playerUI;
                if (ui == null) return;
                EnsureRegistered(ui.playerInput);
            }
            catch (Exception e)
            {
                Log.Error(SevenDashesMod.LogPrefix + _which + " dialog hook failed: " + e);
            }
        }

        // The main-menu path: Options > Controls is reachable without a loaded world, so the
        // local player cannot be relied on to have registered the action yet.
        [HarmonyPatch(typeof(XUiC_OptionsControls), "createControlsEntries")]
        static class Patch_XUiC_OptionsControls_createControlsEntries
        {
            static void Prefix(XUiC_OptionsControls __instance)
            {
                EnsureFromDialog(__instance, "controls");
            }
        }

        // Same for Options > Controller. It is a sibling override, not the inherited base
        // method, so patching XUiC_OptionsControlsBase would never fire for either screen.
        [HarmonyPatch(typeof(XUiC_OptionsController), "createControlsEntries")]
        static class Patch_XUiC_OptionsController_createControlsEntries
        {
            static void Prefix(XUiC_OptionsController __instance)
            {
                EnsureFromDialog(__instance, "controller");
            }
        }

        // Covers an action set that is built after the mod loaded (a second local player, a
        // set recreated on relog). A no-op for the set that already exists at that point.
        [HarmonyPatch(typeof(PlayerActionsLocal), "CreateActions")]
        static class Patch_PlayerActionsLocal_CreateActions
        {
            static void Postfix(PlayerActionsLocal __instance)
            {
                Get(__instance);
            }
        }

        // "Reset to defaults" in Options > Controller ends up here, and this method starts by
        // clearing ControllerRebindableActions. Without this postfix the dash row would
        // vanish from the controller screen until the next restart.
        [HarmonyPatch(typeof(PlayerActionsLocal), "CreateDefaultJoystickBindings")]
        static class Patch_PlayerActionsLocal_CreateDefaultJoystickBindings
        {
            static void Postfix(PlayerActionsLocal __instance)
            {
                try { EnsureRegistered(__instance); }
                catch (Exception e)
                {
                    Log.Error(SevenDashesMod.LogPrefix + "joystick binding hook failed: " + e);
                }
            }
        }
    }
}
