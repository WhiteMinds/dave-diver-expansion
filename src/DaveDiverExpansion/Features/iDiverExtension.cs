using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DaveDiverExpansion.Helpers;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Extends the iDiver upgrade panel with custom upgrade items.
/// Data-driven: each UpgradeDef defines a custom SubEquipmentType with its own
/// TID range, max level, price formula, and display value. Effect patches are
/// added separately per upgrade type.
/// </summary>
public static class iDiverExtension
{
    // ========================================================================
    // Upgrade Definitions
    // ========================================================================

    private class UpgradeDef
    {
        public int TypeId;              // custom SubEquipmentType value (100+)
        public int SubEquipBaseTID;     // base TID for SubEquipment entries
        public int IntItemBaseTID;      // base TID for IntegratedItem entries
        public int MaxLevel;
        public Func<int, int> PriceFunc;  // level => price (level 0 always free)
        public string NameKey;          // I18n key for display name
        public string StatusLabelKey;   // I18n key for status label (e.g. "Damage", "Move Speed")
        public string ValueSuffix;      // suffix for display value (e.g. "%", "s")
        public Func<int, float> ValueFunc;  // level => display value for UI
        public StatusDefine StatusType; // UI status icon/label type
        public SubEquipmentType IconSource; // which game type to copy icon from
        public string IconSpriteName;       // if set, load sprite by name instead of IconSource
        public Func<int, int> DroneCountFunc; // level => droneCount (null = 0)
    }

    private static readonly UpgradeDef[] Upgrades =
    {
        new()
        {
            TypeId = 100,
            SubEquipBaseTID = 9900000,
            IntItemBaseTID = 9990000,
            MaxLevel = 5,
            PriceFunc = level => 10000,
            NameKey = "Harpoon Damage Enhancement",
            StatusLabelKey = "Damage",
            ValueSuffix = "",
            ValueFunc = level => level * 8,
            StatusType = StatusDefine.damage,
            IconSource = SubEquipmentType.Harpoon,
        },
        new()
        {
            TypeId = 101,
            SubEquipBaseTID = 9900100,
            IntItemBaseTID = 9990100,
            MaxLevel = 5,
            PriceFunc = level => 5000 * level,   // level 1=5000, level 2=10000, ...
            NameKey = "Movement Speed Enhancement",
            StatusLabelKey = "Move Speed",
            ValueSuffix = "%",
            ValueFunc = level => level * 5,       // +5% per level
            StatusType = StatusDefine.damage,
            IconSource = SubEquipmentType.diving_suit,
        },
        new()
        {
            TypeId = 102,
            SubEquipBaseTID = 9900200,
            IntItemBaseTID = 9990200,
            MaxLevel = 5,
            PriceFunc = level => 5000 * level,   // level 1=5000, level 2=10000, ...
            NameKey = "Booster Speed Enhancement",
            StatusLabelKey = "Booster Speed",
            ValueSuffix = "%",
            ValueFunc = level => level * 5,       // +5% per level
            StatusType = StatusDefine.damage,
            IconSource = SubEquipmentType.o2_tank,
            IconSpriteName = "Booster_Thumbnail",
        },
        new()
        {
            TypeId = 103,
            SubEquipBaseTID = 9900300,
            IntItemBaseTID = 9990300,
            MaxLevel = 5,
            PriceFunc = level => 5000,
            NameKey = "Booster Duration Enhancement",
            StatusLabelKey = "Duration",
            ValueSuffix = "s",
            ValueFunc = level => level * 10,      // +10s per level
            StatusType = StatusDefine.damage,
            IconSource = SubEquipmentType.o2_tank,
            IconSpriteName = "Booster_Thumbnail",
        },
        new()
        {
            TypeId = 104,
            SubEquipBaseTID = 9900400,
            IntItemBaseTID = 9990400,
            MaxLevel = 5,
            PriceFunc = level => 5000 * level,
            NameKey = "Crab Trap Count Enhancement",
            StatusLabelKey = "Trap Count",
            ValueSuffix = "",
            ValueFunc = level => level,            // +1 per level
            StatusType = StatusDefine.crabTrapcount,
            IconSource = SubEquipmentType.crab_trap,
        },
        new()
        {
            TypeId = 105,
            SubEquipBaseTID = 9900500,
            IntItemBaseTID = 9990500,
            MaxLevel = 6,
            PriceFunc = level => 5000 * level,
            NameKey = "Crab Trap Efficiency Enhancement",
            StatusLabelKey = "Catch Time",
            ValueSuffix = "s",
            ValueFunc = level => -level * 10,      // -10s per level
            StatusType = StatusDefine.crabTrapcount,
            IconSource = SubEquipmentType.crab_trap,
        },
        new()
        {
            TypeId = 106,
            SubEquipBaseTID = 9900600,
            IntItemBaseTID = 9990600,
            MaxLevel = 10,
            PriceFunc = level => 10000,
            NameKey = "Drone Count Enhancement",
            StatusLabelKey = "Drone",
            ValueSuffix = "",
            ValueFunc = level => level,            // +1 per level
            StatusType = StatusDefine.dronecount,
            IconSource = SubEquipmentType.drone,
            // NOT using DroneCountFunc — bonus applied via delta tracking in Update
            // (same as Crab Trap Count). DroneCountFunc would cause double counting:
            // once via EquipSubEquip→AddStatus→MakeStatusDic→BaseStatus, and again
            // via Update delta tracking.
        },
    };

