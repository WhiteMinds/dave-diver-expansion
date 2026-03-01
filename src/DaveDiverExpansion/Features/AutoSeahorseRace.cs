using BepInEx.Configuration;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using MiniGame.SeahorseRace;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Auto-controls the player's seahorse during seahorse races.
/// When enabled, automatically accelerates, dodges obstacles, and uses tags.
/// F5 hotkey: quick-jump to the entertainment scene (where seahorse race NPC is).
/// </summary>
public static class AutoSeahorseRace
{
    private static ConfigEntry<bool> _enabled;

    // Gauge thresholds: 0-50=Run, 50-75=MaxRun(fastest), 75-100=OverRun(decel)
    private const float GaugeAccelTarget = 76f;

    // Extra buffer beyond the computed trigger point to start dodging early.
    // Physics (OnTriggerEnter) runs BEFORE Update in Unity's frame order,
    // so we must have set move.y in a PREVIOUS frame's Update for the state
    // transition to take effect before the next physics step.
    // At 60fps max-speed the racer moves ~0.02 units/frame, so 0.1 ≈ 5 frames buffer.
    private const float DodgeBuffer = 0.1f;

    // Cached obstacle data, refreshed once per race
    private static ObstacleInfo[] _cachedObstacles;
    private static bool _obstaclesCached;

    // Racer collider half-width along x-axis (cached once per race)
    private static float _racerHalfX;
    private static bool _racerHalfCached;


    /// <summary>
    /// Pre-computed obstacle data: position + trigger collider leading edge.
    /// </summary>
    private struct ObstacleInfo
    {
        public SeahorseRaceTrackObstacle obstacle;
        public float triggerLeadingEdgeX; // bounds.min.x of the trigger collider
    }

    public static void Init(ConfigFile config)
    {
        _enabled = config.Bind(
            "AutoSeahorseRace", "Enabled", false,
            "Automatically control seahorse during racing");

        Plugin.Log.LogInfo("AutoSeahorseRace initialized");
    }

