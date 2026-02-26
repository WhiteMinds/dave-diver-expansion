using System;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Temporary debug hooks for investigating Godzilla figurine spawning.
/// Logs all SpawnerChestItem_GodzillaFigure instances and their loot status.
/// </summary>
public static class SaveDebug
{
    private static float _lastScanTime;
    private static bool _scanned;

    public static void Init()
    {
        try
        {
            Application.add_logMessageReceived(
                new Action<string, string, LogType>(OnUnityLog));
            Plugin.Log.LogInfo("[SaveDebug] Registered Unity log listener");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SaveDebug] Failed to register log listener: {ex}");
        }
    }

    private static void OnUnityLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            Plugin.Log.LogError($"[SaveDebug][Unity {type}] {message}");
            if (!string.IsNullOrEmpty(stackTrace))
                Plugin.Log.LogError($"[SaveDebug][Unity StackTrace] {stackTrace}");
        }
    }

    // ============================================================
    // Hook SpawnerChestItem_GodzillaFigure.Start to log each chest
    // ============================================================
    [HarmonyPatch(typeof(SpawnerChestItem_GodzillaFigure), nameof(SpawnerChestItem_GodzillaFigure.Start))]
    static class FigureChestStartPatch
    {
        static void Prefix(SpawnerChestItem_GodzillaFigure __instance)
        {
            try
            {
                var go = __instance.gameObject;
                var pos = __instance.transform.position;
                var uid = __instance.UniqueID;
                var tid = __instance.TargetSpawnItemListTID;
                var scene = go.scene.name;

                Plugin.Log.LogWarning(
                    $"[FigureChest] Start: scene={scene} uid={uid} " +
                    $"dropListTID={tid} " +
                    $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                    $"active={go.activeSelf} name={go.name}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[FigureChest] Prefix error: {ex}");
            }
        }
    }

    // ============================================================
    // Hook SaveData.HaveBeenLooted to trace all loot checks
    // ============================================================
    [HarmonyPatch(typeof(SaveData), nameof(SaveData.HaveBeenLooted))]
    static class HaveBeenLootedPatch
    {
        static void Postfix(int id, bool __result)
        {
            // Only log figurine-related IDs (1010301-1010320)
            if (id >= 1010301 && id <= 1010320)
            {
                Plugin.Log.LogInfo($"[FigureChest] HaveBeenLooted({id}) = {__result}");
            }
        }
    }

    // ============================================================
    // Periodic scan: use PlayerCharacter.Update to trigger scan once
    // ============================================================
    [HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
    static class PeriodicScanPatch
    {
        static void Postfix()
        {
            if (_scanned) return;

            var t = Time.time;
            if (t - _lastScanTime < 5f) return;
            _lastScanTime = t;

            // Only scan when we have a player in a dive scene
            if (t < 10f) return;

            _scanned = true;
            ScanAllFigureChests();
        }
    }

    static void ScanAllFigureChests()
    {
        try
        {
            // FindObjectsOfType(includeInactive: true) to catch SetActive(false) chests
            var all = UnityEngine.Object.FindObjectsOfType<SpawnerChestItem_GodzillaFigure>(true);
            Plugin.Log.LogWarning($"[FigureChest] === SCAN: Found {all.Count} SpawnerChestItem_GodzillaFigure in scene ===");

            for (int i = 0; i < all.Count; i++)
            {
                var chest = all[i];
                if (chest == null) continue;

                try
                {
                    var go = chest.gameObject;
                    var pos = chest.transform.position;
                    var uid = chest.UniqueID;
                    var tid = chest.TargetSpawnItemListTID;
                    var scene = go.scene.name;
                    var active = go.activeSelf;
                    var activeH = go.activeInHierarchy;

                    // Check parent hierarchy for inactive
                    string parentInfo = "";
                    var parent = chest.transform.parent;
                    if (parent != null)
                    {
                        parentInfo = $"parent={parent.name}(active={parent.gameObject.activeSelf})";
                        var pp = parent.parent;
                        if (pp != null)
                            parentInfo += $" grandparent={pp.name}(active={pp.gameObject.activeSelf})";
                    }

                    Plugin.Log.LogWarning(
                        $"[FigureChest] #{i}: scene={scene} uid={uid} " +
                        $"dropListTID={tid} " +
                        $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                        $"activeSelf={active} activeInHierarchy={activeH} " +
                        $"name={go.name} {parentInfo}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[FigureChest] Scan error for #{i}: {ex.Message}");
                }
            }

            // Also log Looting data for figurine IDs
            try
            {
                var save = DR.Save.SaveSystem.GetGameSave();
                if (save != null)
                {
                    Plugin.Log.LogWarning("[FigureChest] === Looting status for figurine items ===");
                    for (int id = 1010301; id <= 1010320; id++)
                    {
                        var looted = save.HaveBeenLooted(id);
                        if (looted)
                            Plugin.Log.LogWarning($"[FigureChest] Item {id}: LOOTED");
                    }
                    Plugin.Log.LogInfo("[FigureChest] (Items not listed above are NOT looted)");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[FigureChest] Error checking looting data: {ex}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[FigureChest] ScanAllFigureChests error: {ex}");
        }
    }
}
