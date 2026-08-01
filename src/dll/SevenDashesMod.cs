using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// 7 Dashes to Die - a dash / air dash / double dash ability gated behind an Agility perk.
//
// The whole ability lives in this DLL; the only XML is the perk itself (progression.xml),
// the Gears settings schema (ModSettings.xml) and the strings (Config/Localization.csv).
namespace SevenDashesToDie
{
    public class SevenDashesMod : IModApi
    {
        // Must match ModInfo.xml <Name> - Gears keys its settings by that value.
        public const string ModName = "SevenDashesToDie";
        public const string LogPrefix = "[7 Dashes to Die] ";

        /// <summary>Mod folder on disk; the dash clip is loaded relative to it.</summary>
        public static string ModPath = "";

        public void InitMod(Mod _modInstance)
        {
            ModPath = _modInstance.Path;
            new Harmony("hannapanda.sevendashestodie").PatchAll(Assembly.GetExecutingAssembly());
            Log.Out(LogPrefix + "loaded from " + ModPath + ", Harmony patches applied");
        }
    }

    // ---------------------------------------------------------------------------------
    // Settings. Gears is read lazily and purely through reflection, so the DLL neither
    // references nor needs GearsAPI.dll - without Gears the defaults below apply.
    // Values are read fresh on every dash, so moving a slider takes effect immediately.
    // ---------------------------------------------------------------------------------
    public static class Settings
    {
        public const bool DefaultEnabled = true;
        public const bool DefaultRequirePerk = true;
        public const float DefaultForcePercent = 100f;
        public const float DefaultCooldownSeconds = 1.5f;
        public const float DefaultStaminaCost = 10f;
        public const float DefaultVolumePercent = 100f;
        public const bool DefaultDebugLog = false;
        public const bool DefaultDoubleTap = false;

        /// <summary>
        /// Milliseconds allowed between the two presses of a double tap.
        ///
        /// 300 ms is the compromise, not a round number picked for looks. Windows' own
        /// double-click default is 500 ms, which is far too slack here: a movement key is
        /// held, released and re-pressed constantly during normal play, and at 500 ms
        /// ordinary strafe corrections start dashing on their own. Competitive double-tap
        /// dodges sit around 200-250 ms, which is reliable for someone who practises it and
        /// frustrating for everyone else. 300 ms is comfortably reachable without aiming for
        /// it, and still short enough that a deliberate walk-stop-walk does not trip it.
        /// Players who get phantom dashes should come down to 200; players who cannot land
        /// one should go up.
        /// </summary>
        public const float DefaultDoubleTapWindowMs = 300f;

        // Must match ModSettings.xml
        const string GearsTab = "SevenDashes";
        const string GearsCategory = "Dash";

        static bool connected;
        static bool gearsMissing;
        static float nextRetryTime;
        static PropertyInfo currentValueProp;
        static object enabledSetting, requirePerkSetting, forceSetting,
                      cooldownSetting, staminaSetting, volumeSetting, debugSetting,
                      doubleTapSetting, doubleTapWindowSetting;

        public static bool Enabled { get { return ReadBool(enabledSetting, DefaultEnabled); } }
        public static bool RequirePerk { get { return ReadBool(requirePerkSetting, DefaultRequirePerk); } }
        public static float ForceScale { get { return ReadFloat(forceSetting, DefaultForcePercent) / 100f; } }
        public static float CooldownSeconds { get { return ReadFloat(cooldownSetting, DefaultCooldownSeconds); } }
        public static float StaminaCost { get { return ReadFloat(staminaSetting, DefaultStaminaCost); } }
        public static float Volume { get { return Mathf.Clamp01(ReadFloat(volumeSetting, DefaultVolumePercent) / 100f); } }
        public static bool DebugLog { get { return ReadBool(debugSetting, DefaultDebugLog); } }
        public static bool DoubleTap { get { return ReadBool(doubleTapSetting, DefaultDoubleTap); } }
        public static float DoubleTapWindowSeconds
        {
            get { return ReadFloat(doubleTapWindowSetting, DefaultDoubleTapWindowMs) / 1000f; }
        }

        static string Read(object setting)
        {
            EnsureGears();
            if (setting == null || currentValueProp == null) return null;
            try { return currentValueProp.GetValue(setting, null) as string; }
            catch (Exception e)
            {
                Log.Warning(SevenDashesMod.LogPrefix + "could not read Gears setting: " + e.Message);
                return null;
            }
        }

