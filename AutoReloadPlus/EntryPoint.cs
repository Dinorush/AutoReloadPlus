using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using AutoReloadPlus.Utils;

namespace AutoReloadPlus
{
    [BepInPlugin("Dinorush." + MODNAME, MODNAME, "1.1.2")]
    [BepInDependency("dev.gtfomodding.gtfo-api", BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class EntryPoint : BasePlugin
    {
        public const string MODNAME = "AutoReloadPlus";

        public override void Load()
        {
            ARPLogger.Log("Loading " + MODNAME);
            Configuration.Init();

            new Harmony(MODNAME).PatchAll(typeof(AutoReloadPatch));

            ARPLogger.Log("Loaded " + MODNAME);
        }
    }
}