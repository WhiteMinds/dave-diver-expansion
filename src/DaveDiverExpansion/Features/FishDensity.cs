using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Increases fish population by calling DoInstanceFishOrGroup with the original prefab
/// on FishAllocators that have already spawned. This creates additional BoidGroups
/// through the game's own instantiation path, ensuring fish are fully functional.
///
/// Multiplier is driven by iDiverExtension's "Ecology Protection" upgrade (TypeId=107).
/// Triggered by BoidGroup.Start (fires when a fish group finishes spawning),
/// so extra groups appear in the same frame — no polling delay.
/// </summary>
public static class FishDensity
{
    // Track which allocators we've already processed (by instance ID)
    private static readonly HashSet<int> _processedAllocators = new();

    // Reentrant guard: DoInstanceFishOrGroup creates new BoidGroups whose Start
    // triggers our Postfix again — this flag prevents recursive processing.
    private static bool _isSpawning;

    /// <summary>
    /// Called from BoidGroup.Start Postfix. Scans FishAllocators for newly-spawned
    /// ones and re-instantiates from their original prefab via DoInstanceFishOrGroup.
    /// </summary>
    internal static void OnBoidGroupStarted()
    {
        if (_isSpawning) return;
        int multiplier = iDiverExtension.GetFishDensityMultiplier();
        if (multiplier <= 1) return;

        var allocators = Object.FindObjectsOfType<FishAllocator>();
        if (allocators == null) return;

        _isSpawning = true;
        try
        {
            int totalSpawned = 0;
            for (int i = 0; i < allocators.Count; i++)
            {
                var alloc = allocators[i];
                if (alloc == null) continue;
                if (!alloc.IsInstanced) continue;

                int id = alloc.GetInstanceID();
                if (_processedAllocators.Contains(id)) continue;
                _processedAllocators.Add(id);

                // Get the prefab: Default type uses FishPrefabOrGroup directly,
                // RandomSelect type picks from a weighted list via GetRandomFishGroup().
                GameObject prefab = null;
                if (alloc.instanceType == FishAllocator.InstanceType.Default)
                {
                    prefab = alloc.FishPrefabOrGroup;
                }
                else
                {
                    var selector = alloc.GetRandomFishGroup();
                    if (selector != null)
                        prefab = selector.data;
                }

                if (prefab == null) continue;

                for (int j = 1; j < multiplier; j++)
                {
                    alloc.DoInstanceFishOrGroup(prefab, null, false);
                    totalSpawned++;
                }
            }

            if (totalSpawned > 0)
                Plugin.Log.LogInfo($"[FishDensity] Spawned {totalSpawned} extra fish groups via DoInstanceFishOrGroup (processed: {_processedAllocators.Count} allocators)");
        }
        finally
        {
            _isSpawning = false;
        }
    }

    /// <summary>
    /// Clear tracking state on scene change.
    /// </summary>
    internal static void OnSceneChange()
    {
        _processedAllocators.Clear();
    }
}

/// <summary>
/// Postfix on BoidGroup.Start (CallerCount=0, Unity message — safe to patch).
/// Fires when a fish group is created, triggering extra spawns from allocator prefabs.
/// </summary>
[HarmonyPatch(typeof(BoidGroup), nameof(BoidGroup.Start))]
static class FishDensityPatch
{
    static void Postfix()
    {
        FishDensity.OnBoidGroupStarted();
    }
}
