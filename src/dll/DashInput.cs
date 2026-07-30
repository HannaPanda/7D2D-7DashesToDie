using System;
using HarmonyLib;
using InControl;

namespace SevenDashesToDie
{
    // ---------------------------------------------------------------------------------
    // Registers a rebindable "Dash" key with the game's own input system.
    //
    // PlayerActionsBase.InitActionSet() runs CreateActions() -> CreateDefaultKeyboardBindings()
    // -> CreateDefaultJoystickBindings(), so a postfix on CreateActions is the point where the
    // set exists but nothing has been bound or loaded yet.
    //
    // XUiC_OptionsControls.createControlsEntries() enumerates PlayerActionSet.Actions at
    // runtime and groups each action by its PlayerAction.UserData (a
    // PlayerActionData.ActionUserData). It has no hardcoded list, so an action created here
    // shows up in Options > Controls by itself, complete with rebinding.
    // ---------------------------------------------------------------------------------
    public static class DashInput
    {
        // InControl serialises bindings by action name; changing this resets everyone's key.
        public const string ActionName = "SevenDashesDash";

        // Default key. V is unbound in vanilla PlayerActionsLocal (checked against
        // CreateDefaultKeyboardBindings) and sits next to WASD.
        const Key DefaultKey = Key.V;

        static PlayerActionsLocal cachedOwner;
        static PlayerAction cachedAction;

        /// <summary>The dash action for this input set, or null if it is not registered.</summary>
        public static PlayerAction Get(PlayerActionsLocal _input)
        {
            if (_input == null) return null;
            if (ReferenceEquals(_input, cachedOwner)) return cachedAction;

            foreach (PlayerAction a in _input.Actions)
            {
                if (a.Name != ActionName) continue;
                cachedOwner = _input;
                cachedAction = a;
                return a;
            }
            return null;
        }

        [HarmonyPatch(typeof(PlayerActionsLocal), "CreateActions")]
        static class Patch_PlayerActionsLocal_CreateActions
        {
            static void Postfix(PlayerActionsLocal __instance)
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
                        return;
                    }

                    var action = (PlayerAction)create.Invoke(__instance, new object[] { ActionName });

                    // Argument order matches PlayerActionData.ActionUserData's ctor:
                    // nameKey, descKey, group, appliesToInputType, allowRebind,
                    // allowMultipleBindings, doNotDisplay, defaultOnStartup.
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

                    cachedOwner = __instance;
                    cachedAction = action;
                    Log.Out(SevenDashesMod.LogPrefix + "dash key registered (default " + DefaultKey + ")");
                }
                catch (Exception e)
                {
                    Log.Error(SevenDashesMod.LogPrefix + "failed to register the dash key: " + e);
                }
            }
        }
    }
}
