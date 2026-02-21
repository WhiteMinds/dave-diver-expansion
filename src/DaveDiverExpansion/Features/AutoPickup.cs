using System.Collections.Generic;
using BepInEx.Configuration;
using DaveDiverExpansion.Helpers;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Automatically picks up nearby items during diving.
/// Reads from EntityRegistry (shared Harmony-patched instance registries).
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

    // Suppress pickup when player is locked (cutscene/dialogue), with cooldown after unlock
    private static bool _wasLocked;
    private static float _unlockTime;
    private const float UnlockCooldown = 1f;

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
            "AutoPickup", "PickupRadius", 1f,
            "Radius around the player to auto-pick items (in game units)");

        Plugin.Log.LogInfo($"AutoPickup initialized (enabled={Enabled.Value}, radius={PickupRadius.Value})");
    }

    /// <summary>
    /// Core auto-pickup logic. Called from Harmony postfix on PlayerCharacter.Update.
    /// </summary>
    internal static void TryPickupNearby(PlayerCharacter player)
    {
        if (!Enabled.Value) return;

        // Skip pickup when player is in cutscene/dialogue/locked state
        bool isLocked = player.IsActionLock || player.IsScenarioPlaying;
        if (isLocked)
        {
            if (!_wasLocked)
            {
                Plugin.Log.LogInfo($"AutoPickup: locked (ActionLock={player.IsActionLock}, Scenario={player.IsScenarioPlaying}) — pausing");
                _wasLocked = true;
            }
            return;
        }
        if (_wasLocked)
        {
            _unlockTime = Time.time;
            _wasLocked = false;
            Plugin.Log.LogInfo("AutoPickup: unlocked — cooldown 1s");
        }
        if (Time.time - _unlockTime < UnlockCooldown)
            return;

        var playerPos = player.transform.position;
        var radius = PickupRadius.Value;

        // Clean up pending destroy set each frame
        _pendingDestroy.RemoveWhere(go => go == null);

        // Fish
        if (AutoPickupFish.Value)
        {
            foreach (var fish in EntityRegistry.AllFish)
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
            foreach (var item in EntityRegistry.AllItems)
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
            foreach (var chest in EntityRegistry.AllChests)
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

// --- Auto-pickup trigger patch ---

[HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
public static class AutoPickupPatch
{
    private static void Postfix(PlayerCharacter __instance)
    {
        AutoPickup.TryPickupNearby(__instance);
    }
}
