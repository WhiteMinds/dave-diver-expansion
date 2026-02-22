using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Helpers;

/// <summary>
/// Shared registry of in-scene entities populated by Harmony lifecycle patches.
/// Both AutoPickup and DiveMap read from these sets — no FindObjectsOfType needed.
/// </summary>
public static class EntityRegistry
{
    public static readonly HashSet<FishInteractionBody> AllFish = new();
    public static readonly HashSet<PickupInstanceItem> AllItems = new();
    public static readonly HashSet<InstanceItemChest> AllChests = new();
    public static readonly HashSet<BreakableLootObject> AllBreakableOres = new();
    public static readonly HashSet<InteractionGimmick_Mining> AllMiningNodes = new();

    // Periodic null purge for sets without OnDisable cleanup
    private const float PurgeInterval = 2f;
    private static float _purgeTimer;

    private static bool IsDebug => Features.DiveMap.DebugLog?.Value == true;

    /// <summary>
    /// Remove destroyed objects from registries that lack OnDisable patches.
    /// Call periodically from a per-frame hook (e.g. PlayerCharacter.Update postfix).
    /// </summary>
    internal static void Purge()
    {
        _purgeTimer += Time.deltaTime;
        if (_purgeTimer < PurgeInterval) return;
        _purgeTimer = 0f;

        int fishBefore = AllFish.Count;
        int chestBefore = AllChests.Count;
        AllFish.RemoveWhere(f => f == null);
        AllChests.RemoveWhere(c => c == null);
        AllBreakableOres.RemoveWhere(o => o == null);
        AllMiningNodes.RemoveWhere(m => m == null);

        if (IsDebug)
        {
            int fishRemoved = fishBefore - AllFish.Count;
            int chestRemoved = chestBefore - AllChests.Count;
            if (fishRemoved > 0 || chestRemoved > 0)
                Plugin.Log.LogInfo($"[EntityRegistry] Purge: fish={fishRemoved} chest={chestRemoved} removed (remaining: fish={AllFish.Count} item={AllItems.Count} chest={AllChests.Count} ores={AllBreakableOres.Count} mining={AllMiningNodes.Count})");
        }
    }
}

// --- Instance registry patches ---

[HarmonyPatch(typeof(PickupInstanceItem), nameof(PickupInstanceItem.OnEnable))]
static class PickupItemOnEnablePatch
{
    static void Postfix(PickupInstanceItem __instance)
    {
        EntityRegistry.AllItems.Add(__instance);
        if (Features.DiveMap.DebugLog?.Value == true)
            Plugin.Log.LogInfo($"[EntityRegistry] Item+ {__instance.gameObject.name} pos={__instance.transform.position} (total={EntityRegistry.AllItems.Count})");
    }
}

[HarmonyPatch(typeof(PickupInstanceItem), nameof(PickupInstanceItem.OnDisable))]
static class PickupItemOnDisablePatch
{
    static void Postfix(PickupInstanceItem __instance)
    {
        EntityRegistry.AllItems.Remove(__instance);
        if (Features.DiveMap.DebugLog?.Value == true)
            Plugin.Log.LogInfo($"[EntityRegistry] Item- {__instance.gameObject.name} (total={EntityRegistry.AllItems.Count})");
    }
}

[HarmonyPatch(typeof(InstanceItemChest), nameof(InstanceItemChest.OnEnable))]
static class ChestOnEnablePatch
{
    static void Postfix(InstanceItemChest __instance)
    {
        EntityRegistry.AllChests.Add(__instance);
        if (Features.DiveMap.DebugLog?.Value == true)
            Plugin.Log.LogInfo($"[EntityRegistry] Chest+ {__instance.gameObject.name} pos={__instance.transform.position} (total={EntityRegistry.AllChests.Count})");
    }
}

[HarmonyPatch(typeof(InstanceItemChest), nameof(InstanceItemChest.SuccessInteract))]
static class ChestSuccessInteractPatch
{
    static void Postfix(InstanceItemChest __instance)
    {
        EntityRegistry.AllChests.Remove(__instance);
        if (Features.DiveMap.DebugLog?.Value == true)
            Plugin.Log.LogInfo($"[EntityRegistry] Chest- (interact) {__instance.gameObject.name} (total={EntityRegistry.AllChests.Count})");
    }
}

[HarmonyPatch(typeof(FishInteractionBody), nameof(FishInteractionBody.Awake))]
static class FishAwakePatch
{
    static void Postfix(FishInteractionBody __instance)
    {
        EntityRegistry.AllFish.Add(__instance);
        if (Features.DiveMap.DebugLog?.Value == true)
            Plugin.Log.LogInfo($"[EntityRegistry] Fish+ {__instance.gameObject.name} pos={__instance.transform.position} (total={EntityRegistry.AllFish.Count})");
    }
}

[HarmonyPatch(typeof(BreakableLootObject), nameof(BreakableLootObject.OnEnable))]
static class BreakableLootOnEnablePatch
{
    static void Postfix(BreakableLootObject __instance)
    {
        if (__instance == null) return;
        // Only register ore types (Pile=6, SeaWeed=7 are not ores)
        try
        {
            var t = __instance.lootObjectType;
            if (t == BreakableLootObject.BreakableLootObjectType.Pile
                || t == BreakableLootObject.BreakableLootObjectType.SeaWeed)
                return;
        }
        catch { return; }
        EntityRegistry.AllBreakableOres.Add(__instance);
    }
}

[HarmonyPatch(typeof(BreakableLootObject), nameof(BreakableLootObject.OnDie))]
static class BreakableLootOnDiePatch
{
    static void Postfix(BreakableLootObject __instance)
    {
        if (__instance == null) return;
        EntityRegistry.AllBreakableOres.Remove(__instance);
    }
}

[HarmonyPatch(typeof(InteractionGimmick_Mining), nameof(InteractionGimmick_Mining.Awake))]
static class MiningNodeAwakePatch
{
    static void Postfix(InteractionGimmick_Mining __instance)
    {
        if (__instance == null) return;
        EntityRegistry.AllMiningNodes.Add(__instance);
    }
}

// No OnDie patch — it's virtual and the IL2CPP trampoline crashes when called
// from base/sibling classes. Purge() handles cleanup via null/isClear checks.

[HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
static class EntityRegistryPurgePatch
{
    static void Postfix() => EntityRegistry.Purge();
}