    /// <summary>
    /// Called every frame from ConfigUIBehaviour.Update.
    /// F5 quick-jumps to the entertainment scene (MV_Casino_Inside) for testing seahorse races.
    /// </summary>
    internal static void CheckHotkey()
    {
        // F5 debug hotkey disabled for release. Uncomment to re-enable.
        return;
#pragma warning disable CS0162
        try
        {
            if (!Input.GetKeyDown(KeyCode.F5)) return;
        }
        catch { return; }
#pragma warning restore CS0162

        try
        {
            var sceneLoader = Object.FindObjectOfType<SceneLoader>();
            if (sceneLoader == null)
            {
                Plugin.Log.LogWarning("AutoSeahorseRace: SceneLoader not found");
                return;
            }
            var sceneName = SceneLoader.k_SceneName_MV_Casino_Inside;
            Plugin.Log.LogInfo($"AutoSeahorseRace: Jumping to scene '{sceneName}'");
            sceneLoader.ChangeSceneAsync(sceneName, SceneTransitionType.FadeOutIn);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"AutoSeahorseRace: Scene switch failed: {e.Message}");
        }
    }

    /// <summary>
    /// Cache racer collider half-width on first call per race.
    /// </summary>
    private static float GetRacerHalfX(SeahorseRacer racer)
    {
        if (!_racerHalfCached)
        {
            var col = racer.GetComponent<Collider>();
            _racerHalfX = col != null ? col.bounds.extents.x : 1.0f;
            _racerHalfCached = true;
            Plugin.Log.LogInfo($"AutoSeahorseRace: Racer collider halfX={_racerHalfX:F2}");
        }
        return _racerHalfX;
    }

    /// <summary>
    /// Caches all obstacles with their trigger collider leading edge (bounds.min.x).
    /// This tells us the exact x-position where the racer's collider front will first overlap.
    /// Dodge point = triggerLeadingEdgeX - racerHalfX - DodgeBuffer
    /// </summary>
    private static void CacheObstacles()
    {
        var raw = Object.FindObjectsOfType<SeahorseRaceTrackObstacle>();
        if (raw == null || raw.Length == 0)
        {
            _cachedObstacles = new ObstacleInfo[0];
            _obstaclesCached = true;
            return;
        }

        _cachedObstacles = new ObstacleInfo[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            var obs = raw[i];
            var col = obs.GetComponent<Collider>();
            // Use collider bounds.min.x as the leading edge facing the racer.
            // If no collider found, fall back to transform.position.x
            float leadingX = col != null ? col.bounds.min.x : obs.transform.position.x;
            _cachedObstacles[i] = new ObstacleInfo
            {
                obstacle = obs,
                triggerLeadingEdgeX = leadingX,
            };
        }
        _obstaclesCached = true;
        Plugin.Log.LogInfo($"AutoSeahorseRace: Cached {raw.Length} obstacles with collider bounds");
    }

    /// <summary>
    /// Finds the nearest obstacle ahead using collider-based trigger distance.
    /// The racer's collider front = racerPos.x + racerHalfX.
    /// Trigger fires when racerFront >= obstacle.bounds.min.x.
    /// We dodge when racerFront >= obstacle.bounds.min.x - DodgeBuffer.
    /// </summary>
    private static SeahorseRaceTrackObstacle FindObstacleAhead(SeahorseRacer racer)
    {
        if (!_obstaclesCached) CacheObstacles();
        if (_cachedObstacles == null || _cachedObstacles.Length == 0) return null;

        float racerHalfX = GetRacerHalfX(racer);
        var racerPos = racer.transform.position;
        float racerFrontX = racerPos.x + racerHalfX;
        float racerZ = racerPos.z;

        SeahorseRaceTrackObstacle nearest = null;
        float nearestGap = float.MaxValue;

        for (int i = 0; i < _cachedObstacles.Length; i++)
        {
            ref var info = ref _cachedObstacles[i];
            if (info.obstacle == null || !info.obstacle.gameObject.activeInHierarchy) continue;

            // Only consider obstacles in the same lane (z within ±2 units)
            float obsZ = info.obstacle.transform.position.z;
            if (Mathf.Abs(obsZ - racerZ) > 2f) continue;

            // gap = how far the racer front is from the trigger leading edge
            // gap > 0 means racer hasn't reached the trigger zone yet
            // gap <= 0 means racer is inside or past the trigger zone
            float gap = info.triggerLeadingEdgeX - racerFrontX;

            // Dodge when the gap is within DodgeBuffer (slightly before trigger fires)
            if (gap > -0.5f && gap < DodgeBuffer && gap < nearestGap)
            {
                nearestGap = gap;
                nearest = info.obstacle;
            }
        }

        return nearest;
    }

    [HarmonyPatch(typeof(SeahorseRacer), nameof(SeahorseRacer.Update))]
    static class SeahorseRacerUpdate_Patch
    {
        static void Prefix(SeahorseRacer __instance)
        {
            if (!_enabled.Value) return;

            try
            {
                var racerValue = __instance.racerValue;
                if (racerValue == null || !racerValue.isPlayer) return;

                var inputValue = __instance.inputValue;
                if (inputValue == null) return;

                // Only act during active racing states
                var stateMachine = __instance.stateMachine;
                if (stateMachine == null) return;
                var currentState = stateMachine.currentState;
                if (currentState == null) return;
                var state = currentState.stateName;

                // StateName: 0=Wait, 1=Ready, 2=Run, 3=MaxRun, 4=OverRun, 5=Jump, 6=Crawl, 7=Fail, 8=Fall, 9=Finish
                int stateInt = (int)state;
                if (stateInt < 2 || stateInt > 6) return;

                // Detect obstacle: first check trigger-based, then lookahead
                var obstacle = __instance.obstacle;
                if (obstacle == null)
                    obstacle = FindObstacleAhead(__instance);

                if (obstacle != null)
                {
                    // type: hurdle(0) → move up (Jump), crawl(1) → move down (Crawl)
                    float moveY = (int)obstacle.type == 0 ? 1f : -1f;
                    inputValue.move = new Vector2(0f, moveY);
                }
                else
                {
                    inputValue.move = Vector2.zero;
                }

                // Always maintain acceleration gauge in MaxRun zone (50-75)
                var gauge = __instance.gauge;
                if (gauge != null)
                {
                    if (gauge.gauge < GaugeAccelTarget)
                        inputValue.OnAccel();
                }
                else
                {
                    inputValue.OnAccel();
                }

                // Tag when in a tag zone.
                // CalcGaugeTransRatio() increases linearly as racer moves through the zone.
                // The ratio is multiplied directly into the inherited gauge speed on relay:
                //   newGauge = oldGauge / oldMax * ratio * newMax
                // So higher ratio = more speed preserved. Wait for ratio >= 0.9 to get Perfect.
                var trackTag = __instance.trackTag;
                if (trackTag != null)
                {
                    float ratio = trackTag.CalcGaugeTransRatio();
                    inputValue.IsTag = ratio >= 0.9f;
                }
                else
                {
                    inputValue.IsTag = false;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"AutoSeahorseRace error: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Reset obstacle cache when a new race starts.
    /// </summary>
    [HarmonyPatch(typeof(SeahorseRaceSessionPlay), nameof(SeahorseRaceSessionPlay.Start_Impl))]
    static class SessionStart_Patch
    {
        static void Prefix()
        {
            _obstaclesCached = false;
            _cachedObstacles = null;
            _racerHalfCached = false;
        }
    }

}
