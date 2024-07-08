using System.IO;
using BepInEx;
using BepInEx.Configuration;
using GTFO.API.Utilities;

namespace AutoReloadPlus
{
    internal static class Configuration
    {
        public static int clicksToReload = 1;
        public static bool clicksBypassDelay = true;
        public static bool autoReloadAimingInstant = false;
        public static bool sprintHaltsReload = false;
        public static bool interactHaltsReload = false;

        public static float autoReloadDelayHoldAuto = -1;
        public static float autoReloadDelayAuto = 2f;
        public static bool autoReloadBypassDelayAuto = true;

        public static float autoReloadDelaySemi = 2f;
        public static bool autoReloadBypassDelaySemi = true;

        private static ConfigFile configFile;

        internal static void Init()
        {
            configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, EntryPoint.MODNAME + ".cfg"), saveOnInit: true);
            BindAll(configFile);
            LiveEditListener listener = LiveEdit.CreateListener(Paths.ConfigPath, EntryPoint.MODNAME + ".cfg", false);
            listener.FileChanged += OnFileChanged;
        }

        private static void OnFileChanged(LiveEditEventArgs _)
        {
            configFile.Reload();
            string section = "Base Settings";
            clicksToReload = (int)configFile[section, "Clicks to Reload"].BoxedValue;
            clicksBypassDelay = (bool)configFile[section, "Clicks Bypass Cooldown"].BoxedValue;
            autoReloadAimingInstant = (bool)configFile[section, "Aim Triggers Auto Reload"].BoxedValue;
            sprintHaltsReload = (bool)configFile[section, "Sprint Halts Auto Reload"].BoxedValue;
            interactHaltsReload = (bool)configFile[section, "Interact Halts Auto Reload"].BoxedValue;

            section = "Full-Auto Weapons";
            autoReloadDelayHoldAuto = (float)configFile[section, "Held Auto Reload Delay"].BoxedValue;
            autoReloadDelayAuto = (float)configFile[section, "Auto Reload Delay"].BoxedValue;
            autoReloadBypassDelayAuto = (bool)configFile[section, "Auto Reloads Bypass Cooldown"].BoxedValue;

            section = "Semi-Auto/Burst Weapons";
            autoReloadDelaySemi = (float)configFile[section, "Auto Reload Delay"].BoxedValue;
            autoReloadBypassDelaySemi = (bool)configFile[section, "Auto Reload Bypasses Cooldown"].BoxedValue;
        }

        private static void BindAll(ConfigFile config)
        {
            string section = "Base Settings";
            clicksToReload = config.Bind(section, "Clicks to Reload", clicksToReload, "The number of clicks required to trigger a reload while out of ammo.\nThe vanilla Auto Reload feature requires two clicks. Consider disabling it if setting this value above two.\nA value of 0 or less will reload immediately.").Value;
            clicksBypassDelay = config.Bind(section, "Clicks Bypass Cooldown", clicksBypassDelay, "Clicking will count towards a reload without waiting for shot cooldown.\nThe vanilla Auto Reload feature waits for this cooldown, although it can be bypassed by reloading manually.").Value;
            autoReloadAimingInstant = config.Bind(section, "Aim Triggers Auto Reload", autoReloadAimingInstant, "Aiming down sights will instantly trigger a reload when out of ammo.").Value;
            sprintHaltsReload = config.Bind(section, "Sprint Halts Auto Reload", sprintHaltsReload, "If a sprint is performed while out of ammo, Auto Reload Delays will halt until the next reload.\nIf disabled, Auto Reload Delays will reset their timers and continue after the sprint has ended.").Value;
            interactHaltsReload = config.Bind(section, "Interact Halts Auto Reload", sprintHaltsReload, "If an interact is attempted while out of ammo, Auto Reload Delays will halt until the next reload.\nIf disabled, Auto Reload Delays will reset their timers and continue after the interact attempt has ended.").Value;

            section = "Full-Auto Weapons";
            autoReloadDelayHoldAuto = config.Bind(section, "Held Auto Reload Delay", autoReloadDelayHoldAuto, "Time in seconds before an automatic reload occurs when out of ammo and holding down the trigger.\nSetting to a value less than 0 disables this.").Value;
            autoReloadDelayAuto = config.Bind(section, "Auto Reload Delay", autoReloadDelayAuto, "Time in seconds before an automatic reload occurs when out of ammo.\nSetting to a value less than 0 disables this.").Value;
            autoReloadBypassDelayAuto = config.Bind(section, "Auto Reloads Bypass Cooldown", autoReloadBypassDelayAuto, "Auto Reload Delays will bypass shot cooldown, starting their timers the moment the last shot is fired.").Value;

            section = "Semi-Auto/Burst Weapons";
            autoReloadDelaySemi = config.Bind(section, "Auto Reload Delay", autoReloadDelaySemi, "Time in seconds before an automatic reload occurs when out of ammo.\nSetting to a value less than 0 disables this.").Value;
            autoReloadBypassDelaySemi = config.Bind(section, "Auto Reload Bypasses Cooldown", autoReloadBypassDelaySemi, "Auto Reload Delay will bypass shot cooldown, starting its timer the moment the last shot is fired.").Value;
        }
    }
}
