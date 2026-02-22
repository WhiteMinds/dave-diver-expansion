using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DaveDiverExpansion.Helpers;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DaveDiverExpansion.Features;

public enum MiniMapCorner { TopRight, TopLeft, BottomRight, BottomLeft }

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
    public static ConfigEntry<bool> ShowAggressiveFish;
    public static ConfigEntry<bool> ShowCatchableFish;
    public static ConfigEntry<bool> ShowItems;
    public static ConfigEntry<bool> ShowChests;
    public static ConfigEntry<float> MapSize;
    public static ConfigEntry<float> MapOpacity;
    public static ConfigEntry<float> MiniMapZoom;
    public static ConfigEntry<bool> MiniMapEnabled;
    public static ConfigEntry<MiniMapCorner> MiniMapPosition;
    public static ConfigEntry<float> MiniMapOffsetX;
    public static ConfigEntry<float> MiniMapOffsetY;
    public static ConfigEntry<bool> ShowOres;
    public static ConfigEntry<float> MarkerScale;
    public static ConfigEntry<bool> DebugLog;

    public static void Init(ConfigFile config)
    {
        // General
        Enabled = config.Bind(
            "DiveMap", "Enabled", true,
            "Enable the dive map HUD");
        ToggleKey = config.Bind(
            "DiveMap", "ToggleKey", KeyCode.M,
            "Key to toggle the enlarged map view");
        // Minimap appearance
        MiniMapEnabled = config.Bind(
            "DiveMap", "MiniMapEnabled", true,
            "Show the minimap overlay during diving");
        MiniMapPosition = config.Bind(
            "DiveMap", "MiniMapPosition", MiniMapCorner.TopRight,
            "Screen corner for the minimap");
        MiniMapOffsetX = config.Bind(
            "DiveMap", "MiniMapOffsetX", 16f,
            new ConfigDescription("Minimap horizontal offset from screen edge",
                new AcceptableValueRange<float>(0f, 500f)));
        MiniMapOffsetY = config.Bind(
            "DiveMap", "MiniMapOffsetY", 16f,
            new ConfigDescription("Minimap vertical offset from screen edge",
                new AcceptableValueRange<float>(0f, 500f)));
        MapSize = config.Bind(
            "DiveMap", "MapSize", 0.3f,
            new ConfigDescription("Minimap size as fraction of screen height",
                new AcceptableValueRange<float>(0.15f, 0.5f)));
        MiniMapZoom = config.Bind(
            "DiveMap", "MiniMapZoom", 3f,
            new ConfigDescription("Minimap zoom level (higher = more zoomed in)",
                new AcceptableValueRange<float>(1f, 10f)));
        MapOpacity = config.Bind(
            "DiveMap", "MapOpacity", 0.8f,
            new ConfigDescription("Minimap opacity",
                new AcceptableValueRange<float>(0.3f, 1.0f)));
        // Markers
        ShowEscapePods = config.Bind(
            "DiveMap", "ShowEscapePods", true,
            "Show escape pod/mirror markers on the map");
        ShowOres = config.Bind(
            "DiveMap", "ShowOres", true,
            "Show ore/mineral markers on the map");
        ShowFish = config.Bind(
            "DiveMap", "ShowFish", false,
            "Show normal fish markers on the map (non-aggressive, non-catchable)");
        ShowAggressiveFish = config.Bind(
            "DiveMap", "ShowAggressiveFish", true,
            "Show aggressive fish markers on the map (e.g. sharks, piranhas)");
        ShowCatchableFish = config.Bind(
            "DiveMap", "ShowCatchableFish", true,
            "Show catchable fish markers on the map (e.g. shrimp, seahorse)");
        ShowItems = config.Bind(
            "DiveMap", "ShowItems", false,
            "Show item markers on the map");
        ShowChests = config.Bind(
            "DiveMap", "ShowChests", false,
            "Show chest markers on the map");
        MarkerScale = config.Bind(
            "DiveMap", "MarkerScale", 1f,
            new ConfigDescription("Scale multiplier for all map markers",
                new AcceptableValueRange<float>(0.5f, 3f)));
        DebugLog = config.Bind(
            "Debug", "DebugLog", false,
            "Enable verbose debug logging for DiveMap diagnostics");
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

    // Marker shape types for visual differentiation
    private enum MarkerShape { Circle, Diamond, Triangle, Square }
    private static readonly Dictionary<MarkerShape, Sprite> _shapeSprites = new();

    /// <summary>
    /// Returns a cached 20×20 white sprite with dark border for the given shape.
    /// 3 zones: white fill → solid dark border (1.5px) → semi-transparent outer glow (2px fade).
    /// Color is applied via Image.color (Unity multiplies sprite × color).
    /// </summary>
    private static Sprite GetShapeSprite(MarkerShape shape)
    {
        if (_shapeSprites.TryGetValue(shape, out var cached)) return cached;

        const int sz = 20;
        const float center = (sz - 1) / 2f; // 9.5
        const float shapeR = 7f;             // shape fill radius (inner)
        var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < sz; y++)
        {
            for (int x = 0; x < sz; x++)
            {
                float dx = x - center;
                float dy = y - center;
                // Signed distance to shape edge (negative = inside)
                float dist;
                switch (shape)
                {
                    case MarkerShape.Circle:
                        dist = Mathf.Sqrt(dx * dx + dy * dy) - shapeR;
                        break;
                    case MarkerShape.Diamond:
                        dist = (Mathf.Abs(dx) + Mathf.Abs(dy)) - shapeR;
                        break;
                    case MarkerShape.Triangle:
                    {
                        // Inverted triangle ▼: flat top at y=sz-1, tip at y=0
                        float normY = (y - (center - shapeR)) / (shapeR * 2f);
                        normY = Mathf.Clamp01(normY);
                        float halfW = shapeR * normY;
                        dist = Mathf.Abs(dx) - halfW;
                        if (y > center + shapeR) dist = Mathf.Max(dist, y - (center + shapeR));
                        if (y < center - shapeR) dist = Mathf.Max(dist, (center - shapeR) - y);
                        break;
                    }
                    default: // Square
                        dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) - shapeR;
                        break;
                }

                Color32 pixel;
                if (dist < -1.5f)
                    pixel = new Color32(255, 255, 255, 255);       // white fill
                else if (dist < 0.5f)
                    pixel = new Color32(20, 20, 20, 240);          // solid dark border
                else if (dist < 2.5f)
                {
                    // Outer glow: fade out alpha
                    float t = (dist - 0.5f) / 2f;                 // 0..1
                    byte a = (byte)(160 * (1f - t));
                    pixel = new Color32(0, 0, 0, a);
                }
                else
                    pixel = new Color32(0, 0, 0, 0);              // transparent

                tex.SetPixel(x, y, pixel);
            }
        }

        tex.Apply(false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f), sz);
        _shapeSprites[shape] = sprite;
        return sprite;
    }

    // Map camera & rendering
    private Camera _mapCamera;
    private RenderTexture _renderTexture;

    // HUD elements
    private GameObject _canvasGO;
    private RectTransform _containerRT;
    private RawImage _mapImage;
    private Image _borderImage;
    private RectTransform _markerPanel;
    private GameObject _legendPanel;  // big map legend (color/shape key)
    private List<(Text text, string key)> _legendTexts; // cached for language refresh
    private bool _legendLangChinese = true; // init to opposite of likely default to force first refresh

    // Markers (UI image pools)
    private Image _playerMarker;
    private List<Image> _escapeMarkers;
    private List<Image> _entityMarkers;
    private const int MaxEntityMarkers = 1000;
    private const float MiniMarkerPlayer = 12f;
    private const float MiniMarkerEscape = 10f;
    private const float MiniMarkerEntity = 7f;
    private const float BigMarkerPlayer = 16f;
    private const float BigMarkerEscape = 12f;
    private const float BigMarkerEntity = 8f;

    // Cached scan results — separates expensive FindObjectsOfType from cheap position updates
    private bool _escapeScanned;
    private List<Vector3> _escapePosCache;                   // static, scan once
    private List<(Vector3 pos, Color color, MarkerShape shape)> _staticCache;   // chests + items, rescan periodically
    private List<(Transform tr, Color color, MarkerShape shape)> _fishCache;    // fish move, update pos every frame
    private List<(Vector3 pos, Color color, MarkerShape shape)> _oreCache;     // ores are static, rescan periodically
    private int _cachedEntityCount;                          // total markers in use after last scan

    // Night light overlay: renderers to hide during map camera rendering
    private List<Renderer> _nightOverlayRenderers;
    private bool _nightOverlayScanned;

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
    private HashSet<string> _fishDebugLogged; // log each fish prefab name only once
    private bool _chestDebugDone;  // log chest details only once per scene
    private const float MinScanInterval = 1.0f;
    private const int RenderEveryNFrames = 3;  // map camera renders once per N frames (~20 FPS at 60)
    private const int TexBaseHeight = 256;     // RenderTexture height for minimap
    private const int TexBigHeight = 1024;     // RenderTexture height for big map
    private int _currentTexSize;               // current RenderTexture dimension
    private const float BaseMiniMapOrtho = 45f; // fixed minimap half-height in world units (visible height = 90u)

    // IL2CPP low-level pointers for reading FishAggressionType without Sirenix reference
    // FishAggressionType: None=0, OnlyRun=1, Attack=2, Custom=3, Neutral=4, OnlyMoveWaypoint=5
    private static System.IntPtr _saFishSysClassPtr;
    private static System.IntPtr _fishAIDataFieldPtr;
    private static System.IntPtr _aggrTypeFieldPtr;
    private static bool _aggrCacheInit;

    private void EnsureLists()
    {
        if (_escapeMarkers == null) _escapeMarkers = new List<Image>();
        if (_entityMarkers == null) _entityMarkers = new List<Image>();
        if (_escapePosCache == null) _escapePosCache = new List<Vector3>();
        if (_staticCache == null) _staticCache = new List<(Vector3, Color, MarkerShape)>();
        if (_fishCache == null) _fishCache = new List<(Transform, Color, MarkerShape)>();
        if (_oreCache == null) _oreCache = new List<(Vector3, Color, MarkerShape)>();
        if (_nightOverlayRenderers == null) _nightOverlayRenderers = new List<Renderer>();
        if (_legendTexts == null) _legendTexts = new List<(Text, string)>();
        if (_fishDebugLogged == null) _fishDebugLogged = new HashSet<string>();
    }

    private void Update()
    {
        try
        {
            if (!DiveMap.Enabled.Value)
            {
                if (_canvasGO != null) _canvasGO.SetActive(false);
                return;
            }

            if (_frameCount < 120) { _frameCount++; return; }

            bool toggled = false;
            try { toggled = Input.GetKeyDown(DiveMap.ToggleKey.Value); }
            catch { return; }

            // ESC closes big map (before pause menu check so both can fire)
            if (Input.GetKeyDown(KeyCode.Escape) && _showBigMap)
            {
                _showBigMap = false;
                Plugin.Log.LogInfo("DiveMap: big map closed via ESC");
            }

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

            // Disable in merfolk village (has its own M-key map)
            if (inGame)
            {
                try
                {
                    var sceneName = SceneManager.GetActiveScene().name;
                    if (sceneName.Contains("MermanVillage") || sceneName.StartsWith("MV_"))
                        inGame = false;
                }
                catch { }
            }

            if (!inGame)
            {
                if (_wasInGame) { Plugin.Log.LogInfo("DiveMap: left dive scene"); Cleanup(); }
                _wasInGame = false;
                return;
            }

            // Hide minimap and ignore M key when pause menu is open (ESC during diving)
            bool pauseMenuOpen = false;
            try
            {
                var mcm = Singleton<MainCanvasManager>._instance;
                if (mcm != null)
                {
                    var pausePanel = mcm.pausePopupPanel;
                    if (pausePanel != null && pausePanel.gameObject.activeSelf)
                        pauseMenuOpen = true;
                }
            }
            catch { }

            if (pauseMenuOpen)
            {
                if (_canvasGO != null) _canvasGO.SetActive(false);
                return;
            }

            // Hide map during cutscenes / scripted actions
            var player = Singleton<InGameManager>._instance?.playerCharacter;
            if (player != null && (player.IsScenarioPlaying || player.IsActionLock))
            {
                if (_showBigMap) _showBigMap = false;
                _canvasGO?.SetActive(false);
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

            if (_canvasGO != null) _canvasGO.SetActive(true);

            UpdateCamera();
            ApplyLayout();

            _scanTimer += Time.deltaTime;
            if (_scanTimer >= MinScanInterval)
            {
                _scanTimer = 0f;
                if (!_nightOverlayScanned) ScanNightOverlay();
                ScanEntities();
            }

            // Manual render: big map every frame, minimap every N frames
            _renderSkipCount++;
            int renderInterval = _showBigMap ? 1 : RenderEveryNFrames;
            if (_renderSkipCount >= renderInterval && _mapCamera != null)
            {
                _renderSkipCount = 0;
                RenderMapCamera();
            }

            UpdatePlayerMarker();
            UpdateMarkerPositions();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"DiveMap.Update: {e}");
        }
    }

    /// <summary>
    /// Renders the map camera manually, temporarily hiding night overlay renderers
    /// so the headlight darkness mask doesn't appear on the map.
    /// </summary>
    private void RenderMapCamera()
    {
        EnsureLists();

        // Temporarily hide night overlay renderers
        var wasEnabled = new List<bool>(_nightOverlayRenderers.Count);
        for (int i = 0; i < _nightOverlayRenderers.Count; i++)
        {
            try
            {
                var r = _nightOverlayRenderers[i];
                if (r == null) { wasEnabled.Add(false); continue; }
                wasEnabled.Add(r.enabled);
                r.enabled = false;
            }
            catch { wasEnabled.Add(false); }
        }

        // Render
        try { _mapCamera.Render(); }
        catch (System.Exception e) { Plugin.Log.LogError($"DiveMap: Camera.Render() failed: {e.Message}"); }

        // Restore
        for (int i = 0; i < _nightOverlayRenderers.Count; i++)
        {
            try
            {
                var r = _nightOverlayRenderers[i];
                if (r != null) r.enabled = wasEnabled[i];
            }
            catch { }
        }
    }

    /// <summary>
    /// Scans the player's child objects for headlight overlay renderers
    /// (HeadLightOuter_Deep/Night/Glacier — large sprites on Default layer, sortingLayer=Player).
    /// These are hidden during manual Camera.Render() to prevent the night darkness mask
    /// from appearing on the map.
    /// </summary>
    private void ScanNightOverlay()
    {
        EnsureLists();
        _nightOverlayRenderers.Clear();

        try
        {
            var player = Singleton<InGameManager>._instance?.playerCharacter;
            if (player == null) return;

            var renderers = player.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                // HeadLightOuter_* are ~10x10 unit sprites that create the night light cone
                if (r.gameObject.name.StartsWith("HeadLightOuter"))
                {
                    _nightOverlayRenderers.Add(r);
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"DiveMap: night overlay scan failed: {e.Message}");
        }

        if (_nightOverlayRenderers.Count > 0)
        {
            Plugin.Log.LogInfo($"DiveMap: found {_nightOverlayRenderers.Count} headlight overlay renderers to hide during map render");
            _nightOverlayScanned = true;
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

        // Union all camera sub-bounds to get the full level extent
        // (GetBoundary() only returns the current sub-region, not the whole level)
        try
        {
            var subBoundsCol = igm.SubBoundsCollection;
            if (subBoundsCol != null)
            {
                var boundsList = subBoundsCol.m_BoundsList;
                if (boundsList != null && boundsList.Count > 0)
                {
                    for (int i = 0; i < boundsList.Count; i++)
                    {
                        var sb = boundsList[i];
                        if (sb == null) continue;
                        var b = sb.Bounds;
                        _boundsMin = Vector2.Min(_boundsMin, new Vector2(b.min.x, b.min.y));
                        _boundsMax = Vector2.Max(_boundsMax, new Vector2(b.max.x, b.max.y));
                    }
                    Plugin.Log.LogInfo($"DiveMap: merged {boundsList.Count} sub-bounds");
                }
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"DiveMap: SubBounds scan failed: {e.Message}"); }

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

        // Square RenderTexture — camera renders 1:1 area, minimap is always square,
        // big map uses uvRect to crop to level aspect
        int texSize = TexBaseHeight;
        _currentTexSize = texSize;

        _renderTexture = new RenderTexture(texSize, texSize, 24);
        _renderTexture.useMipMap = false;
        _renderTexture.filterMode = FilterMode.Bilinear;

        // Map camera — CopyFrom to inherit URP pipeline, always disabled (we use manual Render())
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
        _mapCamera.enabled = false; // always disabled, we call Render() manually

        float cx = (_boundsMin.x + _boundsMax.x) / 2f;
        float cy = (_boundsMin.y + _boundsMax.y) / 2f;
        camGO.transform.position = new Vector3(cx, cy, _cameraZ);

        // Initialize view bounds to full level
        _viewMin = _boundsMin;
        _viewMax = _boundsMax;

        CreateHUD();
        Plugin.Log.LogInfo($"DiveMap: created, bounds=({_boundsMin})-({_boundsMax}), aspect={_levelAspect:F2}, tex={texSize}x{texSize}");
    }

    /// <summary>
    /// Recreates the RenderTexture at a new size and rebinds camera + RawImage.
    /// </summary>
    private void EnsureTextureSize(int size)
    {
        if (size == _currentTexSize || _mapCamera == null) return;

        if (_renderTexture != null)
        {
            _mapCamera.targetTexture = null;
            _renderTexture.Release();
            Object.Destroy(_renderTexture);
        }

        _currentTexSize = size;
        _renderTexture = new RenderTexture(size, size, 24);
        _renderTexture.useMipMap = false;
        _renderTexture.filterMode = FilterMode.Bilinear;
        _mapCamera.targetTexture = _renderTexture;
        if (_mapImage != null) _mapImage.texture = _renderTexture;

        Plugin.Log.LogInfo($"DiveMap: texture resized to {size}x{size}");
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
            // Full level view — ortho must cover entire level in both axes (square texture)
            float bigOrtho = Mathf.Max(_fullOrthoSize, _fullOrthoSize * _levelAspect);
            _mapCamera.orthographicSize = bigOrtho;
            float cx = (_boundsMin.x + _boundsMax.x) / 2f;
            float cy = (_boundsMin.y + _boundsMax.y) / 2f;
            _mapCamera.transform.position = new Vector3(cx, cy, _cameraZ);

            _viewMin = _boundsMin;
            _viewMax = _boundsMax;
        }
        else
        {
            // Minimap: follow player with zoom, fixed world-space viewport
            float zoom = DiveMap.MiniMapZoom.Value;
            float ortho = BaseMiniMapOrtho / zoom;
            _mapCamera.orthographicSize = ortho;

            // Square texture → halfW = halfH = ortho
            float half = ortho;

            // Get player position
            Vector3 playerPos = _mapCamera.transform.position;
            try
            {
                var player = Singleton<InGameManager>._instance?.playerCharacter;
                if (player != null) playerPos = player.transform.position;
            }
            catch { }

            // Clamp camera so it doesn't go outside level bounds
            float camX = Mathf.Clamp(playerPos.x, _boundsMin.x + half, _boundsMax.x - half);
            float camY = Mathf.Clamp(playerPos.y, _boundsMin.y + half, _boundsMax.y - half);

            // If level is smaller than view in either axis, center on level
            if (half * 2 >= _boundsMax.x - _boundsMin.x)
                camX = (_boundsMin.x + _boundsMax.x) / 2f;
            if (half * 2 >= _boundsMax.y - _boundsMin.y)
                camY = (_boundsMin.y + _boundsMax.y) / 2f;

            _mapCamera.transform.position = new Vector3(camX, camY, _cameraZ);

            _viewMin = new Vector2(camX - half, camY - half);
            _viewMax = new Vector2(camX + half, camY + half);
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

        _playerMarker = CreateMarker("Player", Color.white, MiniMarkerPlayer, MarkerShape.Circle);

        for (int i = 0; i < 50; i++)
        {
            var m = CreateMarker("Escape_" + i, new Color(0.2f, 0.9f, 0.3f), MiniMarkerEscape, MarkerShape.Diamond);
            m.gameObject.SetActive(false);
            _escapeMarkers.Add(m);
        }

        for (int i = 0; i < MaxEntityMarkers; i++)
        {
            var m = CreateMarker("Entity_" + i, Color.white, MiniMarkerEntity);
            m.gameObject.SetActive(false);
            _entityMarkers.Add(m);
        }

        CreateLegend(containerGO);
    }

    private Image CreateMarker(string name, Color color, float size, MarkerShape shape = MarkerShape.Circle)
    {
        var go = CreateUIObject(name, _markerPanel.gameObject);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.sprite = GetShapeSprite(shape);
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

            // Crop square texture to show only the level portion
            if (_levelAspect >= 1f)
                _mapImage.uvRect = new Rect(0f, (1f - 1f / _levelAspect) / 2f, 1f, 1f / _levelAspect);
            else
                _mapImage.uvRect = new Rect((1f - _levelAspect) / 2f, 0f, _levelAspect, 1f);

            _mapImage.color = new Color(1, 1, 1, Mathf.Max(opacity, 0.9f));
            _borderImage.color = new Color(0.05f, 0.05f, 0.1f, 0.95f);
            if (_legendPanel != null)
            {
                _legendPanel.SetActive(true);
                bool isChinese = I18n.IsChinese();
                if (isChinese != _legendLangChinese)
                {
                    _legendLangChinese = isChinese;
                    RefreshLegendTexts();
                }
            }
            if (modeChanged)
            {
                EnsureTextureSize(TexBigHeight);
                SetMarkerSizes(BigMarkerPlayer, BigMarkerEscape, BigMarkerEntity);
            }
        }
        else
        {
            if (_legendPanel != null) _legendPanel.SetActive(false);
            // Hide canvas when minimap is disabled and big map is off
            if (!DiveMap.MiniMapEnabled.Value)
            {
                if (_canvasGO != null) _canvasGO.SetActive(false);
                return;
            }

            // Position minimap in the configured corner
            float ax, ay;
            switch (DiveMap.MiniMapPosition.Value)
            {
                case MiniMapCorner.TopLeft:     ax = 0; ay = 1; break;
                case MiniMapCorner.BottomRight: ax = 1; ay = 0; break;
                case MiniMapCorner.BottomLeft:  ax = 0; ay = 0; break;
                default:                        ax = 1; ay = 1; break; // TopRight
            }
            _containerRT.anchorMin = new Vector2(ax, ay);
            _containerRT.anchorMax = new Vector2(ax, ay);
            _containerRT.pivot = new Vector2(ax, ay);

            float mapH = 1080f * DiveMap.MapSize.Value;
            _containerRT.sizeDelta = new Vector2(mapH, mapH);

            float ox = DiveMap.MiniMapOffsetX.Value * (ax >= 0.5f ? -1 : 1);
            float oy = DiveMap.MiniMapOffsetY.Value * (ay >= 0.5f ? -1 : 1);
            _containerRT.anchoredPosition = new Vector2(ox, oy);

            _mapImage.uvRect = new Rect(0f, 0f, 1f, 1f);

            _mapImage.color = new Color(1, 1, 1, opacity);
            _borderImage.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);
            if (modeChanged)
            {
                EnsureTextureSize(TexBaseHeight);
                SetMarkerSizes(MiniMarkerPlayer, MiniMarkerEscape, MiniMarkerEntity);
            }
        }
    }

    private void SetMarkerSizes(float player, float escape, float entity)
    {
        float s = DiveMap.MarkerScale.Value;
        float ps = player * s, es = escape * s, ens = entity * s;
        if (_playerMarker != null)
            _playerMarker.rectTransform.sizeDelta = new Vector2(ps, ps);
        if (_escapeMarkers != null)
            foreach (var m in _escapeMarkers)
                m.rectTransform.sizeDelta = new Vector2(es, es);
        if (_entityMarkers != null)
            foreach (var m in _entityMarkers)
                m.rectTransform.sizeDelta = new Vector2(ens, ens);
    }

    private void UpdatePlayerMarker()
    {
        if (_playerMarker == null || _markerPanel == null) return;
        try
        {
            var player = Singleton<InGameManager>._instance?.playerCharacter;
            if (player == null) return;
            float zoomScale = _showBigMap ? 1f : DiveMap.MiniMapZoom.Value / 3f;
            float ps = (_showBigMap ? BigMarkerPlayer : MiniMarkerPlayer) * DiveMap.MarkerScale.Value * zoomScale;
            _playerMarker.rectTransform.sizeDelta = new Vector2(ps, ps);
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
        int itemSkipped = 0;
        if (DiveMap.ShowItems.Value)
        {
            try
            {
                foreach (var item in EntityRegistry.AllItems)
                {
                    if (item == null) continue;
                    if (!item.gameObject.activeInHierarchy) { itemSkipped++; continue; }
                    var pos = item.transform.position;
                    if (pos == Vector3.zero) continue;
                    _staticCache.Add((pos, new Color(1f, 0.9f, 0.2f), MarkerShape.Diamond)); // yellow item
                }
            }
            catch { }
        }
        int chestSkipped = 0;
        bool debugChest = DiveMap.DebugLog.Value && !_chestDebugDone;
        if (DiveMap.ShowChests.Value)
        {
            try
            {
                foreach (var chest in EntityRegistry.AllChests)
                {
                    if (chest == null) continue;
                    bool active = chest.gameObject.activeInHierarchy;
                    if (!active) { chestSkipped++; if (debugChest) Plugin.Log.LogInfo($"[DiveMap] chest: {chest.gameObject.name} SKIP(inactive)"); continue; }
                    bool isOpen = false;
                    try { isOpen = chest.IsOpen; } catch { chestSkipped++; continue; }
                    if (isOpen) { chestSkipped++; if (debugChest) Plugin.Log.LogInfo($"[DiveMap] chest: {chest.gameObject.name} SKIP(IsOpen)"); continue; }
                    var pos = chest.transform.position;
                    if (pos == Vector3.zero) continue;
                    var chestName = chest.gameObject.name;
                    bool isO2 = chestName.Contains("O2") || chestName.Contains("ShellFish004");
                    bool isIngredient = chestName.Contains("IngredientPot");
                    var color = isO2 ? new Color(0.2f, 0.85f, 1f)
                              : isIngredient ? new Color(0.85f, 0.2f, 0.6f)
                              : new Color(1f, 0.6f, 0.2f);
                    if (debugChest) Plugin.Log.LogInfo($"[DiveMap] chest: {chestName} IsOpen={isOpen} pos={pos}");
                    _staticCache.Add((pos, color, MarkerShape.Square));
                }
            }
            catch { }
            if (debugChest) _chestDebugDone = true;
        }

        // Moving entities: fish (need position updates every frame)
        _fishCache.Clear();
        int fishSkipped = 0;
        bool showNormalFish = DiveMap.ShowFish.Value;
        bool showAggrFish = DiveMap.ShowAggressiveFish.Value;
        bool showCatchFish = DiveMap.ShowCatchableFish.Value;
        if (showNormalFish || showAggrFish || showCatchFish)
        {
            try
            {
                bool debugFish = DiveMap.DebugLog.Value;
                foreach (var fish in EntityRegistry.AllFish)
                {
                    if (fish == null) continue;
                    if (!fish.gameObject.activeInHierarchy) { fishSkipped++; continue; }
                    if (fish.transform.position == Vector3.zero) continue;
                    // Determine aggression via FishAIData.AggressionType
                    // Attack=2 always aggressive; Custom=3 depends on AwayFromTarget
                    int aggrType = GetAggressionType(fish);
                    bool hasAFT = fish.GetComponent<DR.AI.AwayFromTarget>() != null;
                    bool aggressive;
                    if (aggrType >= 0)
                        aggressive = aggrType == 2 || (aggrType == 3 && !hasAFT);
                    else
                        aggressive = !hasAFT; // fallback
                    // Catchable fish (shrimp, seahorse): Custom + AwayFromTarget
                    bool catchable = aggrType == 3 && hasAFT;

                    // Check per-type config
                    if (aggressive && !showAggrFish) { fishSkipped++; continue; }
                    if (catchable && !showCatchFish) { fishSkipped++; continue; }
                    if (!aggressive && !catchable && !showNormalFish) { fishSkipped++; continue; }

                    var color = aggressive
                        ? new Color(1f, 0.3f, 0.2f)   // red for aggressive
                        : catchable
                            ? new Color(0.4f, 1f, 0.4f)  // green for catchable (shrimp/seahorse)
                            : new Color(0.3f, 0.6f, 1f);  // blue for normal
                    var fishShape = aggressive ? MarkerShape.Triangle : MarkerShape.Circle;
                    if (debugFish && _fishDebugLogged.Add(fish.gameObject.name))
                        LogFishDebug(fish, aggressive);
                    _fishCache.Add((fish.transform, color, fishShape));
                }
            }
            catch { }
        }

        // Ores / mining nodes
        _oreCache.Clear();
        if (DiveMap.ShowOres.Value)
        {
            var oreColor = new Color(1f, 0.5f, 0.9f); // pink/magenta
            try
            {
                foreach (var ore in EntityRegistry.AllBreakableOres)
                {
                    if (ore == null) continue;
                    try { if (ore.IsDead()) continue; } catch { continue; }
                    // Skip activeInHierarchy — game deactivates distant ores but positions remain valid
                    var pos = ore.transform.position;
                    if (pos == Vector3.zero) continue;
                    _oreCache.Add((pos, oreColor, MarkerShape.Diamond));
                }
            }
            catch { }
            try
            {
                foreach (var node in EntityRegistry.AllMiningNodes)
                {
                    if (node == null) continue;
                    try { if (node.isClear) continue; } catch { continue; }
                    var pos = node.transform.position;
                    if (pos == Vector3.zero) continue;
                    _oreCache.Add((pos, oreColor, MarkerShape.Diamond));
                }
            }
            catch { }
        }

        _cachedEntityCount = _staticCache.Count + _fishCache.Count + _oreCache.Count;

        if (DiveMap.DebugLog.Value)
        {
            Plugin.Log.LogInfo($"[DiveMap] scan: static={_staticCache.Count}(itemSkip={itemSkipped},chestSkip={chestSkipped})" +
                $" fish={_fishCache.Count}(skip={fishSkipped}) ores={_oreCache.Count}" +
                $" registry(fish={EntityRegistry.AllFish.Count},items={EntityRegistry.AllItems.Count},chests={EntityRegistry.AllChests.Count},ores={EntityRegistry.AllBreakableOres.Count},mining={EntityRegistry.AllMiningNodes.Count})");
        }
    }

    /// <summary>
    /// Cheap reposition phase: updates all marker UI positions from cached data.
    /// Called every frame. No FindObjectsOfType — just reads cached positions/transforms.
    /// </summary>
    private void UpdateMarkerPositions()
    {
        EnsureLists();

        // Escape markers — cached positions, never change
        float zoomScale = _showBigMap ? 1f : DiveMap.MiniMapZoom.Value / 3f;
        float escSize = (_showBigMap ? BigMarkerEscape : MiniMarkerEscape) * DiveMap.MarkerScale.Value * zoomScale;
        int escIdx = 0;
        if (DiveMap.ShowEscapePods.Value)
        {
            int escCount = System.Math.Min(_escapeMarkers.Count, _escapePosCache.Count);
            for (int i = 0; i < escCount; i++)
            {
                _escapeMarkers[i].rectTransform.sizeDelta = new Vector2(escSize, escSize);
                _escapeMarkers[i].gameObject.SetActive(SetMarkerPosition(_escapeMarkers[i], _escapePosCache[i]));
                escIdx++;
            }
        }
        // Only hide markers that were active last frame but aren't now
        for (int i = escIdx; i < _prevEscapeIdx; i++)
            _escapeMarkers[i].gameObject.SetActive(false);
        _prevEscapeIdx = escIdx;

        // Entity markers — static (chests/items) then moving (fish)
        float scale = DiveMap.MarkerScale.Value * zoomScale;
        float entitySize = (_showBigMap ? BigMarkerEntity : MiniMarkerEntity) * scale;
        int idx = 0;
        for (int i = 0; i < _staticCache.Count && idx < MaxEntityMarkers; i++, idx++)
        {
            var m = _entityMarkers[idx];
            m.sprite = GetShapeSprite(_staticCache[i].shape);
            m.color = _staticCache[i].color;
            m.rectTransform.sizeDelta = new Vector2(entitySize, entitySize);
            m.gameObject.SetActive(SetMarkerPosition(m, _staticCache[i].pos));
        }
        for (int i = 0; i < _fishCache.Count && idx < MaxEntityMarkers; i++)
        {
            try
            {
                var tr = _fishCache[i].tr;
                if (tr == null || !tr.gameObject.activeInHierarchy) continue;
                var m = _entityMarkers[idx++];
                m.sprite = GetShapeSprite(_fishCache[i].shape);
                m.color = _fishCache[i].color;
                m.rectTransform.sizeDelta = new Vector2(entitySize, entitySize);
                m.gameObject.SetActive(SetMarkerPosition(m, tr.position));
            }
            catch { } // destroyed fish
        }
        // Ores — static, larger markers (1.4x entity size)
        float oreSize = entitySize * 1.4f;
        for (int i = 0; i < _oreCache.Count && idx < MaxEntityMarkers; i++, idx++)
        {
            var m = _entityMarkers[idx];
            m.sprite = GetShapeSprite(_oreCache[i].shape);
            m.color = _oreCache[i].color;
            m.rectTransform.sizeDelta = new Vector2(oreSize, oreSize);
            m.gameObject.SetActive(SetMarkerPosition(m, _oreCache[i].pos));
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
        _legendPanel = null;
        _legendTexts?.Clear();
        _legendLangChinese = true;
        _playerMarker = null;
        _escapeMarkers.Clear();
        _entityMarkers.Clear();
        _escapePosCache.Clear();
        _staticCache.Clear();
        _fishCache.Clear();
        _oreCache.Clear();
        _nightOverlayRenderers.Clear();
        _escapeScanned = false;
        _nightOverlayScanned = false;
        _fishDebugLogged?.Clear();
        _chestDebugDone = false;
        _cachedEntityCount = 0;
        _showBigMap = false;
        _lastBigMap = false;
        _currentTexSize = 0;
        _scanTimer = 0f;
        _prevEscapeIdx = 0;
        _prevEntityIdx = 0;
    }

    private void OnDestroy() { Cleanup(); }

    /// <summary>
    /// Reads FishAggressionType from SABaseFishSystem.FishAIData.AggressionType
    /// via IL2CPP native field offsets (avoids Sirenix type reference).
    /// Returns: None=0, OnlyRun=1, Attack=2, Custom=3, Neutral=4, OnlyMoveWaypoint=5, or -1 on failure.
    /// </summary>
    private static int GetAggressionType(FishInteractionBody fish)
    {
        if (!_aggrCacheInit)
        {
            _aggrCacheInit = true;
            try
            {
                _saFishSysClassPtr = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "DR.AI", "SABaseFishSystem");
                _fishAIDataFieldPtr = IL2CPP.GetIl2CppField(_saFishSysClassPtr, "FishAIData");
                var fishDataClass = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "SAFishData");
                _aggrTypeFieldPtr = IL2CPP.GetIl2CppField(fishDataClass, "AggressionType");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[DiveMap] AggressionType cache init failed: {e.Message}");
                _saFishSysClassPtr = System.IntPtr.Zero;
            }
        }
        if (_saFishSysClassPtr == System.IntPtr.Zero) return -1;

        try
        {
            var comps = fish.gameObject.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c.GetIl2CppType().FullName != "DR.AI.SABaseFishSystem") continue;

                var aiPtr = c.Pointer;
                int dataOffset = (int)IL2CPP.il2cpp_field_get_offset(_fishAIDataFieldPtr);
                var fishDataPtr = Marshal.ReadIntPtr(aiPtr, dataOffset);
                if (fishDataPtr == System.IntPtr.Zero) return -1;

                int aggrOffset = (int)IL2CPP.il2cpp_field_get_offset(_aggrTypeFieldPtr);
                return Marshal.ReadInt32(fishDataPtr, aggrOffset);
            }
        }
        catch { }
        return -1;
    }

    private static string AggrTypeName(int val) => val switch
    {
        0 => "None", 1 => "OnlyRun", 2 => "Attack", 3 => "Custom",
        4 => "Neutral", 5 => "OnlyMoveWaypoint", _ => $"Unknown({val})"
    };

    /// <summary>
    /// Logs detailed fish properties for debugging aggressive detection.
    /// Called once per unique fish prefab name when DebugLog is enabled.
    /// </summary>
    private static void LogFishDebug(FishInteractionBody fish, bool aggressive)
    {
        try
        {
            string name = fish.gameObject.name;
            bool hasAFT = fish.GetComponent<DR.AI.AwayFromTarget>() != null;
            int aggrType = GetAggressionType(fish);
            string interType = "?";
            try { interType = $"{fish.InteractionType}({(int)fish.InteractionType})"; } catch { }
            Plugin.Log.LogInfo($"[DiveMap] fish: {name} | AggrType={AggrTypeName(aggrType)} AFT={hasAFT} InterType={interType} → aggressive={aggressive}");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"[DiveMap] fish: {fish?.gameObject?.name ?? "?"} debug error: {e.Message}");
        }
    }

    /// <summary>
    /// Creates the legend panel showing marker color/shape meanings.
    /// Anchored to the right side of the map container, hidden by default (shown only on big map).
    /// Text references are cached in _legendTexts for dynamic language refresh.
    /// </summary>
    private void CreateLegend(GameObject container)
    {
        EnsureLists();
        _legendTexts.Clear();

        _legendPanel = CreateUIObject("Legend", container);
        var legendRT = _legendPanel.GetComponent<RectTransform>();
        // Anchor to right edge, vertically centered
        legendRT.anchorMin = new Vector2(1f, 0.5f);
        legendRT.anchorMax = new Vector2(1f, 0.5f);
        legendRT.pivot = new Vector2(0f, 0.5f);

        var legendBg = _legendPanel.AddComponent<Image>();
        legendBg.color = new Color(0.05f, 0.05f, 0.1f, 0.85f);

        // Legend entries: (i18n key, color, shape)
        var entries = new (string key, Color color, MarkerShape shape)[]
        {
            ("Player",          Color.white,                        MarkerShape.Circle),
            ("Escape Point",    new Color(0.2f, 0.9f, 0.3f),       MarkerShape.Diamond),
            ("Aggressive Fish", new Color(1f, 0.3f, 0.2f),         MarkerShape.Triangle),
            ("Normal Fish",     new Color(0.3f, 0.6f, 1f),         MarkerShape.Circle),
            ("Catchable Fish",  new Color(0.4f, 1f, 0.4f),         MarkerShape.Circle),
            ("Item",            new Color(1f, 0.9f, 0.2f),         MarkerShape.Diamond),
            ("Chest",           new Color(1f, 0.6f, 0.2f),         MarkerShape.Square),
            ("O2 Chest",        new Color(0.2f, 0.85f, 1f),        MarkerShape.Square),
            ("Material Chest",  new Color(0.85f, 0.2f, 0.6f),      MarkerShape.Square),
            ("Ore",             new Color(1f, 0.5f, 0.9f),         MarkerShape.Diamond),
        };

        const float iconSize = 12f;
        const float rowH = 18f;
        const float padX = 8f;
        const float padY = 6f;
        const float gap = 4f;
        const float textW = 80f;
        float totalH = padY * 2 + entries.Length * rowH;
        float totalW = padX * 2 + iconSize + gap + textW;

        legendRT.sizeDelta = new Vector2(totalW, totalH);
        legendRT.anchoredPosition = new Vector2(6f, 0f); // 6px gap from map edge

        for (int i = 0; i < entries.Length; i++)
        {
            float yPos = totalH - padY - rowH * (i + 0.5f);

            // Icon
            var iconGO = CreateUIObject("LIcon_" + i, _legendPanel);
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.zero;
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(padX + iconSize / 2f, yPos);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = GetShapeSprite(entries[i].shape);
            iconImg.color = entries[i].color;

            // Label
            var labelGO = CreateUIObject("LTxt_" + i, _legendPanel);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.zero;
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.sizeDelta = new Vector2(textW, rowH);
            labelRT.anchoredPosition = new Vector2(padX + iconSize + gap, yPos);
            var text = labelGO.AddComponent<Text>();
            text.text = I18n.T(entries[i].key);
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 12;
            text.color = new Color(0.9f, 0.9f, 0.9f);
            text.alignment = TextAnchor.MiddleLeft;
            _legendTexts.Add((text, entries[i].key));
        }

        _legendPanel.SetActive(false); // hidden by default (shown on big map)
    }

    /// <summary>
    /// Refreshes legend text labels from I18n. Called when big map is shown
    /// so language changes take effect immediately.
    /// </summary>
    private void RefreshLegendTexts()
    {
        if (_legendTexts == null) return;
        foreach (var (text, key) in _legendTexts)
        {
            if (text != null) text.text = I18n.T(key);
        }
    }

    private static GameObject CreateUIObject(string name, GameObject parent)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }
}
