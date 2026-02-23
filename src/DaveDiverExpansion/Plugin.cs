using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using DaveDiverExpansion.Features;

namespace DaveDiverExpansion;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    private Harmony _harmony;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"Loading {MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION}");

        // Initialize features
        AutoPickup.Init(Config);
        DiveMap.Init(Config);
        QuickSceneSwitch.Init(Config);
        ConfigUI.Init(Config); // Must be after other features so it discovers their ConfigEntries

        // Apply Harmony patches
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll();

        Log.LogInfo("Plugin loaded. Patches applied.");
    }
}

internal static class MyPluginInfo
{
    public const string PLUGIN_GUID = "com.davediver.expansion";
    public const string PLUGIN_NAME = "DaveDiverExpansion";
    public const string PLUGIN_VERSION = "1.2.1";
}
