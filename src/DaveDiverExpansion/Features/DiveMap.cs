using System.Collections.Generic;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UI;

namespace DaveDiverExpansion.Features;

/// <summary>
/// Dive map HUD that renders a top-down overview of the current level.
/// Minimap (top-right) follows the player with configurable zoom;
/// press M to toggle a full-level enlarged map in the center of the screen.
/// </summary>
public static class DiveMap
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<KeyCode> ToggleKey;
    public static ConfigEntry<bool> ShowEscapePods;
    public static ConfigEntry<bool> ShowFish;
    public static ConfigEntry<bool> ShowItems;
    public static ConfigEntry<bool> ShowChests;
    public static ConfigEntry<float> MapSize;
    public static ConfigEntry<float> MapOpacity;
    public static ConfigEntry<float> MiniMapZoom;

    public static void Init(ConfigFile config)
    {
        Enabled = config.Bind(
            "DiveMap", "Enabled", true,
            "Enable the dive map HUD");
        ToggleKey = config.Bind(
            "DiveMap", "ToggleKey", KeyCode.M,
            "Key to toggle the enlarged map view");
        ShowEscapePods = config.Bind(
            "DiveMap", "ShowEscapePods", true,
            "Show escape pod/mirror markers on the map");
        ShowFish = config.Bind(
            "DiveMap", "ShowFish", false,
            "Show fish markers on the map");
        ShowItems = config.Bind(
            "DiveMap", "ShowItems", false,
            "Show item markers on the map");
        ShowChests = config.Bind(
            "DiveMap", "ShowChests", false,
            "Show chest markers on the map");
        MapSize = config.Bind(
            "DiveMap", "MapSize", 0.3f,
            new ConfigDescription("Minimap size as fraction of screen height",
                new AcceptableValueRange<float>(0.15f, 0.5f)));
        MapOpacity = config.Bind(
            "DiveMap", "MapOpacity", 0.8f,
            new ConfigDescription("Map opacity",
                new AcceptableValueRange<float>(0.3f, 1.0f)));
        MiniMapZoom = config.Bind(
            "DiveMap", "MiniMapZoom", 3f,
            new ConfigDescription("Minimap zoom level (higher = more zoomed in)",
                new AcceptableValueRange<float>(1f, 10f)));
        ClassInjector.RegisterTypeInIl2Cpp<DiveMapBehaviour>();
        var go = new GameObject("DDE_DiveMapUpdater");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<DiveMapBehaviour>();

        Plugin.Log.LogInfo("DiveMap initialized (toggle big map: " + ToggleKey.Value + ")");
    }
}

/// <summary>
/// Manages the dive map camera, HUD rendering, and marker overlay.
/// Minimap follows player with zoom; M key toggles full-level center view.
/// </summary>
public class DiveMapBehaviour : MonoBehaviour
{
    public DiveMapBehaviour(System.IntPtr ptr) : base(ptr) { }

    // Map camera & rendering
    private Camera _mapCamera;
    private RenderTexture _renderTexture;

    // HUD elements
    private GameObject _canvasGO;
    private RectTransform _containerRT;
    private RawImage _mapImage;
    private Image _borderImage;
    private RectTransform _markerPanel;

    // Markers (UI image pools)
    private Image _playerMarker;
    private List<Image> _escapeMarkers;
    private List<Image> _entityMarkers;
    private const int MaxEntityMarkers = 1000;
    private const float MiniMarkerPlayer = 5f;
    private const float MiniMarkerEscape = 4f;
    private const float MiniMarkerEntity = 2.5f;
    private const float BigMarkerPlayer = 8f;
    private const float BigMarkerEscape = 6f;
    private const float BigMarkerEntity = 4f;

    // Cached scan results — separates expensive FindObjectsOfType from cheap position updates
    private bool _escapeScanned;
    private List<Vector3> _escapePosCache;                   // static, scan once
    private List<(Vector3 pos, Color color)> _staticCache;   // chests + items, rescan periodically
    private List<(Transform tr, Color color)> _fishCache;    // fish move, update pos every frame
    private int _cachedEntityCount;                          // total markers in use after last scan

