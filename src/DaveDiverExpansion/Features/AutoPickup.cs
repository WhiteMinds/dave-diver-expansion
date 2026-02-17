using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Automatically picks up nearby items during diving.
/// Scans for collectible objects within a configurable radius and triggers their pickup.
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

    public static void Init(BepInEx.Configuration.ConfigFile config)
    {
        Enabled = config.Bind(
            "AutoPickup", "Enabled", true,
            "Enable automatic item pickup while diving");
        AutoPickupFish = config.Bind(
            "AutoPickup", "AutoPickupFish", true,
            "Auto-pickup dead fish");
        AutoPickupItems = config.Bind(
            "AutoPickup", "AutoPickupItems", true,
            "Auto-pickup dropped items");
        AutoOpenChests = config.Bind(
            "AutoPickup", "AutoOpenChests", true,
            "Auto-open treasure chests");
        PickupRadius = config.Bind(
            "AutoPickup", "PickupRadius", 5f,
            "Radius around the player to auto-pick items (in game units)");

        Plugin.Log.LogInfo($"AutoPickup initialized (enabled={Enabled.Value}, radius={PickupRadius.Value})");
    }

    /// <summary>
    /// Core auto-pickup logic. Called from Harmony postfix on PlayerCharacter.Update.
    /// </summary>
    internal static void TryPickupNearby(PlayerCharacter player)
    {
        if (!Enabled.Value) return;

        var playerPos = player.transform.position;
        var radius = PickupRadius.Value;

        // Clean up pending destroy set each frame
        _pendingDestroy.RemoveWhere(go => go == null);

        // Fish
        if (AutoPickupFish.Value)
        {
            foreach (var fish in Object.FindObjectsOfType<FishInteractionBody>())
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
            foreach (var item in Object.FindObjectsOfType<PickupInstanceItem>())
            {
                if (item == null || item.gameObject == null) continue;
                if (_pendingDestroy.Contains(item.gameObject)) continue;
                if (item.transform.position == Vector3.zero) continue;
                if (Vector3.Distance(playerPos, item.transform.position) > radius) continue;

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
            foreach (var chest in Object.FindObjectsOfType<InstanceItemChest>())
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

[HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
public static class AutoPickupPatch
{
    private static void Postfix(PlayerCharacter __instance)
    {
        AutoPickup.TryPickupNearby(__instance);
    }
}
