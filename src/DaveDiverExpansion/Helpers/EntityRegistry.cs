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

    // Periodic null purge for sets without OnDisable cleanup
    private const float PurgeInterval = 2f;
    private static float _purgeTimer;

    /// <summary>
    /// Remove destroyed objects from registries that lack OnDisable patches.
    /// Call periodically from a per-frame hook (e.g. PlayerCharacter.Update postfix).
    /// </summary>
    internal static void Purge()
    {
        _purgeTimer += Time.deltaTime;
        if (_purgeTimer < PurgeInterval) return;
        _purgeTimer = 0f;
        AllFish.RemoveWhere(f => f == null);
        AllChests.RemoveWhere(c => c == null);
    }
}

// --- Instance registry patches ---

[HarmonyPatch(typeof(PickupInstanceItem), nameof(PickupInstanceItem.OnEnable))]
static class PickupItemOnEnablePatch
{
    static void Postfix(PickupInstanceItem __instance) => EntityRegistry.AllItems.Add(__instance);
}

[HarmonyPatch(typeof(PickupInstanceItem), nameof(PickupInstanceItem.OnDisable))]
static class PickupItemOnDisablePatch
{
    static void Postfix(PickupInstanceItem __instance) => EntityRegistry.AllItems.Remove(__instance);
}

[HarmonyPatch(typeof(InstanceItemChest), nameof(InstanceItemChest.OnEnable))]
static class ChestOnEnablePatch
{
    static void Postfix(InstanceItemChest __instance) => EntityRegistry.AllChests.Add(__instance);
}

[HarmonyPatch(typeof(FishInteractionBody), nameof(FishInteractionBody.Awake))]
static class FishAwakePatch
{
    static void Postfix(FishInteractionBody __instance) => EntityRegistry.AllFish.Add(__instance);
}

[HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
static class EntityRegistryPurgePatch
{
    static void Postfix() => EntityRegistry.Purge();
}