    // Set to true to override all upgrade prices to 1 gold (for testing)
    private static readonly bool DebugCheapUpgrades = false;

    // Config
    private static ConfigEntry<bool> _enabled;

    // Cached synthetic objects (created on first access)
    private static readonly Dictionary<int, DR.SubEquipment> _subEquipCache = new();
    private static readonly Dictionary<int, IntegratedItem> _integratedItemCache = new();
    private static bool _initInjected;

    // Cached sprites loaded by name at runtime
    private static readonly Dictionary<string, Sprite> _spriteCache = new();

    // Template data read from AlloyHarpoon at runtime
    private static string _templateUIIcon;
    private static SpecDataBase _templateSpecData;
    private static bool _templateReady;

    public static void Init(ConfigFile config)
    {
        _enabled = config.Bind(
            "iDiverExtension", "Enabled", false,
            "Enable extra iDiver upgrade options (harpoon damage, move speed, booster speed & duration). Disabling hides the UI and removes effects, but preserves your upgrade levels.");

        Plugin.Log.LogInfo($"iDiverExtension initialized (enabled={_enabled.Value})");
    }

    // ========================================================================
    // Lookup Helpers
    // ========================================================================

    // Upper bound for TID recognition — allows loading saves from when MaxLevel was higher
    private const int TID_RANGE = 99;

    /// <summary>Finds the UpgradeDef that owns a given SubEquipment TID, or null.</summary>
    private static UpgradeDef FindBySubEquipTID(int tid)
    {
        foreach (var u in Upgrades)
            if (tid >= u.SubEquipBaseTID && tid <= u.SubEquipBaseTID + TID_RANGE)
                return u;
        return null;
    }

    /// <summary>Finds the UpgradeDef that owns a given IntegratedItem TID, or null.</summary>
    private static UpgradeDef FindByIntItemTID(int tid)
    {
        foreach (var u in Upgrades)
            if (tid >= u.IntItemBaseTID && tid <= u.IntItemBaseTID + TID_RANGE)
                return u;
        return null;
    }

    /// <summary>Finds the UpgradeDef by custom SubEquipmentType id.</summary>
    private static UpgradeDef FindByTypeId(int typeId)
    {
        foreach (var u in Upgrades)
            if (u.TypeId == typeId)
                return u;
        return null;
    }

    /// <summary>
    /// Gets the current upgrade level for a given UpgradeDef from SubEquipmentManager.
    /// Returns 0 if not equipped or not available.
    /// </summary>
    private static int GetLevel(UpgradeDef def)
    {
        try
        {
            var mgr = SingletonNoMono<SubEquipmentManager>.s_Instance;
            if (mgr == null) return 0;

            var equipped = mgr.GetEquipedSubEquipByType((SubEquipmentType)def.TypeId);
            if (equipped == null) return 0;

            int tid = equipped.TID;
            if (tid < def.SubEquipBaseTID || tid > def.SubEquipBaseTID + TID_RANGE) return 0;
            return Math.Min(tid - def.SubEquipBaseTID, def.MaxLevel);
        }
        catch { return 0; }
    }

    // ========================================================================
    // Template & Data Creation
    // ========================================================================

    private static void EnsureTemplate()
    {
        if (_templateReady) return;

        var dm = Singleton<DataManager>._instance;
        if (dm == null) return;

        var alloyHarpoon = dm.GetSubEquipment(3060306);
        _templateUIIcon = alloyHarpoon?.UIIcon ?? "iDiver_Icon_AlloyHarpoonGun";

        var alloyItem = dm.GetIntegratedItem(3013133);
        if (alloyItem?.specData != null)
            _templateSpecData = alloyItem.specData;

        _templateReady = true;
    }

    private static DR.SubEquipment CreateSubEquipment(UpgradeDef def, int level)
    {
        EnsureTemplate();
        int tid = def.SubEquipBaseTID + level;
        int price = level == 0 ? 0 : (DebugCheapUpgrades ? 1 : def.PriceFunc(level));
        int droneCount = def.DroneCountFunc?.Invoke(level) ?? 0;

        return new DR.SubEquipment(
            tID: tid,
            nameTextID: "custom_upgrade",
            descTextID: "custom_upgrade_desc",
            uIIcon: _templateUIIcon ?? "iDiver_Icon_AlloyHarpoonGun",
            subEquipmentType: def.TypeId,
            subEquipmentLevel: level,
            price: price,
            unlockMission: 0,
            maxHP: 0f,
            maxDepthWater: 0f,
            lootboxWeight: 0f,
            overwightThreshold: 0f,
            equipmentItemID: def.IntItemBaseTID + level,
            droneCount: droneCount,
            trapCount: 0
        );
    }

