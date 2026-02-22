using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Helpers;

/// <summary>
/// Mod UI language override.
/// Auto: detect from game language (Harmony-cached) or OS language.
/// Chinese/English: force specific language.
/// </summary>
public enum ModLanguage { Auto, Chinese, English }

/// <summary>
/// Lightweight i18n helper. English is the default; Chinese translations
/// are looked up from an internal dictionary. Call T("key") to translate.
/// </summary>
public static class I18n
{
    private static readonly Dictionary<string, string> ZhCn = new()
    {
        // Panel
        ["DaveDiverExpansion Settings"] = "DaveDiverExpansion 设置",

        // Sections
        ["AutoPickup"] = "自动拾取",
        ["ConfigUI"] = "配置界面",

        // AutoPickup entries
        ["Enabled"] = "启用",
        ["AutoPickupFish"] = "自动拾取鱼",
        ["AutoPickupItems"] = "自动拾取物品",
        ["AutoOpenChests"] = "自动开启宝箱",
        ["PickupRadius"] = "拾取半径",

        // ConfigUI entries
        ["ToggleKey"] = "切换按键",
        ["Language"] = "语言",

        // DiveMap entries
        ["DiveMap"] = "潜水地图",
        ["ShowEscapePods"] = "显示逃生点",
        ["ShowFish"] = "显示鱼类",
        ["ShowItems"] = "显示物品",
        ["ShowChests"] = "显示宝箱",
        ["MapSize"] = "小地图大小",
        ["MapOpacity"] = "小地图透明度",
        ["MiniMapZoom"] = "小地图缩放",
        ["MiniMapEnabled"] = "显示小地图",
        ["MiniMapPosition"] = "小地图位置",
        ["MiniMapOffsetX"] = "小地图水平偏移",
        ["MiniMapOffsetY"] = "小地图垂直偏移",
        ["ShowOres"] = "显示矿石",
        ["TopRight"] = "右上",
        ["TopLeft"] = "左上",
        ["BottomRight"] = "右下",
        ["BottomLeft"] = "左下",

        // Debug entries
        ["Debug"] = "调试",
        ["DebugLog"] = "调试日志",

        // QuickSceneSwitch entries
        ["QuickSceneSwitch"] = "快速切换场景",

        // KeyCode binding
        ["Press a key..."] = "请按键...",
    };

    public static ConfigEntry<ModLanguage> LanguageSetting;

    /// <summary>
    /// Tracks whether Chinese was ever seen from the Harmony getter patch.
    /// The getter fires for many unrelated save-data fields (returning garbage ints like
    /// 999, 0, 10, etc.), so we only trust it when we see Chinese specifically.
    /// </summary>
    internal static bool SeenChinese;

    private static bool _langDebugDone;

    /// <summary>
    /// Returns true when the UI should display Chinese text.
    /// Priority: ConfigEntry override > Harmony "ever seen Chinese" > OS language.
    /// </summary>
    public static bool IsChinese()
    {
        if (LanguageSetting?.Value == ModLanguage.Chinese) return true;
        if (LanguageSetting?.Value == ModLanguage.English) return false;

        // Auto mode: if Harmony has ever seen Chinese from the getter, trust it
        if (SeenChinese)
        {
            LogAutoDetection("game", "SeenChinese=true", true);
            return true;
        }

        // Fallback: OS language
        var lang = Application.systemLanguage;
        bool fallback = lang == SystemLanguage.Chinese
            || lang == SystemLanguage.ChineseSimplified
            || lang == SystemLanguage.ChineseTraditional;
        LogAutoDetection("OS", $"systemLanguage={lang}", fallback);
        return fallback;
    }

    private static void LogAutoDetection(string source, string detail, bool isChinese)
    {
        if (_langDebugDone) return;
        try
        {
            if (Features.DiveMap.DebugLog?.Value == true)
            {
                Plugin.Log.LogInfo($"[I18n] Auto detect via {source}: {detail} → isChinese={isChinese}");
                _langDebugDone = true;
            }
        }
        catch { }
    }

    /// <summary>
    /// Translate a key. Returns the Chinese string if IsChinese() and a
    /// translation exists; otherwise returns the key itself (English).
    /// </summary>
    public static string T(string key)
    {
        if (IsChinese() && ZhCn.TryGetValue(key, out var zh))
            return zh;
        return key;
    }
}

/// <summary>
/// Harmony patch on SaveUserOptions.get_CurrentLanguage.
/// The IL2CPP getter fires for many unrelated save-data field reads, producing garbage values.
/// We only set SeenChinese=true when Chinese/ChineseTraditional appears — this is a reliable
/// positive signal. All other values are ignored because they're indistinguishable from garbage.
/// </summary>
[HarmonyPatch(typeof(DR.Save.SaveUserOptions), nameof(DR.Save.SaveUserOptions.CurrentLanguage), MethodType.Getter)]
static class GameLanguageCachePatch
{
    static void Postfix(DR.Save.Languages __result)
    {
        if (__result == DR.Save.Languages.Chinese || __result == DR.Save.Languages.ChineseTraditional)
        {
            if (!I18n.SeenChinese)
            {
                Plugin.Log.LogInfo($"[I18n] Chinese detected from game: {__result}({(int)__result})");
                I18n.SeenChinese = true;
            }
        }
    }
}
