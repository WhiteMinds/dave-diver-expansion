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
        ["MapSize"] = "地图大小",
        ["MapOpacity"] = "地图透明度",
        ["MiniMapZoom"] = "小地图缩放",
    };

    public static ConfigEntry<ModLanguage> LanguageSetting;

    /// <summary>Cached from Harmony Postfix on SaveUserOptions.get_CurrentLanguage.</summary>
    internal static DR.Save.Languages? CachedGameLanguage;

    /// <summary>
    /// Returns true when the UI should display Chinese text.
    /// Priority: ConfigEntry override > Harmony-cached game language > OS language.
    /// </summary>
    public static bool IsChinese()
    {
        if (LanguageSetting?.Value == ModLanguage.Chinese) return true;
        if (LanguageSetting?.Value == ModLanguage.English) return false;

        // Auto mode: prefer game's own language (Harmony-cached)
        if (CachedGameLanguage.HasValue)
        {
            return CachedGameLanguage.Value == DR.Save.Languages.Chinese
                || CachedGameLanguage.Value == DR.Save.Languages.ChineseTraditional;
        }

        // Fallback: OS language (before game has read its settings)
        var lang = Application.systemLanguage;
        return lang == SystemLanguage.Chinese
            || lang == SystemLanguage.ChineseSimplified
            || lang == SystemLanguage.ChineseTraditional;
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
/// Harmony patch that captures the game's current language setting whenever
/// it is read. The game's UI/font/save systems call this getter frequently,
/// so the cached value will be available before the user opens our config panel.
/// </summary>
[HarmonyPatch(typeof(DR.Save.SaveUserOptions), nameof(DR.Save.SaveUserOptions.CurrentLanguage), MethodType.Getter)]
static class GameLanguageCachePatch
{
    static void Postfix(DR.Save.Languages __result)
    {
        I18n.CachedGameLanguage = __result;
    }
}