    private static IntegratedItem CreateIntegratedItem(UpgradeDef def, int level)
    {
        EnsureTemplate();
        int tid = def.IntItemBaseTID + level;

        var item = new IntegratedItem();
        item.TID = tid;
        item.ItemLevel = level;
        item.ItemTextID = "custom_upgrade";
        item.ItemDescID = "custom_upgrade_desc";
        item.ItemIcon = _templateUIIcon ?? "iDiver_Icon_AlloyHarpoonGun";
        item.ItemUIIcon = _templateUIIcon ?? "iDiver_Icon_AlloyHarpoonGun";
        item.IntegratedType = 0;
        item.ItemMaxStackCount = 1;
        item.InvenSortOrder = 0;

        // Clone specData from template; set _Damage to display value
        // (MakeStatusDic reads _Damage, but we override in SetSubEquipUIInfo anyway)
        if (_templateSpecData != null)
        {
            var spec = UnityEngine.Object.Instantiate(_templateSpecData);
            spec._Damage = (int)def.ValueFunc(level);
            spec._TID = tid;
            spec._Name = def.NameKey;
            item.specData = spec;
        }

        return item;
    }

    // ========================================================================
    // Harmony Patches — Data Layer (always active for save integrity)
    // ========================================================================

    /// <summary>Intercept SubEquipment lookups for all custom TID ranges.</summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetSubEquipment))]
    static class GetSubEquipment_Patch
    {
        static void Postfix(int tid, ref DR.SubEquipment __result, DataManager __instance)
        {
            if (__result != null) return;
            var def = FindBySubEquipTID(tid);
            if (def == null) return;

            int level = tid - def.SubEquipBaseTID;

            // After init phase, reject levels beyond MaxLevel so that
            // GetSubEquipment(currentTID+1) returns null → correct max-level detection
            if (_initInjected && level > def.MaxLevel)
                return;

            if (!_subEquipCache.TryGetValue(tid, out var cached))
            {
                cached = CreateSubEquipment(def, level);
                _subEquipCache[tid] = cached;

                try
                {
                    var dic = __instance.SubEquipmentDataDic;
                    if (dic != null && !dic.ContainsKey(tid))
                        dic.Add(tid, cached);
                }
                catch { }
            }

            __result = cached;
        }
    }

    /// <summary>Intercept IntegratedItem lookups for all custom TID ranges.</summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetIntegratedItem))]
    static class GetIntegratedItem_Patch
    {
        static void Postfix(int tid, ref IntegratedItem __result, DataManager __instance)
        {
            if (__result != null) return;
            var def = FindByIntItemTID(tid);
            if (def == null) return;

            int level = tid - def.IntItemBaseTID;

            // After init phase, reject levels beyond MaxLevel
            if (_initInjected && level > def.MaxLevel)
                return;

            if (!_integratedItemCache.TryGetValue(tid, out var cached))
            {
                cached = CreateIntegratedItem(def, level);
                _integratedItemCache[tid] = cached;

                try
                {
                    var dic = __instance.IntegratedItemDic;
                    if (dic != null && !dic.ContainsKey(tid))
                        dic.Add(tid, cached);
                }
                catch { }
            }

            __result = cached;
        }
    }

    /// <summary>Auto-equip base level (level 0) for all upgrade types after save loads.</summary>
    [HarmonyPatch(typeof(SubEquipmentManager), nameof(SubEquipmentManager.Init))]
    static class SubEquipInit_Patch
    {
        static void Postfix(SubEquipmentManager __instance)
        {
            if (_initInjected) return;
            // Mark init done BEFORE downgrade — so GetSubEquipment rejects level > MaxLevel,
            // giving correct max-level detection for the downgraded TIDs.
            _initInjected = true;

            foreach (var def in Upgrades)
            {
                try
                {
                    var equipped = __instance.GetEquipedSubEquipByType((SubEquipmentType)def.TypeId);
                    if (equipped != null)
                    {
                        int savedLevel = equipped.TID - def.SubEquipBaseTID;
                        Plugin.Log.LogInfo($"[iDiverExt] Init type {def.TypeId}: equipped TID={equipped.TID}, savedLevel={savedLevel}, MaxLevel={def.MaxLevel}");
                        if (savedLevel > def.MaxLevel)
                        {
                            int clampedTID = def.SubEquipBaseTID + def.MaxLevel;
                            Plugin.Log.LogInfo($"[iDiverExt] Downgrading type {def.TypeId}: level {savedLevel}→{def.MaxLevel}, newTID={clampedTID}");
                            __instance.AddSubEquip(clampedTID);
                            __instance.EquipSubEquip(clampedTID);

                            // Verify downgrade
                            var after = __instance.GetEquipedSubEquipByType((SubEquipmentType)def.TypeId);
                            Plugin.Log.LogInfo($"[iDiverExt] After downgrade: TID={after?.TID}, level={after?.TID - def.SubEquipBaseTID}");
                        }
                        continue;
                    }

                    Plugin.Log.LogInfo($"[iDiverExt] Init type {def.TypeId}: no equipped, adding base level 0");
                    __instance.AddSubEquip(def.SubEquipBaseTID);
                    __instance.EquipSubEquip(def.SubEquipBaseTID);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[iDiverExt] Failed to init base level for type {def.TypeId}: {ex.Message}");
                }
            }
        }
    }

