using BepInEx.Configuration;
using Common.Contents;
using UnityEngine;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Press F2 (configurable) to open the scene-switch menu (MoveScenePanel)
/// from anywhere in the current scene, without walking to the trigger zone.
/// </summary>
public static class QuickSceneSwitch
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<KeyCode> ToggleKey;

    // Track whether we opened the panel so we can clean up OnPlayerEnter state
    private static bool _openedByUs;
    private static MoveScenePanel _cachedPanel;

    public static void Init(ConfigFile config)
    {
        Enabled = config.Bind(
            "QuickSceneSwitch", "Enabled", true,
            "Open the scene-switch menu with a hotkey (no need to walk to the exit). " +
            "WARNING: Using during cutscenes/story events may cause missions to be skipped or unexpected behavior.");
        ToggleKey = config.Bind(
            "QuickSceneSwitch", "ToggleKey", KeyCode.F2,
            "Key to open/close the scene-switch menu");

        Plugin.Log.LogInfo($"QuickSceneSwitch initialized (key={ToggleKey.Value})");
    }

    /// <summary>
    /// Called every frame from ConfigUIBehaviour.Update.
    /// </summary>
    internal static void CheckToggle()
    {
        if (!Enabled.Value) return;

        // Clean up after native ESC close: if we opened it and it's now closed,
        // simulate player leaving the trigger zone to remove the lingering button
        if (_openedByUs && _cachedPanel != null && !_cachedPanel.IsOpened)
        {
            _cachedPanel.OnPlayerEnter(false);
            _openedByUs = false;
            _cachedPanel = null;
        }

        try
        {
            if (!Input.GetKeyDown(ToggleKey.Value)) return;
        }
        catch
        {
            return;
        }

        var panel = _cachedPanel != null ? _cachedPanel : Object.FindObjectOfType<MoveScenePanel>();
        if (panel == null) return;

        if (panel.IsOpened)
        {
            panel.OnCancel();
            panel.OnPlayerEnter(false);
            _openedByUs = false;
            _cachedPanel = null;
        }
        else
        {
            panel.OnPlayerEnter(true);
            panel.ShowList(true);
            _openedByUs = true;
            _cachedPanel = panel;
        }
    }
}
