using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Automatically picks up nearby items during diving.
/// Uses Harmony-patched instance registries instead of FindObjectsOfType for performance.
/// </summary>
public static class AutoPickup
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> AutoPickupFish;
    public static ConfigEntry<bool> AutoPickupItems;
    public static ConfigEntry<bool> AutoOpenChests;
    public static ConfigEntry<float> PickupRadius;

    // Track objects being destroyed this frame to avoid double-pickup
    private static readonly HashSet<GameObject> _pendingDestroy = new();

    // Instance registries populated by Harmony patches on lifecycle methods
    internal static readonly HashSet<FishInteractionBody> AllFish = new();
    internal static readonly HashSet<PickupInstanceItem> AllItems = new();
    internal static readonly HashSet<InstanceItemChest> AllChests = new();

    // Periodic null purge for sets without OnDisable cleanup
    private const float PurgeInterval = 2f;
    private static float _purgeTimer;

    public static void Init(BepInEx.Configuration.ConfigFile config)
    {
        Enabled = config.Bind(
            "AutoPickup", "Enabled", true,
            "Enable automatic item pickup while diving");
        AutoPickupFish = config.Bind(
            "AutoPickup", "AutoPickupFish", false,
            "Auto-pickup dead fish");
        AutoPickupItems = config.Bind(
            "AutoPickup", "AutoPickupItems", true,
            "Auto-pickup dropped items");
        AutoOpenChests = config.Bind(
            "AutoPickup", "AutoOpenChests", false,
            "Auto-open treasure chests");
        PickupRadius = config.Bind(
            "AutoPickup", "PickupRadius", 2f,
            "Radius around the player to auto-pick items (in game units)");

        Plugin.Log.LogInfo($"AutoPickup initialized (enabled={Enabled.Value}, radius={PickupRadius.Value})");
    }

    /// <summary>
    /// Core auto-pickup logic. Called from Harmony postfix on PlayerCharacter.Update.
    /// </summary>
    internal static void TryPickupNearby(PlayerCharacter player)
    {
        if (!Enabled.Value) return;

        // Periodically purge destroyed objects from registries without OnDisable
        _purgeTimer += Time.deltaTime;
        if (_purgeTimer >= PurgeInterval)
        {
            _purgeTimer = 0f;
            AllFish.RemoveWhere(f => f == null);
            AllChests.RemoveWhere(c => c == null);
        }

        var playerPos = player.transform.position;
        var radius = PickupRadius.Value;

        // Clean up pending destroy set each frame
        _pendingDestroy.RemoveWhere(go => go == null);

        // Fish
        if (AutoPickupFish.Value)
        {
            foreach (var fish in AllFish)
            {
                if (fish == null || fish.gameObject == null) continue;
                if (_pendingDestroy.Contains(fish.gameObject)) continue;
                if (fish.transform.position == Vector3.zero) continue;
                if (Vector3.Distance(playerPos, fish.transform.position) > radius) continue;
                if (fish.InteractionType != FishInteractionBody.FishInteractionType.Pickup) continue;

                if (fish.CheckAvailableInteraction(player))
                {
                    fish.SuccessInteract(player);
                    _pendingDestroy.Add(fish.gameObject);
                }
            }
        }

        // Items
        if (AutoPickupItems.Value)
        {
            foreach (var item in AllItems)
            {
                if (item == null || item.gameObject == null) continue;
                if (_pendingDestroy.Contains(item.gameObject)) continue;
                if (item.isNeedSwapSetID != 0) continue; // swap-indicator ghost copy
                if (item.transform.position == Vector3.zero) continue;
                if (Vector3.Distance(playerPos, item.transform.position) > radius) continue;

                // Skip weapons and equipment that trigger swap loops:
                //   PickupInstanceMelee(Clone) — melee weapons
                //   PickupInstanceWeapon(Clone) — ranged weapons
                //   *HarpoonHead* — harpoon head upgrades
                var goName = item.gameObject.name;
                if (goName.StartsWith("PickupInstance") || goName.Contains("HarpoonHead"))
                    continue;

                if (item.CheckAvailableInteraction(player))
                {
                    item.SuccessInteract(player);
                    _pendingDestroy.Add(item.gameObject);
                }
            }
        }

        // Chests
        if (AutoOpenChests.Value)
        {
            foreach (var chest in AllChests)
            {
                if (chest == null || chest.gameObject == null) continue;
                if (_pendingDestroy.Contains(chest.gameObject)) continue;
                if (chest.transform.position == Vector3.zero) continue;
                if (Vector3.Distance(playerPos, chest.transform.position) > radius) continue;
                if (chest.IsOpen) continue;

                chest.SuccessInteract(player);
                _pendingDestroy.Add(chest.gameObject);
            }
        }
    }
}

// --- Instance registry patches ---

[HarmonyPatch(typeof(PickupInstanceItem), nameof(PickupInstanceItem.OnEnable))]
static class PickupItemOnEnablePatch
{
    static void Postfix(PickupInstanceItem __instance) => AutoPickup.AllItems.Add(__instance);
}

[HarmonyPatch(typeof(PickupInstanceItem), nameof(PickupInstanceItem.OnDisable))]
static class PickupItemOnDisablePatch
{
    static void Postfix(PickupInstanceItem __instance) => AutoPickup.AllItems.Remove(__instance);
}

[HarmonyPatch(typeof(InstanceItemChest), nameof(InstanceItemChest.OnEnable))]
static class ChestOnEnablePatch
{
    static void Postfix(InstanceItemChest __instance) => AutoPickup.AllChests.Add(__instance);
}

[HarmonyPatch(typeof(FishInteractionBody), nameof(FishInteractionBody.Awake))]
static class FishAwakePatch
{
    static void Postfix(FishInteractionBody __instance) => AutoPickup.AllFish.Add(__instance);
}

// --- Auto-pickup trigger patch ---

[HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
public static class AutoPickupPatch
{
    private static void Postfix(PlayerCharacter __instance)
    {
        AutoPickup.TryPickupNearby(__instance);
    }
}
