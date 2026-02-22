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
        ["ShowFish"] = "显示普通鱼",
        ["ShowAggressiveFish"] = "显示攻击性鱼",
        ["ShowCatchableFish"] = "显示可捕捉鱼",
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
        ["MarkerScale"] = "标记大小",
        // Legend labels
        ["Player"] = "玩家",
        ["Escape Point"] = "逃生点",
        ["Aggressive Fish"] = "攻击性鱼",
        ["Normal Fish"] = "普通鱼",
        ["Catchable Fish"] = "可捕捉鱼",
        ["Item"] = "物品",
        ["Chest"] = "宝箱",
        ["O2 Chest"] = "氧气箱",
        ["Material Chest"] = "材料箱",
        ["Ore"] = "矿石",
        // Big map help panel
        ["Scroll to Zoom"] = "滚轮缩放",
        ["Drag to Pan"] = "拖拽平移",
        ["Close"] = "关闭",
        ["TopRight"] = "右上",
        ["TopLeft"] = "左上",
        ["BottomRight"] = "右下",
        ["BottomLeft"] = "左下",

        // Debug entries
        ["Debug"] = "调试",
        ["DebugLog"] = "调试日志",

        // QuickSceneSwitch entries
        ["QuickSceneSwitch"] = "快速切换场景",

        // Config descriptions
        ["Key to open/close the in-game settings panel"] = "打开/关闭游戏内设置面板的按键",
        ["UI language (Auto detects from game/system)"] = "界面语言（Auto 自动检测游戏/系统语言）",
        ["Open the scene-switch menu with a hotkey (no need to walk to the exit)"] = "按快捷键打开场景切换菜单（无需走到出口）",
        ["Key to open/close the scene-switch menu"] = "打开/关闭场景切换菜单的按键",
        ["Enable automatic item pickup while diving"] = "潜水时启用自动拾取物品",
        ["Auto-pickup dead fish"] = "自动拾取死鱼",
        ["Auto-pickup dropped items"] = "自动拾取掉落物品",
        ["Auto-open treasure chests"] = "自动开启宝箱",
        ["Radius around the player to auto-pick items (in game units)"] = "自动拾取物品的范围半径（游戏单位）",
        ["Enable the dive map HUD"] = "启用潜水地图 HUD",
        ["Key to toggle the enlarged map view"] = "切换大地图视图的按键",
        ["Show the minimap overlay during diving"] = "潜水时显示小地图",
        ["Screen corner for the minimap"] = "小地图在屏幕上的位置",
        ["Minimap horizontal offset from screen edge"] = "小地图距屏幕边缘的水平偏移",
        ["Minimap vertical offset from screen edge"] = "小地图距屏幕边缘的垂直偏移",
        ["Minimap size as fraction of screen height"] = "小地图大小（占屏幕高度的比例）",
        ["Minimap zoom level (higher = more zoomed in)"] = "小地图缩放级别（越大越放大）",
        ["Minimap opacity"] = "小地图透明度",
        ["Show escape pod/mirror markers on the map"] = "在地图上显示逃生点标记",
        ["Show ore/mineral markers on the map"] = "在地图上显示矿石标记",
        ["Show normal fish markers on the map (non-aggressive, non-catchable)"] = "在地图上显示普通鱼标记（非攻击性、非可捕捉）",
        ["Show aggressive fish markers on the map (e.g. sharks, piranhas)"] = "在地图上显示攻击性鱼标记（如鲨鱼、食人鱼）",
        ["Show catchable fish markers on the map (e.g. shrimp, seahorse)"] = "在地图上显示可捕捉鱼标记（如虾、海马）",
        ["Show item markers on the map"] = "在地图上显示物品标记",
        ["Show chest markers on the map"] = "在地图上显示宝箱标记",
        ["Scale multiplier for all map markers"] = "所有地图标记的缩放倍率",
        ["Enable verbose debug logging for DiveMap diagnostics"] = "启用详细的 DiveMap 调试日志",

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
