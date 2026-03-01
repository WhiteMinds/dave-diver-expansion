using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MiniGame;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Expands casino mini-game betting options from [10, 50, 100] to
/// [10, 50, 100, 500, 1000, 5000].
/// Patches BettingUI.RefreshCost — the single convergence point called by all
/// betting code paths (direct field write for MatchGame/HermitCrab, and
/// SetBettingCosts() for SharkGame/Balatro).
/// Only replaces arrays matching the known default [10, 50, 100] to avoid
/// breaking games with different betting structures (e.g. Balatro).
/// </summary>
public static class BettingExpansion
{
    private static readonly int[] ExpandedAmounts = { 10, 50, 100, 500, 1000, 5000 };

    private static ConfigEntry<bool> _enabled;
    private static bool _loggedOnce;

    public static void Init(ConfigFile config)
    {
        _enabled = config.Bind(
            "BettingExpansion", "Enabled", true,
            "Expand casino mini-game betting from 10/50/100 to 10/50/100/500/1000/5000");
    }

    private static bool IsDefaultCosts(Il2CppStructArray<int> costs)
    {
        return costs.Length == 3
            && costs[0] == 10
            && costs[1] == 50
            && costs[2] == 100;
    }

    private static bool IsExpandedCosts(Il2CppStructArray<int> costs)
    {
        if (costs.Length != ExpandedAmounts.Length) return false;
        for (int i = 0; i < ExpandedAmounts.Length; i++)
            if (costs[i] != ExpandedAmounts[i]) return false;
        return true;
    }

    [HarmonyPatch(typeof(BettingUI), nameof(BettingUI.RefreshCost))]
    static class RefreshCost_Patch
    {
        static void Prefix(BettingUI __instance)
        {
            if (!_enabled.Value) return;

            var costs = __instance.bettingCosts;
            if (costs == null) return;

            if (IsExpandedCosts(costs)) return;
            if (!IsDefaultCosts(costs)) return;

            var newCosts = new Il2CppStructArray<int>(ExpandedAmounts.Length);
            for (int i = 0; i < ExpandedAmounts.Length; i++)
                newCosts[i] = ExpandedAmounts[i];

            int idx = __instance._bettingIndex;
            if (idx >= ExpandedAmounts.Length) idx = 0;

            __instance.SetBettingCosts(newCosts, idx, false);

            if (!_loggedOnce)
            {
                Plugin.Log.LogInfo("[BettingExpansion] Expanded betting to [10, 50, 100, 500, 1000, 5000]");
                _loggedOnce = true;
            }
        }
    }
}
