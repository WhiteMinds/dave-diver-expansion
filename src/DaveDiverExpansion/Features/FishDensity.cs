using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Increases fish population by calling DoInstanceFishOrGroup with the original prefab
/// on FishAllocators that have already spawned. This creates additional BoidGroups
/// through the game's own instantiation path, ensuring fish are fully functional.
///
/// Multiplier is driven by iDiverExtension's "Ecology Protection" upgrade (TypeId=107).
///
/// Scans InGameManager.FishAllocators every frame (cheap list iteration, no
/// FindObjectsOfType) to catch both boid-based and single-fish allocators.
/// See docs/fish-density-system.md for design rationale and alternatives.
/// </summary>
public static class FishDensity
{
    // Track which allocators we've already processed (by instance ID)
    private static readonly HashSet<int> _processedAllocators = new();

    // Reentrant guard: DoInstanceFishOrGroup may create BoidGroups whose Start
    // could trigger other code paths — this flag prevents recursive processing.
    private static bool _isSpawning;

    // Scene change hook
    private static bool _sceneChangeHooked;

    /// <summary>
    /// Hook into SceneManager.sceneLoaded to clear state on scene change.
    /// </summary>
    internal static void EnsureSceneChangeHook()
    {
        if (_sceneChangeHooked) return;
        _sceneChangeHooked = true;
        try
        {
            SceneManager.add_sceneLoaded(
                (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)OnSceneLoaded);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[FishDensity] Failed to hook sceneLoaded: {ex.Message}");
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            int prevCount = _processedAllocators.Count;
            _processedAllocators.Clear();
            if (prevCount > 0)
                Plugin.Log.LogInfo($"[FishDensity] Scene '{scene.name}' loaded, cleared {prevCount} processed allocators");
        }
    }

    /// <summary>
    /// Per-frame scan of InGameManager.FishAllocators. Processes any allocator
    /// that has become IsInstanced since the last scan.
    /// Cost: ~300 bool reads + HashSet lookups per frame ≈ negligible.
    /// </summary>
    internal static void ScanAllocators()
    {
        EnsureSceneChangeHook();

        if (_isSpawning) return;

        int multiplier = iDiverExtension.GetFishDensityMultiplier();
        if (multiplier <= 1) return;

        var mgr = Singleton<InGameManager>._instance;
        if (mgr == null) return;

        var allocators = mgr.FishAllocators;
        if (allocators == null) return;

        // Quick pass: check if any new instanced allocator exists
        bool hasNew = false;
        for (int i = 0; i < allocators.Count; i++)
        {
            var alloc = allocators[i];
            if (alloc != null && alloc.IsInstanced && !_processedAllocators.Contains(alloc.GetInstanceID()))
            {
                hasNew = true;
                break;
            }
        }

        if (!hasNew) return;

        // Full processing pass
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
                if (!_processedAllocators.Add(id)) continue;

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
                Plugin.Log.LogInfo($"[FishDensity] Spawned {totalSpawned} extra groups (processedSet={_processedAllocators.Count})");
        }
        finally
        {
            _isSpawning = false;
        }
    }

    /// <summary>
    /// Clear tracking state on scene change (legacy entry point).
    /// </summary>
    internal static void OnSceneChange()
    {
        _processedAllocators.Clear();
    }
}

/// <summary>
/// Per-frame scan on PlayerCharacter.Update to process newly-instanced allocators.
/// Uses InGameManager.FishAllocators (maintained list) — no FindObjectsOfType.
/// </summary>
[HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.Update))]
static class FishDensityScanPatch
{
    static void Postfix()
    {
        FishDensity.ScanAllocators();
    }
}
