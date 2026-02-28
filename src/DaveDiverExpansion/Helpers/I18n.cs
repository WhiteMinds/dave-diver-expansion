using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace DaveDiverExpansion.Helpers;

/// <summary>
/// Mod UI language override.
/// Auto: detect from game language (SaveSystem API) or OS language.
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
        ["ShowDistantFish"] = "显示远处鱼",
        ["MarkerScale"] = "标记大小",
        // Legend labels
        ["Player"] = "玩家",
        ["Escape Point"] = "逃生点",
        ["Aggressive Fish"] = "攻击性鱼",
        ["Normal Fish"] = "普通鱼",
        ["Catchable Fish"] = "可捕捉鱼",
        ["Item"] = "物品",
        ["Ammo Box"] = "弹药箱",
        ["Chest"] = "宝箱",
        ["O2 Chest"] = "氧气箱",
        ["Material Chest"] = "材料箱",
        ["Ore"] = "矿石",
        ["ShowCrabTraps"] = "显示渔笼位置",
        ["Show fish trap spot markers on the map"] = "在地图上显示可放置渔笼的岩石缝隙标记",
        ["Trap Spot"] = "渔笼位置",
        // Big map help panel
        ["Scroll to Zoom"] = "滚轮缩放",
        ["Drag to Pan"] = "拖拽平移",
        ["Close"] = "关闭",
        ["TopRight"] = "右上",
        ["TopLeft"] = "左上",
        ["BottomRight"] = "右下",
        ["BottomLeft"] = "左下",

        // iDiver extension
        ["iDiverExtension"] = "更多 iDiver 升级选项",
        ["Harpoon Damage Enhancement"] = "鱼叉伤害强化",
        ["Movement Speed Enhancement"] = "移动速度强化",
        ["Booster Speed Enhancement"] = "推进器速度强化",
        ["Booster Duration Enhancement"] = "推进器持续时间强化",
        ["Enable extra iDiver upgrade options (harpoon damage, move speed, booster speed & duration). Disabling hides the UI and removes effects, but preserves your upgrade levels."] = "启用额外的 iDiver 升级选项（鱼叉伤害、移动速度、推进器速度和持续时间）。关闭后将隐藏 UI 并移除效果，但不会重置已升级的等级。",
        // iDiver status labels
        ["Damage"] = "伤害",
        ["Move Speed"] = "移动速度",
        ["Booster Speed"] = "推进速度",
        ["Duration"] = "持续时间",

        // Debug entries
        ["Debug"] = "调试",
        ["DebugLog"] = "调试日志",

        // QuickSceneSwitch entries
        ["QuickSceneSwitch"] = "快速切换场景",

        // Config descriptions
        ["Key to open/close the in-game settings panel"] = "打开/关闭游戏内设置面板的按键",
        ["UI language (Auto detects from game/system)"] = "界面语言（Auto 自动检测游戏/系统语言）",
        ["Open the scene-switch menu with a hotkey (no need to walk to the exit). WARNING: Using during cutscenes/story events may cause missions to be skipped or unexpected behavior."] = "按快捷键打开场景切换菜单（无需走到出口）。⚠️注意：在过场动画/剧情事件期间使用可能导致任务被跳过或出现意外情况。",
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
        ["Show normal fish markers on the map (non-aggressive, non-catchable). Note: some large fish like tuna patrol fixed routes and can deal contact damage, but are classified as normal since they don't actively chase the player."] = "在地图上显示普通鱼标记（非攻击性、非可捕捉）。注意：部分大型鱼（如金枪鱼）沿固定路线巡游，接触会造成伤害，但因不会主动追击玩家而归为普通鱼。",
        ["Show aggressive fish markers on the map. These fish actively attack or chase the player (e.g. sharks, jellyfish, lionfish, triggerfish)."] = "在地图上显示攻击性鱼标记。这些鱼会主动攻击或追击玩家（如鲨鱼、水母、狮子鱼、炮弹鱼）。",
        ["Show catchable fish markers on the map. These fish flee from the player and can be caught with special tools (e.g. shrimp, seahorse)."] = "在地图上显示可捕捉鱼标记。这些鱼会逃离玩家，可用特殊工具捕捉（如虾、海马）。",
        ["Show item markers on the map"] = "在地图上显示物品标记",
        ["Show chest markers on the map"] = "在地图上显示宝箱标记",
        ["Scale multiplier for all map markers"] = "所有地图标记的缩放倍率",
        ["Show markers for distant fish that are streamed out by the game (frozen at last known position)"] = "显示被游戏流式卸载的远处鱼标记（冻结在最后已知位置）",
        ["Enable verbose debug logging for DiveMap diagnostics"] = "启用详细的 DiveMap 调试日志",

        // Reset button
        ["Reset All Settings"] = "重置所有设置",
        ["Confirm Reset?"] = "确认重置？",

        // KeyCode binding
        ["Press a key..."] = "请按键...",
    };

    public static ConfigEntry<ModLanguage> LanguageSetting;

    private static bool _langDebugDone;

    /// <summary>
    /// Returns true when the UI should display Chinese text.
    /// Priority: ConfigEntry override > game SaveSystem API > OS language.
    /// </summary>
    public static bool IsChinese()
    {
        if (LanguageSetting?.Value == ModLanguage.Chinese) return true;
        if (LanguageSetting?.Value == ModLanguage.English) return false;

        // Auto mode: query game's SaveSystem directly
        try
        {
            var saveSystem = Singleton<DR.Save.SaveSystem>._instance;
            if (saveSystem != null)
            {
                var optMgr = saveSystem.UserOptionManager;
                if (optMgr != null)
                {
                    var lang = optMgr.CurrentLanguage;
                    bool fromGame = lang == DR.Save.Languages.Chinese
                        || lang == DR.Save.Languages.ChineseTraditional;
                    LogAutoDetection("SaveSystem", $"CurrentLanguage={lang}({(int)lang})", fromGame);
                    return fromGame;
                }
            }
        }
        catch { }

        // Fallback: OS language (SaveSystem not ready yet)
        var sysLang = Application.systemLanguage;
        bool fallback = sysLang == SystemLanguage.Chinese
            || sysLang == SystemLanguage.ChineseSimplified
            || sysLang == SystemLanguage.ChineseTraditional;
        LogAutoDetection("OS", $"systemLanguage={sysLang}({(int)sysLang})", fallback);
        return fallback;
    }

    private static void LogAutoDetection(string source, string detail, bool isChinese)
    {
        if (_langDebugDone) return;
        try
        {
            Plugin.Log.LogInfo($"[I18n] Auto detect via {source}: {detail} → isChinese={isChinese}");
            _langDebugDone = true;
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