    // State
    private bool _showBigMap;
    private bool _lastBigMap;     // tracks mode for SetMarkerSizes dirty check
    private bool _wasInGame;
    private float _scanTimer;
    private float _levelAspect;   // full level width/height
    private float _fullOrthoSize; // orthoSize to show entire level
    private float _cameraZ;
    private Vector2 _boundsMin;   // full level bounds
    private Vector2 _boundsMax;
    private Vector2 _viewMin;     // current camera visible bounds (updated each frame)
    private Vector2 _viewMax;
    private int _frameCount;
    private int _prevEscapeIdx;   // how many escape markers were active last frame
    private int _prevEntityIdx;   // how many entity markers were active last frame
    private int _renderSkipCount; // frame counter for camera render skipping
    private const float MinScanInterval = 1.0f;
    private const int RenderEveryNFrames = 3;  // map camera renders once per N frames (~20 FPS at 60)
    private const int TexBaseHeight = 256;     // RenderTexture height (was 512)

    private void EnsureLists()
    {
        if (_escapeMarkers == null) _escapeMarkers = new List<Image>();
        if (_entityMarkers == null) _entityMarkers = new List<Image>();
        if (_escapePosCache == null) _escapePosCache = new List<Vector3>();
        if (_staticCache == null) _staticCache = new List<(Vector3, Color)>();
        if (_fishCache == null) _fishCache = new List<(Transform, Color)>();
    }