    // ========================================================================
    // Harmony Patches — UI Layer (gated by _enabled)
    // ========================================================================

    /// <summary>Add custom upgrade cells to the iDiver scroll list.</summary>
    [HarmonyPatch(typeof(LobbyEquipUpgradeScrollPanel), nameof(LobbyEquipUpgradeScrollPanel.AddCellData))]
    static class AddCellData_Patch
    {
        static void Postfix(LobbyEquipUpgradeScrollPanel __instance)
        {
            if (_enabled?.Value != true) return;

            foreach (var def in Upgrades)
            {
                try
                {
                    var cellData = new LobbyEquipUpgradeScrollPanel.ScrollerCellData();
                    cellData.subEquiptype = (SubEquipmentType)def.TypeId;
                    __instance.m_ScrollCellDataList.Add(cellData.Cast<IScrollerCellData>());
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[iDiverExt] Failed to add cell for type {def.TypeId}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>Override display info (name, icon, status) for all custom types.</summary>
    [HarmonyPatch(typeof(SubEquipmentManager), nameof(SubEquipmentManager.SetSubEquipUIInfo))]
    static class SetSubEquipUIInfo_Patch
    {
        static void Postfix(SubEquipmentType type)
        {
            var def = FindByTypeId((int)type);
            if (def == null) return;
            if (_enabled?.Value != true) return;

            try
            {
                var mgr = SingletonNoMono<SubEquipmentManager>.s_Instance;
                if (mgr == null) return;

                var uiInfo = mgr.GetSubEquipUIInfo((SubEquipmentType)def.TypeId);
                if (uiInfo == null) return;

                uiInfo.subEquipName = I18n.T(def.NameKey);

                // Set icon: prefer named sprite, fallback to copying from game type
                var icon = GetIconForDef(def, mgr);
                if (icon != null)
                    uiInfo.subEquipIcon = icon;

                // Manually build SubEquipmentStatusInfo (game's GetStausInfo skips zero values)
                int currentLevel = GetLevel(def);
                float currentVal = def.ValueFunc(currentLevel);
                int nextLevel = currentLevel + 1;
                bool isMax = nextLevel > def.MaxLevel;
                float nextVal = isMax ? currentVal : def.ValueFunc(nextLevel);

                uiInfo.subEquipLevel = currentLevel;
                uiInfo.isMaxLevel = isMax;
                uiInfo.subEquipStatus = new SubEquipmentStatusInfo(
                    def.StatusType,
                    currentVal,
                    nextVal,
                    (float)currentLevel,
                    (float)(isMax ? currentLevel : nextLevel)
                );
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[iDiverExt] Failed to set UI info for type {def.TypeId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Override name text and inline status panel in scroll cells for all custom types.
    /// Each scroll cell has an embedded m_UpgradeDetailPanel (LobbyUpgradeEquipDetailPanel)
    /// that shows status label/value — we override those via SetOverride for persistence.
    /// </summary>
    [HarmonyPatch(typeof(LobbyUpgradeEquipScrollCell), nameof(LobbyUpgradeEquipScrollCell.SetUIData))]
    static class SetUIData_Patch
    {
        static void Postfix(LobbyUpgradeEquipScrollCell __instance, SubEquipmentType itemtype)
        {
            var def = FindByTypeId((int)itemtype);
            if (def == null) return;
            if (_enabled?.Value != true) return;

            try
            {
                // Override name text
                var nameField = __instance.itemNameText;
                if (nameField != null)
                {
                    var tmpText = nameField.GetComponentInChildren<TMPro.TMP_Text>();
                    if (tmpText != null)
                        tmpText.text = I18n.T(def.NameKey);
                }

                // Override inline status panel (status label + value)
                OverrideStatusPanel(__instance.m_UpgradeDetailPanel, def);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[iDiverExt] Failed to set scroll cell UI for type {def.TypeId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Override status text, values, title, and icons in the full detail panel
    /// (shown when user clicks an upgrade item).
    /// Prefix re-enables all UIDataText components (in case we disabled them for a
    /// previous custom type), so the original code path works for vanilla items.
    /// Postfix then disables + overwrites for our custom types.
    /// </summary>
    [HarmonyPatch(typeof(IDiverItemDetailPanel), nameof(IDiverItemDetailPanel.SetItemDetailData))]
    static class SetItemDetailData_Patch
    {
        static void Prefix(IDiverItemDetailPanel __instance)
        {
            // Re-enable all UIDataText we may have previously disabled
            RestoreUIDataTexts(__instance.title);
            RestoreUIDataTexts(__instance.maxStatusText);
            RestoreUIDataTexts(__instance.maxStatusValue);
            if (__instance.statusPanel != null)
            {
                RestoreUIDataTexts(__instance.statusPanel.currentStatusText);
                RestoreUIDataTexts(__instance.statusPanel.currentStatusValue);
                RestoreUIDataTexts(__instance.statusPanel.nextStatusText);
                RestoreUIDataTexts(__instance.statusPanel.nextStatusValue);
            }
        }

        static void Postfix(IDiverItemDetailPanel __instance, SubEquipmentType itemtype)
        {
            var def = FindByTypeId((int)itemtype);
            if (def == null) return;
            if (_enabled?.Value != true) return;

            try
            {
                // Override title
                OverrideUIDataText(__instance.title, I18n.T(def.NameKey));

                // Override inline status panel
                OverrideStatusPanel(__instance.statusPanel, def);

                // Override max level display text
                int level = GetLevel(def);
                string label = I18n.T(def.StatusLabelKey);
                string valStr = FormatValue(def.ValueFunc(def.MaxLevel), def.ValueSuffix);
                OverrideUIDataText(__instance.maxStatusText, label);
                OverrideUIDataText(__instance.maxStatusValue, valStr);

                // Override icons
                var mgr = SingletonNoMono<SubEquipmentManager>.s_Instance;
                var icon = mgr != null ? GetIconForDef(def, mgr) : null;
                if (icon != null)
                {
                    if (__instance.itemIcon1 != null)
                        __instance.itemIcon1.sprite = icon;
                    if (__instance.itemIcon2 != null)
                        __instance.itemIcon2.sprite = icon;
                    if (__instance.maxIcon != null)
                        __instance.maxIcon.sprite = icon;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[iDiverExt] SetItemDetailData override failed for type {def.TypeId}: {ex.Message}");
            }
        }
    }

    // ========================================================================
    // UI Helpers
    // ========================================================================

    /// <summary>
    /// Override a UIDataText's displayed content by disabling the UIDataText component
    /// (prevents its Refresh from overwriting our text) and setting TMP_Text directly.
    /// DelegateSupport.ConvertDelegate fails to bridge managed→IL2CPP callbacks for
    /// OverrideTextFunc in this BepInEx 6 version, so we bypass UIDataText entirely.
    /// </summary>
    private static void OverrideUIDataText(Common.UI.UIDataText uiDataText, string text)
    {
        if (uiDataText == null) return;

        // Disable the UIDataText MonoBehaviour so it won't refresh/overwrite our text
        uiDataText.enabled = false;

        var tmp = uiDataText.GetComponentInChildren<TMPro.TMP_Text>();
        if (tmp != null)
            tmp.text = text;
    }

    /// <summary>
    /// Re-enable a UIDataText that we previously disabled. Called in Prefix before
    /// the original method runs, so vanilla items get normal UIDataText behavior.
    /// </summary>
    private static void RestoreUIDataTexts(Common.UI.UIDataText uiDataText)
    {
        if (uiDataText != null)
            uiDataText.enabled = true;
    }

    /// <summary>
    /// Get the icon sprite for a given UpgradeDef. Prefers IconSpriteName (loaded via
    /// Resources.FindObjectsOfTypeAll), falls back to copying from IconSource type.
    /// </summary>
    private static Sprite GetIconForDef(UpgradeDef def, SubEquipmentManager mgr)
    {
        if (!string.IsNullOrEmpty(def.IconSpriteName))
        {
            var sprite = FindSpriteByName(def.IconSpriteName);
            if (sprite != null) return sprite;
        }
        var sourceInfo = mgr.GetSubEquipUIInfo(def.IconSource);
        return sourceInfo?.subEquipIcon;
    }

    private static Sprite FindSpriteByName(string name)
    {
        if (_spriteCache.TryGetValue(name, out var cached))
            return cached;

        var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in allSprites)
        {
            if (s.name == name)
            {
                _spriteCache[name] = s;
                return s;
            }
        }
        _spriteCache[name] = null;
        return null;
    }

    /// <summary>
    /// Override status label and value text on a LobbyUpgradeEquipDetailPanel
    /// (used both inline in scroll cells and in the full detail panel).
    /// </summary>
    private static void OverrideStatusPanel(LobbyUpgradeEquipDetailPanel panel, UpgradeDef def)
    {
        if (panel == null) return;

        int currentLevel = GetLevel(def);
        float currentVal = def.ValueFunc(currentLevel);
        int nextLevel = currentLevel + 1;
        bool isMax = nextLevel > def.MaxLevel;
        float nextVal = isMax ? currentVal : def.ValueFunc(nextLevel);

        string label = I18n.T(def.StatusLabelKey);
        string currentStr = FormatValue(currentVal, def.ValueSuffix);
        string nextStr = FormatValue(nextVal, def.ValueSuffix);

        OverrideUIDataText(panel.currentStatusText, label);
        OverrideUIDataText(panel.currentStatusValue, currentStr);
        OverrideUIDataText(panel.nextStatusText, label);
        OverrideUIDataText(panel.nextStatusValue, nextStr);
    }

    private static string FormatValue(float val, string suffix)
    {
        string num = val == (int)val ? ((int)val).ToString() : val.ToString("0.#");
        return string.IsNullOrEmpty(suffix) ? num : num + suffix;
    }

    // ========================================================================
    // Harmony Patches — Effect Layer (gated by _enabled)
    // ========================================================================

    /// <summary>Harpoon damage bonus: +8 per level.</summary>
    [HarmonyPatch(typeof(HarpoonProjectile), nameof(HarpoonProjectile.BuffedProjectileDamage), MethodType.Getter)]
    static class HarpoonDamage_Patch
    {
        static void Postfix(ref int __result)
        {
            if (_enabled?.Value != true) return;

            int level = GetLevel(Upgrades[0]); // Harpoon Damage Enhancement
            if (level > 0)
                __result += level * 8;
        }
    }

    /// <summary>
    /// Movement speed via BuffDataContainer.AddMoveSpeedParam().
    /// DetermineMoveSpeed() multiplies base speed by GetBuffComponents.MoveSpeedParameter,
    /// which aggregates all entries in MoveSpeedParamDic. This avoids offset hacks and
    /// Prefix/Postfix timing issues. No buff icon is shown (MoveSpeedParamDic is pure data).
    ///
    /// Patching BuffHandler.Start (CallerCount=0, runs once when Dave spawns) to inject
    /// our speed param. We also update it each frame in Update to respond to level changes.
    /// </summary>
    [HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
    static class AllEffects_Patch
    {
        private static bool _loggedMove;
        private static bool _loggedBoostDur;
        private static int _boostSpdLogCount;

        // Custom TID for our speed param entry in MoveSpeedParamDic
        private const int MOVE_SPEED_PARAM_TID = 9900101;

        private static int _lastMoveLevel = -1;

        // Last known remainTime per slot index, used to detect active drain
        private static readonly Dictionary<int, float> _lastRemainTime = new();

        // Track bonus + expected count for AvailableCrabTrapCount overwrite detection
        private static int _lastTrapBonus;
        private static int _expectedTrapCount = -1;
        private static bool _loggedTrapCount;

        // Track bonus + expected count for AvailableLiftDroneCount overwrite detection
        private static int _lastDroneBonus;
        private static int _expectedDroneCount = -1;
        private static bool _loggedDroneCount;

        // Detect new PlayerCharacter instance (new dive) → reset per-dive tracking
        private static PlayerCharacter _lastPlayer;

        static void Prefix(PlayerCharacter __instance)
        {
            try
            {
                // Reset per-dive state when PlayerCharacter instance changes (new dive)
                if (__instance != _lastPlayer)
                {
                    _lastPlayer = __instance;
                    _lastTrapBonus = 0;
                    _expectedTrapCount = -1;
                    _loggedTrapCount = false;
                    _lastDroneBonus = 0;
                    _expectedDroneCount = -1;
                    _loggedDroneCount = false;
                }

                DebugSpawnBooster(__instance);
                ApplyMoveSpeed(__instance);
                ApplyBoosterEffects(__instance);
                ApplyCrabTrapCount(__instance);
                ApplyDroneCount(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[iDiverExt] AllEffects_Patch error: {ex.Message}");
            }
        }

        /// <summary>DEBUG: Press F4 to spawn a Booster in SubHelper inventory.</summary>
        private static void DebugSpawnBooster(PlayerCharacter player)
        {
            if (!DebugCheapUpgrades) return; // only when debug flag is on
            if (!Input.GetKeyDown(KeyCode.F4)) return;

            var inven = player.m_SubHelperItemInven;
            if (inven == null) { Plugin.Log.LogWarning("[iDiverExt] No SubHelperItemInventory"); return; }

            var resMgr = Singleton<ResourceManager>._instance;
            if (resMgr == null) { Plugin.Log.LogWarning("[iDiverExt] No ResourceManager"); return; }

            // Find Booster and BoosterMk2 specs and add both
            var list = resMgr._SubHelperSpecDataList;
            if (list == null) { Plugin.Log.LogWarning("[iDiverExt] No SubHelperSpecDataList"); return; }

            bool foundAny = false;
            for (int i = 0; i < list.Count; i++)
            {
                var spec = list[i];
                if (spec == null) continue;
                if (spec._SubHelperType == SubHelperType.Booster || spec._SubHelperType == SubHelperType.BoosterMk2)
                {
                    inven.StoreInstanceItem(spec);
                    Plugin.Log.LogInfo($"[iDiverExt] DEBUG: Spawned {spec._SubHelperType} TID={spec.TID}, Duration={spec.BatteryDuration}s, Speed={spec.BoosterSpeed}");
                    foundAny = true;
                }
            }
            if (!foundAny) Plugin.Log.LogWarning("[iDiverExt] No Booster specs found in game data");
        }

        static void Postfix()
        {
            // No restore needed — we set _multiplyMoveSpeed to the correct boosted value
            // each frame in ApplyBoosterEffects, based on the captured base value.
        }

        private static void ApplyMoveSpeed(PlayerCharacter player)
        {
            int level = _enabled?.Value == true ? GetLevel(Upgrades[1]) : 0;

            // Only update buff param when level changes (or on disable)
            if (level == _lastMoveLevel) return;
            _lastMoveLevel = level;

            var buffHandler = player.m_PlayerBuffHandler;
            if (buffHandler == null) return;
            var buffData = buffHandler.GetBuffComponents;
            if (buffData == null) return;

            if (level > 0)
            {
                float mult = 1f + Upgrades[1].ValueFunc(level) / 100f;
                buffData.AddMoveSpeedParam(MOVE_SPEED_PARAM_TID, mult);
                if (!_loggedMove)
                {
                    Plugin.Log.LogInfo($"[iDiverExt] MoveSpeed: level={level}, AddMoveSpeedParam({MOVE_SPEED_PARAM_TID}, {mult})");
                    _loggedMove = true;
                }
            }
            else
            {
                buffData.RemoveMoveSpeedParam(MOVE_SPEED_PARAM_TID);
                Plugin.Log.LogInfo($"[iDiverExt] MoveSpeed: disabled, RemoveMoveSpeedParam({MOVE_SPEED_PARAM_TID})");
            }
        }

        private static void ApplyBoosterEffects(PlayerCharacter player)
        {
            if (_enabled?.Value != true) return;

            var inven = player.m_SubHelperItemInven;
            if (inven == null) return;

            // --- Booster speed: per level on _multiplyMoveSpeed ---
            // Read base speed from SubHelperSpecData.BoosterSpeed every frame (immutable).
            // No capture/restore — avoids accumulation and timing issues.
            int spdLevel = GetLevel(Upgrades[2]);
            if (spdLevel > 0)
            {
                var handler = inven.currentUsingHandler;
                if (handler != null && handler._multiplyMoveSpeed > 1f)
                {
                    // Find active booster slot to get base BoosterSpeed from spec
                    float baseSpeed = 0f;
                    var slots = inven.subHelperSlots;
                    if (slots != null)
                    {
                        for (int i = 0; i < slots.Length; i++)
                        {
                            var slot = slots[i];
                            if (slot?.subHelper == null) continue;
                            var helperType = slot.subHelper._SubHelperType;
                            if ((helperType == SubHelperType.Booster || helperType == SubHelperType.BoosterMk2)
                                && slot.remainTime > 0f)
                            {
                                baseSpeed = slot.subHelper.BoosterSpeed;
                                break;
                            }
                        }
                    }

                    if (baseSpeed > 1f)
                    {
                        float bonus = Upgrades[2].ValueFunc(spdLevel) / 100f;
                        handler._multiplyMoveSpeed = baseSpeed * (1f + bonus);
                        if (_boostSpdLogCount++ < 5)
                            Plugin.Log.LogInfo($"[iDiverExt] BoosterSpeed SET: level={spdLevel}, bonus={bonus*100}%, baseFromSpec={baseSpeed}, target={handler._multiplyMoveSpeed}");
                    }
                }
            }

            // --- Booster duration: +10s per level via per-frame drain compensation ---
            // When booster is actively draining (remainTime decreasing), we add back
            // a fraction of the consumed time each frame. If base duration is D and
            // bonus is B, effective drain rate becomes D/(D+B), extending total to D+B.
            int durLevel = GetLevel(Upgrades[3]);
            if (durLevel > 0)
            {
                var slots = inven.subHelperSlots;
                if (slots == null) return;

                float bonus = durLevel * 10f;
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (slot == null) continue;

                    var spec = slot.subHelper;
                    if (spec == null) continue;

                    var helperType = spec._SubHelperType;
                    if (helperType != SubHelperType.Booster && helperType != SubHelperType.BoosterMk2)
                        continue;

                    float current = slot.remainTime;
                    if (current <= 0f) continue;

                    // Detect drain: remainTime decreased since last frame
                    if (_lastRemainTime.TryGetValue(i, out float last) && current < last)
                    {
                        float drained = last - current; // how much the game consumed this frame
                        float baseDuration = spec.BatteryDuration;
                        float totalDuration = baseDuration + bonus;
                        if (totalDuration > 0f)
                        {
                            // Slow drain so total duration = baseDuration + bonus.
                            // Effective drain per frame = drained * baseDuration / totalDuration
                            // Compensate = drained - effective = drained * bonus / totalDuration
                            float compensate = drained * bonus / totalDuration;
                            slot.remainTime = current + compensate;

                            if (!_loggedBoostDur)
                            {
                                Plugin.Log.LogInfo($"[iDiverExt] BoosterDuration: level={durLevel}, base={baseDuration}s, bonus={bonus}s, drained={drained}, compensate={compensate}, totalDur={totalDuration}");
                                _loggedBoostDur = true;
                            }
                        }
                    }

                    _lastRemainTime[i] = slot.remainTime;
                }
            }
        }

        /// <summary>
        /// Crab trap count: +1 available trap per level.
        /// AvailableCrabTrapCount getter/setter are both CallerCount=0 (IL2CPP-inlined).
        /// The game sets the base count from SubEquipment.TrapCount at some point after
        /// PlayerCharacter creation (possibly after the first Update frame). We track
        /// the expected count and detect when the game overwrites our value (init) vs
        /// normal decrements (trap placement), re-applying the bonus as needed.
        /// </summary>
        private static void ApplyCrabTrapCount(PlayerCharacter player)
        {
            int wantBonus = _enabled?.Value == true ? GetLevel(Upgrades[4]) : 0;
            int current = player.AvailableCrabTrapCount;

            // Detect if game overwrote our value (e.g., dive init setting base count)
            if (_expectedTrapCount >= 0 && current != _expectedTrapCount)
            {
                int diff = _expectedTrapCount - current;
                if (diff == 1)
                {
                    // Trap placement (dec by 1) — our bonus is still in effect
                }
                else
                {
                    // Game overwrite (init or other) — our bonus was lost
                    _lastTrapBonus = 0;
                }
            }

            // Apply/adjust bonus
            int delta = wantBonus - _lastTrapBonus;
            if (delta != 0)
            {
                player.AvailableCrabTrapCount = current + delta;
                _lastTrapBonus = wantBonus;
                if (!_loggedTrapCount)
                {
                    Plugin.Log.LogInfo($"[iDiverExt] CrabTrapCount: bonus={wantBonus}, delta={delta}, {current}→{player.AvailableCrabTrapCount}");
                    _loggedTrapCount = true;
                }
            }

            _expectedTrapCount = player.AvailableCrabTrapCount;
        }

        /// <summary>
        /// Drone count: +1 available drone per level.
        /// Same approach as crab trap count — delta tracking on the backing field.
        /// We intentionally do NOT set SubEquipment.DroneCount (DroneCountFunc=null)
        /// to avoid double counting via EquipSubEquip→AddStatus→MakeStatusDic.
        /// </summary>
        private static void ApplyDroneCount(PlayerCharacter player)
        {
            int wantBonus = _enabled?.Value == true ? GetLevel(Upgrades[6]) : 0;
            int current = player.AvailableLiftDroneCount;

            // Detect game overwrite (same pattern as crab trap)
            if (_expectedDroneCount >= 0 && current != _expectedDroneCount)
            {
                int diff = _expectedDroneCount - current;
                if (diff != 1)
                    _lastDroneBonus = 0;
            }

            int delta = wantBonus - _lastDroneBonus;
            if (delta != 0)
            {
                player.AvailableLiftDroneCount = current + delta;
                _lastDroneBonus = wantBonus;
                if (!_loggedDroneCount)
                {
                    Plugin.Log.LogInfo($"[iDiverExt] DroneCount: bonus={wantBonus}, delta={delta}, {current}→{player.AvailableLiftDroneCount}");
                    _loggedDroneCount = true;
                }
            }

            _expectedDroneCount = player.AvailableLiftDroneCount;
        }
    }

    /// <summary>
    /// Crab trap efficiency: reduce catch delay by 1s per level.
    /// CrabTrapObject.Update() (CallerCount=0) checks state==SetUp, accumulates
    /// elapsedTime, and transitions to Completed when elapsedTime >= targetTime.
    /// targetTime (offset 0x64) is set from GameConstValueInfo.CrabTrapCatchDelay_Sec
    /// in Init (CallerCount=1, inlined — can't patch Init directly).
    /// We read the base delay from DataManager.GameConstValue.CrabTrapCatchDelay_Sec
    /// and set targetTime = base - level (idempotent, safe to call every frame).
    /// </summary>
    [HarmonyPatch(typeof(CrabTrapObject), nameof(CrabTrapObject.Update))]
    static class CrabTrapEfficiency_Patch
    {
        private static bool _logged;
        private static int _cachedBaseDelay; // cached from GameConstValueInfo

        static void Prefix(CrabTrapObject __instance)
        {
            if (_enabled?.Value != true) return;

            int level = GetLevel(Upgrades[5]); // Crab Trap Efficiency Enhancement
            if (level <= 0) return;

            try
            {
                // Read and cache the base catch delay from game data
                if (_cachedBaseDelay <= 0)
                {
                    var dm = Singleton<DataManager>._instance;
                    if (dm == null) return;
                    var constVal = dm.GameConstValue;
                    if (constVal == null) return;
                    _cachedBaseDelay = constVal.CrabTrapCatchDelay_Sec;
                    Plugin.Log.LogInfo($"[iDiverExt] CrabTrapCatchDelay_Sec base value = {_cachedBaseDelay}s");
                }

                // Match UpgradeDef.ValueFunc: -10s per level
                float wantTarget = Math.Max(1f, _cachedBaseDelay - level * 10f);

                var ptr = IL2CPP.Il2CppObjectBaseToPtrNotNull(__instance);
                if (ptr == IntPtr.Zero) return;

                unsafe
                {
                    float* targetTimePtr = (float*)((nint)ptr + 0x64);
                    float current = *targetTimePtr;

                    // Idempotent: only write if different from desired value
                    if (Math.Abs(current - wantTarget) > 0.01f)
                    {
                        *targetTimePtr = wantTarget;
                        if (!_logged)
                        {
                            Plugin.Log.LogInfo($"[iDiverExt] CrabTrapEfficiency: level={level}, base={_cachedBaseDelay}s, target={wantTarget}s (was {current}s)");
                            _logged = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[iDiverExt] CrabTrapEfficiency error: {ex.Message}");
            }
        }
    }
}