        static bool ReadBool(object setting, bool fallback)
        {
            string s = Read(setting);
            bool v;
            if (!string.IsNullOrEmpty(s) && bool.TryParse(s, out v)) return v;
            return fallback;
        }

        static float ReadFloat(object setting, float fallback)
        {
            string s = Read(setting);
            float v;
            if (!string.IsNullOrEmpty(s) &&
                float.TryParse(s, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        static void EnsureGears()
        {
            if (connected || gearsMissing) return;

            // Gears may not have registered us yet, in which case the walk below bails out
            // and we retry. Throttle that: the dash reads settings from a per-frame code
            // path, and an unthrottled retry would run this whole reflection walk every
            // frame for as long as Gears stays silent.
            if (Time.time < nextRetryTime) return;
            nextRetryTime = Time.time + 2f;

            try
            {
                Assembly gearsApi = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "GearsAPI") { gearsApi = a; break; }
                }
                if (gearsApi == null)
                {
                    gearsMissing = true;
                    Log.Out(SevenDashesMod.LogPrefix + "Gears not installed - using built-in defaults.");
                    return;
                }

                Type tManager = gearsApi.GetType("GearsAPI.Settings.GearsSettingsManager");
                Type tGearsMod = gearsApi.GetType("GearsAPI.Settings.IGearsMod");
                Type tGlobal = gearsApi.GetType("GearsAPI.Settings.Global.IModGlobalSettings");
                Type tTab = gearsApi.GetType("GearsAPI.Settings.Global.IGlobalModSettingsTab");
                Type tCategory = gearsApi.GetType("GearsAPI.Settings.Global.IGlobalModSettingsCategory");
                Type tValue = gearsApi.GetType("GearsAPI.Settings.Global.IGlobalValueSetting");
                if (tManager == null || tGearsMod == null || tGlobal == null ||
                    tTab == null || tCategory == null || tValue == null)
                {
                    gearsMissing = true;
                    Log.Warning(SevenDashesMod.LogPrefix + "GearsAPI found but its types did not resolve - using defaults.");
                    return;
                }

                object gearsMod = tManager
                    .GetMethod("GetGearsMod", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { SevenDashesMod.ModName });
                if (gearsMod == null) return; // Gears may not have registered us yet - retry later

                object global = tGearsMod.GetProperty("GlobalSettings").GetValue(gearsMod, null);
                if (global == null) return;

                object tab = tGlobal.GetMethod("GetTab", new Type[] { typeof(string) })
                                    .Invoke(global, new object[] { GearsTab });
                if (tab == null) return;

                object category = tTab.GetMethod("GetCategory", new Type[] { typeof(string) })
                                      .Invoke(tab, new object[] { GearsCategory });
                if (category == null) return;

                // IGlobalModSettingsCategory has both GetSetting(string) and GetSetting<T>(string).
                MethodInfo getSetting = null;
                foreach (MethodInfo m in tCategory.GetMethods())
                {
                    if (m.Name != "GetSetting" || m.IsGenericMethodDefinition) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) { getSetting = m; break; }
                }
                if (getSetting == null) return;

                enabledSetting = getSetting.Invoke(category, new object[] { "Enabled" });
                requirePerkSetting = getSetting.Invoke(category, new object[] { "RequirePerk" });
                forceSetting = getSetting.Invoke(category, new object[] { "Force" });
                cooldownSetting = getSetting.Invoke(category, new object[] { "Cooldown" });
                staminaSetting = getSetting.Invoke(category, new object[] { "StaminaCost" });
                volumeSetting = getSetting.Invoke(category, new object[] { "Volume" });
                debugSetting = getSetting.Invoke(category, new object[] { "DebugLog" });
                doubleTapSetting = getSetting.Invoke(category, new object[] { "DoubleTap" });
                doubleTapWindowSetting = getSetting.Invoke(category, new object[] { "DoubleTapWindow" });
                currentValueProp = tValue.GetProperty("CurrentValue");

                connected = true;
                Log.Out(SevenDashesMod.LogPrefix + "Gears settings connected.");
            }
            catch (Exception e)
            {
                gearsMissing = true;
                Log.Warning(SevenDashesMod.LogPrefix + "Gears bridge failed, using defaults: " + e);
            }
        }
    }
}