    private void Update()
    {
        try
        {
            if (!DiveMap.Enabled.Value)
            {
                if (_canvasGO != null) _canvasGO.SetActive(false);
                if (_mapCamera != null) _mapCamera.enabled = false;
                return;
            }

            if (_frameCount < 120) { _frameCount++; return; }

            bool toggled = false;
            try { toggled = Input.GetKeyDown(DiveMap.ToggleKey.Value); }
            catch { return; }

            // Check dive scene (use _instance to avoid auto-creation)
            bool inGame = false;
            Camera mainCam = null;
            try
            {
                var igm = Singleton<InGameManager>._instance;
                if (igm != null && igm.playerCharacter != null)
                {
                    mainCam = Camera.main;
                    if (mainCam != null) inGame = true;
                }
            }
            catch { }

            if (!inGame)
            {
                if (_wasInGame) { Plugin.Log.LogInfo("DiveMap: left dive scene"); Cleanup(); }
                _wasInGame = false;
                return;
            }

            if (toggled)
            {
                _showBigMap = !_showBigMap;
                Plugin.Log.LogInfo($"DiveMap: big map {(_showBigMap ? "ON" : "OFF")}");
            }

            // Create map lazily
            if (_mapCamera == null)
            {
                if (!_wasInGame) Plugin.Log.LogInfo("DiveMap: entered dive scene, creating map...");
                try { SetupMap(mainCam); }
                catch (System.Exception e) { Plugin.Log.LogError($"DiveMap: setup failed: {e}"); _wasInGame = true; return; }
            }
            _wasInGame = true;

            // Camera renders once every N frames to reduce GPU load
            _renderSkipCount++;
            if (_mapCamera != null)
                _mapCamera.enabled = _renderSkipCount >= RenderEveryNFrames;
            if (_mapCamera != null && _mapCamera.enabled)
                _renderSkipCount = 0;
            if (_canvasGO != null) _canvasGO.SetActive(true);

            UpdateCamera();
            ApplyLayout();

            _scanTimer += Time.deltaTime;
            if (_scanTimer >= MinScanInterval) { _scanTimer = 0f; ScanEntities(); }

            UpdatePlayerMarker();
            UpdateMarkerPositions();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"DiveMap.Update: {e}");
        }
    }

    private void SetupMap(Camera mainCam)
    {
        EnsureLists();

        var igm = Singleton<InGameManager>._instance;
        if (igm == null) return;

        var boundary = igm.GetBoundary();
        _boundsMin = new Vector2(boundary.min.x, boundary.min.y);
        _boundsMax = new Vector2(boundary.max.x, boundary.max.y);

        float boundsWidth = _boundsMax.x - _boundsMin.x;
        float boundsHeight = _boundsMax.y - _boundsMin.y;

        if (boundsWidth <= 1f || boundsHeight <= 1f)
        {
            var ccb = igm.CurrentCameraBounds;
            Plugin.Log.LogInfo($"DiveMap: fallback to CurrentCameraBounds center={ccb.center} size={ccb.size}");
            _boundsMin = new Vector2(ccb.min.x, ccb.min.y);
            _boundsMax = new Vector2(ccb.max.x, ccb.max.y);
            boundsWidth = _boundsMax.x - _boundsMin.x;
            boundsHeight = _boundsMax.y - _boundsMin.y;
        }

        if (boundsWidth <= 1f || boundsHeight <= 1f)
        {
            Plugin.Log.LogWarning($"DiveMap: invalid bounds ({boundsWidth}x{boundsHeight}), skipping");
            return;
        }

        float padX = boundsWidth * 0.02f;
        float padY = boundsHeight * 0.02f;
        _boundsMin -= new Vector2(padX, padY);
        _boundsMax += new Vector2(padX, padY);
        boundsWidth = _boundsMax.x - _boundsMin.x;
        boundsHeight = _boundsMax.y - _boundsMin.y;

        _levelAspect = boundsWidth / boundsHeight;
        _fullOrthoSize = boundsHeight / 2f;
        _cameraZ = mainCam.transform.position.z;

        // RenderTexture (lower res = less GPU work)
        int texHeight = TexBaseHeight;
        int texWidth = Mathf.Clamp(Mathf.RoundToInt(texHeight * _levelAspect), 64, 2048);

        _renderTexture = new RenderTexture(texWidth, texHeight, 24);
        _renderTexture.useMipMap = false;
        _renderTexture.filterMode = FilterMode.Bilinear;

        // Map camera — CopyFrom to inherit URP pipeline
        var camGO = new GameObject("DDE_MapCamera");
        _mapCamera = camGO.AddComponent<Camera>();
        _mapCamera.CopyFrom(mainCam);
        _mapCamera.tag = "Untagged";
        _mapCamera.orthographic = true;
        _mapCamera.orthographicSize = _fullOrthoSize;
        _mapCamera.targetTexture = _renderTexture;
        _mapCamera.clearFlags = CameraClearFlags.SolidColor;
        _mapCamera.backgroundColor = new Color(0.05f, 0.08f, 0.15f, 1f);
        _mapCamera.depth = -100;
        _mapCamera.enabled = false;

        float cx = (_boundsMin.x + _boundsMax.x) / 2f;
        float cy = (_boundsMin.y + _boundsMax.y) / 2f;
        camGO.transform.position = new Vector3(cx, cy, _cameraZ);

        // Initialize view bounds to full level
        _viewMin = _boundsMin;
        _viewMax = _boundsMax;

        CreateHUD();
        Plugin.Log.LogInfo($"DiveMap: created, bounds=({_boundsMin})-({_boundsMax}), tex={texWidth}x{texHeight}");
    }

    /// <summary>
    /// Updates camera position and orthoSize based on current mode.
    /// Minimap: follow player, zoomed in. Big map: full level overview.
    /// </summary>
    private void UpdateCamera()
    {
        if (_mapCamera == null) return;

        if (_showBigMap)
        {
            // Full level view
            _mapCamera.orthographicSize = _fullOrthoSize;
            float cx = (_boundsMin.x + _boundsMax.x) / 2f;
            float cy = (_boundsMin.y + _boundsMax.y) / 2f;
            _mapCamera.transform.position = new Vector3(cx, cy, _cameraZ);

            _viewMin = _boundsMin;
            _viewMax = _boundsMax;
        }
        else
        {
            // Minimap: follow player with zoom
            float zoom = DiveMap.MiniMapZoom.Value;
            float ortho = _fullOrthoSize / zoom;
            _mapCamera.orthographicSize = ortho;

            // Camera visible half-extents
            float halfH = ortho;
            float halfW = ortho * _levelAspect;

            // Get player position
            Vector3 playerPos = _mapCamera.transform.position;
            try
            {
                var player = Singleton<InGameManager>._instance?.playerCharacter;
                if (player != null) playerPos = player.transform.position;
            }
            catch { }

            // Clamp camera so it doesn't go outside level bounds
            float camX = Mathf.Clamp(playerPos.x, _boundsMin.x + halfW, _boundsMax.x - halfW);
            float camY = Mathf.Clamp(playerPos.y, _boundsMin.y + halfH, _boundsMax.y - halfH);

            // If level is smaller than view in either axis, center on level
            if (halfW * 2 >= _boundsMax.x - _boundsMin.x)
                camX = (_boundsMin.x + _boundsMax.x) / 2f;
            if (halfH * 2 >= _boundsMax.y - _boundsMin.y)
                camY = (_boundsMin.y + _boundsMax.y) / 2f;

            _mapCamera.transform.position = new Vector3(camX, camY, _cameraZ);

            _viewMin = new Vector2(camX - halfW, camY - halfH);
            _viewMax = new Vector2(camX + halfW, camY + halfH);
        }
    }

    private void CreateHUD()
    {
        _canvasGO = new GameObject("DDE_DiveMapCanvas");
        Object.DontDestroyOnLoad(_canvasGO);

        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var containerGO = CreateUIObject("MapContainer", _canvasGO);
        _containerRT = containerGO.GetComponent<RectTransform>();
        _borderImage = containerGO.AddComponent<Image>();
        _borderImage.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);

        var mapImgGO = CreateUIObject("MapImage", containerGO);
        var mapImgRT = mapImgGO.GetComponent<RectTransform>();
        mapImgRT.anchorMin = Vector2.zero;
        mapImgRT.anchorMax = Vector2.one;
        mapImgRT.offsetMin = new Vector2(2, 2);
        mapImgRT.offsetMax = new Vector2(-2, -2);
        _mapImage = mapImgGO.AddComponent<RawImage>();
        _mapImage.texture = _renderTexture;

        var markerGO = CreateUIObject("MarkerPanel", containerGO);
        _markerPanel = markerGO.GetComponent<RectTransform>();
        _markerPanel.anchorMin = Vector2.zero;
        _markerPanel.anchorMax = Vector2.one;
        _markerPanel.offsetMin = new Vector2(2, 2);
        _markerPanel.offsetMax = new Vector2(-2, -2);

        _playerMarker = CreateMarker("Player", Color.white, MiniMarkerPlayer);

        for (int i = 0; i < 8; i++)
        {
            var m = CreateMarker("Escape_" + i, new Color(0.2f, 0.9f, 0.3f), MiniMarkerEscape);
            m.gameObject.SetActive(false);
            _escapeMarkers.Add(m);
        }

        for (int i = 0; i < MaxEntityMarkers; i++)
        {
            var m = CreateMarker("Entity_" + i, Color.white, MiniMarkerEntity);
            m.gameObject.SetActive(false);
            _entityMarkers.Add(m);
        }
    }

    private Image CreateMarker(string name, Color color, float size)
    {
        var go = CreateUIObject(name, _markerPanel.gameObject);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private void ApplyLayout()
    {
        if (_containerRT == null || _mapImage == null) return;

        bool modeChanged = _showBigMap != _lastBigMap;
        _lastBigMap = _showBigMap;

        float opacity = DiveMap.MapOpacity.Value;

        if (_showBigMap)
        {
            _containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            _containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            _containerRT.pivot = new Vector2(0.5f, 0.5f);

            float mapH = 1080f * 0.75f;
            float mapW = mapH * _levelAspect;
            if (mapW > 1920f * 0.9f) { mapW = 1920f * 0.9f; mapH = mapW / _levelAspect; }
            _containerRT.sizeDelta = new Vector2(mapW, mapH);
            _containerRT.anchoredPosition = Vector2.zero;

            _mapImage.color = new Color(1, 1, 1, Mathf.Max(opacity, 0.9f));
            _borderImage.color = new Color(0.05f, 0.05f, 0.1f, 0.95f);
            if (modeChanged) SetMarkerSizes(BigMarkerPlayer, BigMarkerEscape, BigMarkerEntity);
        }
        else
        {
            _containerRT.anchorMin = new Vector2(1, 1);
            _containerRT.anchorMax = new Vector2(1, 1);
            _containerRT.pivot = new Vector2(1, 1);

            float mapH = 1080f * DiveMap.MapSize.Value;
            float mapW = mapH * _levelAspect;
            _containerRT.sizeDelta = new Vector2(mapW, mapH);
            _containerRT.anchoredPosition = new Vector2(-16, -16);

            _mapImage.color = new Color(1, 1, 1, opacity);
            _borderImage.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);
            if (modeChanged) SetMarkerSizes(MiniMarkerPlayer, MiniMarkerEscape, MiniMarkerEntity);
        }
    }

    private void SetMarkerSizes(float player, float escape, float entity)
    {
        if (_playerMarker != null)
            _playerMarker.rectTransform.sizeDelta = new Vector2(player, player);
        if (_escapeMarkers != null)
            foreach (var m in _escapeMarkers)
                m.rectTransform.sizeDelta = new Vector2(escape, escape);
        if (_entityMarkers != null)
            foreach (var m in _entityMarkers)
                m.rectTransform.sizeDelta = new Vector2(entity, entity);
    }

    private void UpdatePlayerMarker()
    {
        if (_playerMarker == null || _markerPanel == null) return;
        try
        {
            var player = Singleton<InGameManager>._instance?.playerCharacter;
            if (player == null) return;
            bool vis = SetMarkerPosition(_playerMarker, player.transform.position);
            _playerMarker.gameObject.SetActive(vis);
        }
        catch { }
    }

    /// <summary>
    /// Expensive scan phase: FindObjectsOfType to discover entities, cache results.
    /// Called at throttled intervals (>= 0.5s).
    /// </summary>
    private void ScanEntities()
    {
        EnsureLists();

        // Escape pods/mirrors: static, scan once per scene
        if (!_escapeScanned)
        {
            _escapeScanned = true;
            _escapePosCache.Clear();
            try
            {
                foreach (var pod in Object.FindObjectsOfType<Interaction.Escape.EscapePodZone>())
                    if (pod != null) _escapePosCache.Add(pod.transform.position);
            }
            catch { }
            try
            {
                foreach (var mirror in Object.FindObjectsOfType<Interaction.Escape.EscapeMirror>())
                    if (mirror != null) _escapePosCache.Add(mirror.transform.position);
            }
            catch { }
        }

        // Static entities: chests + items (don't move, but can be opened/picked up)
        _staticCache.Clear();
        if (DiveMap.ShowItems.Value)
        {
            try
            {
                foreach (var item in Object.FindObjectsOfType<PickupInstanceItem>())
                {
                    if (item == null) continue;
                    var pos = item.transform.position;
                    if (pos == Vector3.zero) continue;
                    _staticCache.Add((pos, new Color(1f, 0.9f, 0.2f))); // yellow
                }
            }
            catch { }
        }
        if (DiveMap.ShowChests.Value)
        {
            try
            {
                foreach (var chest in Object.FindObjectsOfType<InstanceItemChest>())
                {
                    if (chest == null) continue;
                    var pos = chest.transform.position;
                    if (pos == Vector3.zero) continue;
                    if (chest.IsOpen) continue;
                    bool isO2 = chest.gameObject.name.Contains("O2");
                    var color = isO2 ? new Color(0.2f, 0.85f, 1f) : new Color(1f, 0.6f, 0.2f);
                    _staticCache.Add((pos, color));
                }
            }
            catch { }
        }

        // Moving entities: fish (need position updates every frame)
        _fishCache.Clear();
        if (DiveMap.ShowFish.Value)
        {
            try
            {
                foreach (var fish in Object.FindObjectsOfType<FishInteractionBody>())
                {
                    if (fish == null) continue;
                    if (fish.transform.position == Vector3.zero) continue;
                    // Aggressive fish lack AwayFromTarget (they attack instead of fleeing)
                    bool aggressive = fish.GetComponent<DR.AI.AwayFromTarget>() == null;
                    var color = aggressive
                        ? new Color(1f, 0.3f, 0.2f)   // red for aggressive
                        : new Color(0.3f, 0.6f, 1f);   // blue for normal
                    _fishCache.Add((fish.transform, color));
                }
            }
            catch { }
        }

        _cachedEntityCount = _staticCache.Count + _fishCache.Count;
    }

    /// <summary>
    /// Cheap reposition phase: updates all marker UI positions from cached data.
    /// Called every frame. No FindObjectsOfType — just reads cached positions/transforms.
    /// </summary>
    private void UpdateMarkerPositions()
    {
        EnsureLists();

        // Escape markers — cached positions, never change
        int escIdx = 0;
        if (DiveMap.ShowEscapePods.Value)
        {
            int escCount = System.Math.Min(_escapeMarkers.Count, _escapePosCache.Count);
            for (int i = 0; i < escCount; i++)
            {
                _escapeMarkers[i].gameObject.SetActive(SetMarkerPosition(_escapeMarkers[i], _escapePosCache[i]));
                escIdx++;
            }
        }
        // Only hide markers that were active last frame but aren't now
        for (int i = escIdx; i < _prevEscapeIdx; i++)
            _escapeMarkers[i].gameObject.SetActive(false);
        _prevEscapeIdx = escIdx;

        // Entity markers — static (chests/items) then moving (fish)
        int idx = 0;
        for (int i = 0; i < _staticCache.Count && idx < MaxEntityMarkers; i++, idx++)
        {
            var m = _entityMarkers[idx];
            m.color = _staticCache[i].color;
            m.gameObject.SetActive(SetMarkerPosition(m, _staticCache[i].pos));
        }
        for (int i = 0; i < _fishCache.Count && idx < MaxEntityMarkers; i++)
        {
            try
            {
                var tr = _fishCache[i].tr;
                if (tr == null) continue;
                var m = _entityMarkers[idx++];
                m.color = _fishCache[i].color;
                m.gameObject.SetActive(SetMarkerPosition(m, tr.position));
            }
            catch { } // destroyed fish
        }
        // Only hide markers that were active last frame but aren't now
        for (int i = idx; i < _prevEntityIdx; i++)
            _entityMarkers[i].gameObject.SetActive(false);
        _prevEntityIdx = idx;
    }

    /// <summary>
    /// Maps world position to marker panel coordinates based on current camera view.
    /// Returns false if the marker is outside the visible area.
    /// </summary>
    private bool SetMarkerPosition(Image marker, Vector3 worldPos)
    {
        float viewW = _viewMax.x - _viewMin.x;
        float viewH = _viewMax.y - _viewMin.y;
        if (viewW <= 0 || viewH <= 0) return false;

        float nx = (worldPos.x - _viewMin.x) / viewW;
        float ny = (worldPos.y - _viewMin.y) / viewH;

        if (nx < -0.02f || nx > 1.02f || ny < -0.02f || ny > 1.02f)
            return false;

        var panelSize = _markerPanel.rect.size;
        marker.rectTransform.anchoredPosition = new Vector2(nx * panelSize.x, ny * panelSize.y);
        return true;
    }

    private void Cleanup()
    {
        EnsureLists();

        if (_mapCamera != null)
        {
            _mapCamera.targetTexture = null;
            Object.Destroy(_mapCamera.gameObject);
            _mapCamera = null;
        }
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Object.Destroy(_renderTexture);
            _renderTexture = null;
        }
        if (_canvasGO != null)
        {
            Object.Destroy(_canvasGO);
            _canvasGO = null;
        }

        _mapImage = null;
        _borderImage = null;
        _containerRT = null;
        _markerPanel = null;
        _playerMarker = null;
        _escapeMarkers.Clear();
        _entityMarkers.Clear();
        _escapePosCache.Clear();
        _staticCache.Clear();
        _fishCache.Clear();
        _escapeScanned = false;
        _cachedEntityCount = 0;
        _showBigMap = false;
        _lastBigMap = false;
        _scanTimer = 0f;
        _prevEscapeIdx = 0;
        _prevEntityIdx = 0;
    }

    private void OnDestroy() { Cleanup(); }

    private static GameObject CreateUIObject(string name, GameObject parent)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }
}
