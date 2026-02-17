using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DaveDiverExpansion.Helpers;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DaveDiverExpansion.Features;

/// <summary>
/// In-game configuration panel built with uGUI.
/// Press F1 (configurable) to toggle the settings panel overlay.
/// Auto-discovers all ConfigEntry items registered by other features.
/// Uses an Il2CppInterop-registered MonoBehaviour for global Update (works in all scenes).
/// </summary>
public static class ConfigUI
{
    private static ConfigEntry<KeyCode> _toggleKey;
    private static ConfigFile _configFile;

    private static GameObject _canvasGO;
    private static GameObject _panelGO;
    private static GameObject _overlayGO;
    private static Text _titleText;
    private static bool _isVisible;

    // Track UI controls for cleanup on rebuild
    private static readonly List<GameObject> _sectionObjects = new();

    public static void Init(ConfigFile config)
    {
        _configFile = config;

        _toggleKey = config.Bind(
            "ConfigUI", "ToggleKey", KeyCode.F1,
            "Key to open/close the in-game settings panel");

        I18n.LanguageSetting = config.Bind(
            "ConfigUI", "Language", ModLanguage.Auto,
            "UI language (Auto detects from game/system)");

        // Register our MonoBehaviour in IL2CPP type system and spawn it
        ClassInjector.RegisterTypeInIl2Cpp<ConfigUIBehaviour>();
        var go = new GameObject("DDE_ConfigUIUpdater");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ConfigUIBehaviour>();

        Plugin.Log.LogInfo("ConfigUI initialized (toggle key: " + _toggleKey.Value + ")");
    }

    /// <summary>
    /// Called every frame by ConfigUIBehaviour.Update to check hotkey.
    /// Builds UI lazily on first toggle.
    /// </summary>
    internal static void CheckToggle()
    {
        try
        {
            if (!Input.GetKeyDown(_toggleKey.Value)) return;
        }
        catch
        {
            // Input may not be ready yet during early frames
            return;
        }

        if (_canvasGO == null)
            BuildUI();

        _isVisible = !_isVisible;
        _panelGO.SetActive(_isVisible);
        _overlayGO.SetActive(_isVisible);

        if (_isVisible)
        {
            if (_titleText != null)
                _titleText.text = I18n.T("DaveDiverExpansion Settings");
            RebuildEntries();
        }
    }

    private static void BuildUI()
    {
        // Canvas
        _canvasGO = new GameObject("DDE_ConfigCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_canvasGO);

        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _canvasGO.AddComponent<GraphicRaycaster>();

        // EventSystem (only if none exists)
        if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("DDE_EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(esGO);
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Dimmed background overlay (catches clicks outside panel to close)
        _overlayGO = CreateUIObject("Overlay", _canvasGO);
        var overlayRT = _overlayGO.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.sizeDelta = Vector2.zero;
        var overlayImg = _overlayGO.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.4f);
        var overlayBtn = _overlayGO.AddComponent<Button>();
        overlayBtn.onClick.AddListener((UnityAction)delegate { Hide(); });

        // Panel
        _panelGO = CreateUIObject("Panel", _canvasGO);
        var panelRT = _panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(520, 620);
        panelRT.anchoredPosition = Vector2.zero;
        var panelImg = _panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.16f, 0.96f);

        // Block clicks from falling through the panel to the overlay
        _panelGO.AddComponent<Button>().onClick.AddListener((UnityAction)delegate { });

        var panelLayout = _panelGO.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(16, 16, 12, 12);
        panelLayout.spacing = 4;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        // Header row
        var headerGO = CreateUIObject("Header", _panelGO);
        var headerLayout = headerGO.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.spacing = 8;
        var headerLE = headerGO.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 40;
        headerLE.flexibleHeight = 0;

        // Title
        var titleGO = CreateUIObject("Title", headerGO);
        _titleText = titleGO.AddComponent<Text>();
        _titleText.text = I18n.T("DaveDiverExpansion Settings");
        _titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _titleText.fontSize = 22;
        _titleText.fontStyle = FontStyle.Bold;
        _titleText.color = new Color(0.9f, 0.9f, 0.9f);
        _titleText.alignment = TextAnchor.MiddleLeft;
        var titleLE = titleGO.AddComponent<LayoutElement>();
        titleLE.flexibleWidth = 1;

        // Close button
        var closeBtnGO = CreateUIObject("CloseBtn", headerGO);
        var closeBtnImg = closeBtnGO.AddComponent<Image>();
        closeBtnImg.color = new Color(0.8f, 0.2f, 0.2f, 0.8f);
        var closeBtn = closeBtnGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBtnImg;
        closeBtn.onClick.AddListener((UnityAction)delegate { Hide(); });
        var closeBtnLE = closeBtnGO.AddComponent<LayoutElement>();
        closeBtnLE.preferredWidth = 36;
        closeBtnLE.preferredHeight = 36;

        var closeTxtGO = CreateUIObject("X", closeBtnGO);
        var closeTxt = closeTxtGO.AddComponent<Text>();
        closeTxt.text = "X";
        closeTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        closeTxt.fontSize = 20;
        closeTxt.fontStyle = FontStyle.Bold;
        closeTxt.color = Color.white;
        closeTxt.alignment = TextAnchor.MiddleCenter;
        var closeTxtRT = closeTxtGO.GetComponent<RectTransform>();
        closeTxtRT.anchorMin = Vector2.zero;
        closeTxtRT.anchorMax = Vector2.one;
        closeTxtRT.sizeDelta = Vector2.zero;

        // Divider
        var divGO = CreateUIObject("Divider", _panelGO);
        var divImg = divGO.AddComponent<Image>();
        divImg.color = new Color(0.4f, 0.4f, 0.5f, 0.6f);
        var divLE = divGO.AddComponent<LayoutElement>();
        divLE.preferredHeight = 2;
        divLE.flexibleHeight = 0;

        // ScrollRect
        var scrollGO = CreateUIObject("Scroll", _panelGO);
        var scrollLE = scrollGO.AddComponent<LayoutElement>();
        scrollLE.flexibleHeight = 1;
        scrollLE.flexibleWidth = 1;

        var scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30;

        // Viewport (with mask)
        var viewportGO = CreateUIObject("Viewport", scrollGO);
        var viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.sizeDelta = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();
        var viewportImg = viewportGO.AddComponent<Image>();
        viewportImg.color = new Color(0, 0, 0, 0);

        // Content
        var contentGO = CreateUIObject("Content", viewportGO);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 0);
        var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(4, 4, 4, 4);
        contentLayout.spacing = 6;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;

        // Start hidden
        _panelGO.SetActive(false);
        _overlayGO.SetActive(false);
    }

    private static void RebuildEntries()
    {
        // Clear old section objects
        foreach (var go in _sectionObjects)
        {
            if (go != null)
                UnityEngine.Object.Destroy(go);
        }
        _sectionObjects.Clear();

        // Find the Content transform
        var contentRT = _panelGO.transform.Find("Scroll/Viewport/Content");
        if (contentRT == null)
        {
            Plugin.Log.LogError("ConfigUI: Content transform not found");
            return;
        }

        // Group entries by section
        var sections = new SortedDictionary<string, List<ConfigEntryBase>>();
        foreach (var kv in _configFile)
        {
            var section = kv.Key.Section;
            var entry = kv.Value;

            // Skip ToggleKey entries — KeyCode enums have hundreds of values
            // that make a dropdown unusable.
            if (entry.Definition.Key == "ToggleKey")
                continue;

            if (!sections.ContainsKey(section))
                sections[section] = new List<ConfigEntryBase>();
            sections[section].Add(entry);
        }

        foreach (var section in sections)
        {
            // Section header
            var sectionGO = CreateUIObject("Section_" + section.Key, contentRT.gameObject);
            _sectionObjects.Add(sectionGO);
            var sectionLE = sectionGO.AddComponent<LayoutElement>();
            sectionLE.preferredHeight = 30;
            sectionLE.flexibleHeight = 0;
            var sectionText = sectionGO.AddComponent<Text>();
            sectionText.text = I18n.T(section.Key);
            sectionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            sectionText.fontSize = 18;
            sectionText.fontStyle = FontStyle.Bold;
            sectionText.color = new Color(0.6f, 0.75f, 1f);
            sectionText.alignment = TextAnchor.MiddleLeft;

            foreach (var entry in section.Value)
            {
                var rowGO = CreateEntryRow(contentRT.gameObject, entry);
                _sectionObjects.Add(rowGO);
            }
        }
    }

    private static GameObject CreateEntryRow(GameObject parent, ConfigEntryBase entry)
    {
        var rowGO = CreateUIObject("Row_" + entry.Definition.Key, parent);
        var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        var rowLE = rowGO.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 36;
        rowLE.flexibleHeight = 0;

        // Label
        var labelGO = CreateUIObject("Label", rowGO);
        var labelText = labelGO.AddComponent<Text>();
        labelText.text = I18n.T(entry.Definition.Key);
        labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        labelText.fontSize = 16;
        labelText.color = new Color(0.85f, 0.85f, 0.85f);
        labelText.alignment = TextAnchor.MiddleLeft;
        var labelLE = labelGO.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 200;
        labelLE.flexibleWidth = 0;

        // Control based on type
        if (entry.SettingType == typeof(bool))
            CreateBoolControl(rowGO, entry);
        else if (entry.SettingType == typeof(float))
            CreateFloatControl(rowGO, entry);
        else if (entry.SettingType == typeof(int))
            CreateIntControl(rowGO, entry);
        else if (entry.SettingType == typeof(string))
            CreateStringControl(rowGO, entry);
        else if (entry.SettingType.IsEnum)
            CreateEnumControl(rowGO, entry);
        else
            CreateStringControl(rowGO, entry);

        return rowGO;
    }

    private static void CreateBoolControl(GameObject parent, ConfigEntryBase entry)
    {
        var toggleGO = CreateUIObject("Toggle", parent);
        var toggleLE = toggleGO.AddComponent<LayoutElement>();
        toggleLE.preferredWidth = 30;
        toggleLE.preferredHeight = 30;
        toggleLE.flexibleHeight = 0;
        toggleLE.flexibleWidth = 0;

        var bgImg = toggleGO.AddComponent<Image>();
        bgImg.color = new Color(0.35f, 0.35f, 0.4f); // brighter so OFF state is visible

        var checkGO = CreateUIObject("Checkmark", toggleGO);
        var checkRT = checkGO.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkRT.sizeDelta = Vector2.zero;
        var checkImg = checkGO.AddComponent<Image>();
        checkImg.color = new Color(0.3f, 0.8f, 0.4f);

        var toggle = toggleGO.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.isOn = (bool)entry.BoxedValue;
        toggle.onValueChanged.AddListener((UnityAction<bool>)delegate(bool val)
        {
            entry.BoxedValue = val;
        });
    }

    private static void CreateFloatControl(GameObject parent, ConfigEntryBase entry)
    {
        float min = 0f, max = 100f;
        if (entry.Description?.AcceptableValues is AcceptableValueRange<float> range)
        {
            min = range.MinValue;
            max = range.MaxValue;
        }
        else
        {
            var def = (float)entry.DefaultValue;
            min = 0f;
            max = Mathf.Max(def * 4f, 10f);
        }

        var slider = CreateSlider(parent, min, max, (float)entry.BoxedValue, false);

        var valueLabelGO = CreateUIObject("Value", parent);
        var valueText = valueLabelGO.AddComponent<Text>();
        valueText.text = ((float)entry.BoxedValue).ToString("F1");
        valueText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        valueText.fontSize = 15;
        valueText.color = new Color(0.9f, 0.9f, 0.9f);
        valueText.alignment = TextAnchor.MiddleRight;
        var valueLE = valueLabelGO.AddComponent<LayoutElement>();
        valueLE.preferredWidth = 50;
        valueLE.flexibleWidth = 0;
        valueLE.flexibleHeight = 0;

        slider.onValueChanged.AddListener((UnityAction<float>)delegate(float val)
        {
            entry.BoxedValue = val;
            valueText.text = val.ToString("F1");
        });
    }

    private static void CreateIntControl(GameObject parent, ConfigEntryBase entry)
    {
        float min = 0, max = 100;
        if (entry.Description?.AcceptableValues is AcceptableValueRange<int> range)
        {
            min = range.MinValue;
            max = range.MaxValue;
        }
        else
        {
            var def = (int)entry.DefaultValue;
            min = 0;
            max = Mathf.Max(def * 4, 100);
        }

        var slider = CreateSlider(parent, min, max, (int)entry.BoxedValue, true);

        var valueLabelGO = CreateUIObject("Value", parent);
        var valueText = valueLabelGO.AddComponent<Text>();
        valueText.text = entry.BoxedValue.ToString();
        valueText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        valueText.fontSize = 15;
        valueText.color = new Color(0.9f, 0.9f, 0.9f);
        valueText.alignment = TextAnchor.MiddleRight;
        var valueLE = valueLabelGO.AddComponent<LayoutElement>();
        valueLE.preferredWidth = 50;
        valueLE.flexibleWidth = 0;
        valueLE.flexibleHeight = 0;

        slider.onValueChanged.AddListener((UnityAction<float>)delegate(float val)
        {
            entry.BoxedValue = (int)val;
            valueText.text = ((int)val).ToString();
        });
    }

    /// <summary>
    /// Creates a Slider inside a fixed-height wrapper container.
    /// The wrapper's LayoutElement controls size; the Slider fills the wrapper via anchors
    /// so the Slider component cannot overflow its bounds.
    /// </summary>
    private static Slider CreateSlider(GameObject parent, float min, float max, float value, bool wholeNumbers)
    {
        // Wrapper container controlled by layout
        var wrapperGO = CreateUIObject("SliderWrapper", parent);
        var wrapperLE = wrapperGO.AddComponent<LayoutElement>();
        wrapperLE.flexibleWidth = 1;
        wrapperLE.preferredHeight = 24;
        wrapperLE.flexibleHeight = 0;

        // Slider fills wrapper via anchors (not layout)
        var sliderGO = CreateUIObject("Slider", wrapperGO);
        var sliderRT = sliderGO.GetComponent<RectTransform>();
        sliderRT.anchorMin = Vector2.zero;
        sliderRT.anchorMax = Vector2.one;
        sliderRT.sizeDelta = Vector2.zero;
        var sliderBG = sliderGO.AddComponent<Image>();
        sliderBG.color = new Color(0.25f, 0.25f, 0.3f);

        // Fill area (middle 50% height for a thin track look)
        var fillAreaGO = CreateUIObject("FillArea", sliderGO);
        var fillAreaRT = fillAreaGO.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1, 0.75f);
        fillAreaRT.offsetMin = new Vector2(5, 0);
        fillAreaRT.offsetMax = new Vector2(-5, 0);

        var fillGO = CreateUIObject("Fill", fillAreaGO);
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.3f, 0.6f, 0.9f);

        // Handle slide area
        var handleAreaGO = CreateUIObject("HandleArea", sliderGO);
        var handleAreaRT = handleAreaGO.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = new Vector2(5, 0);
        handleAreaRT.offsetMax = new Vector2(-5, 0);

        var handleGO = CreateUIObject("Handle", handleAreaGO);
        var handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(14, 0);
        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = new Color(0.85f, 0.85f, 0.9f);

        var slider = sliderGO.AddComponent<Slider>();
        slider.targetGraphic = handleImg;
        slider.fillRect = fillGO.GetComponent<RectTransform>();
        slider.handleRect = handleRT;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;
        slider.value = value;

        return slider;
    }

    private static void CreateStringControl(GameObject parent, ConfigEntryBase entry)
    {
        var inputGO = CreateUIObject("InputField", parent);
        var inputLE = inputGO.AddComponent<LayoutElement>();
        inputLE.flexibleWidth = 1;
        inputLE.preferredHeight = 30;
        var inputBG = inputGO.AddComponent<Image>();
        inputBG.color = new Color(0.2f, 0.2f, 0.25f);

        var textGO = CreateUIObject("Text", inputGO);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6, 2);
        textRT.offsetMax = new Vector2(-6, -2);
        var text = textGO.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 15;
        text.color = new Color(0.9f, 0.9f, 0.9f);
        text.supportRichText = false;

        var inputField = inputGO.AddComponent<InputField>();
        inputField.textComponent = text;
        inputField.text = entry.BoxedValue?.ToString() ?? "";
        inputField.onEndEdit.AddListener((UnityAction<string>)delegate(string val)
        {
            try
            {
                if (entry.SettingType == typeof(string))
                    entry.BoxedValue = val;
                else
                    entry.BoxedValue = System.ComponentModel.TypeDescriptor
                        .GetConverter(entry.SettingType).ConvertFromString(val);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ConfigUI: Failed to parse '{val}' for {entry.Definition.Key}: {e.Message}");
            }
        });
    }

    private static void CreateEnumControl(GameObject parent, ConfigEntryBase entry)
    {
        var dropdownGO = CreateUIObject("Dropdown", parent);
        var dropdownLE = dropdownGO.AddComponent<LayoutElement>();
        dropdownLE.flexibleWidth = 1;
        dropdownLE.preferredHeight = 30;
        var dropdownBG = dropdownGO.AddComponent<Image>();
        dropdownBG.color = new Color(0.2f, 0.2f, 0.25f);

        var captionGO = CreateUIObject("Label", dropdownGO);
        var captionRT = captionGO.GetComponent<RectTransform>();
        captionRT.anchorMin = Vector2.zero;
        captionRT.anchorMax = Vector2.one;
        captionRT.offsetMin = new Vector2(8, 2);
        captionRT.offsetMax = new Vector2(-28, -2);
        var captionText = captionGO.AddComponent<Text>();
        captionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        captionText.fontSize = 15;
        captionText.color = new Color(0.9f, 0.9f, 0.9f);
        captionText.alignment = TextAnchor.MiddleLeft;

        var arrowGO = CreateUIObject("Arrow", dropdownGO);
        var arrowRT = arrowGO.GetComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(1, 0);
        arrowRT.anchorMax = new Vector2(1, 1);
        arrowRT.pivot = new Vector2(1, 0.5f);
        arrowRT.sizeDelta = new Vector2(24, 0);
        var arrowText = arrowGO.AddComponent<Text>();
        arrowText.text = "v";
        arrowText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        arrowText.fontSize = 14;
        arrowText.color = new Color(0.7f, 0.7f, 0.7f);
        arrowText.alignment = TextAnchor.MiddleCenter;

        // Template (hidden, used by Dropdown to create popup)
        var templateGO = CreateUIObject("Template", dropdownGO);
        templateGO.SetActive(false);
        var templateRT = templateGO.GetComponent<RectTransform>();
        templateRT.anchorMin = new Vector2(0, 0);
        templateRT.anchorMax = new Vector2(1, 0);
        templateRT.pivot = new Vector2(0.5f, 1);
        templateRT.sizeDelta = new Vector2(0, 150);
        var templateImg = templateGO.AddComponent<Image>();
        templateImg.color = new Color(0.18f, 0.18f, 0.22f);
        var templateScroll = templateGO.AddComponent<ScrollRect>();
        templateScroll.horizontal = false;
        templateScroll.movementType = ScrollRect.MovementType.Clamped;

        var viewportGO = CreateUIObject("Viewport", templateGO);
        var viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.pivot = new Vector2(0, 1);
        viewportRT.sizeDelta = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();
        var viewportImg = viewportGO.AddComponent<Image>();
        viewportImg.color = new Color(0, 0, 0, 0);

        // Content — NO VerticalLayoutGroup / ContentSizeFitter here.
        // Dropdown.Show() manually positions items and sets content height.
        var contentGO = CreateUIObject("Content", viewportGO);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 28);

        // Item template — full width, height = 28.
        // Dropdown.Show() clones this, preserves anchorMin/Max.x, overrides y.
        var itemGO = CreateUIObject("Item", contentGO);
        var itemRT = itemGO.GetComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 0.5f);
        itemRT.anchorMax = new Vector2(1, 0.5f);
        itemRT.sizeDelta = new Vector2(0, 28);
        var itemToggle = itemGO.AddComponent<Toggle>();

        var itemBG = CreateUIObject("ItemBG", itemGO);
        var itemBGRT = itemBG.GetComponent<RectTransform>();
        itemBGRT.anchorMin = Vector2.zero;
        itemBGRT.anchorMax = Vector2.one;
        itemBGRT.sizeDelta = Vector2.zero;
        var itemBGImg = itemBG.AddComponent<Image>();
        itemBGImg.color = new Color(0.25f, 0.25f, 0.3f);

        var itemLabelGO = CreateUIObject("ItemLabel", itemGO);
        var itemLabelRT = itemLabelGO.GetComponent<RectTransform>();
        itemLabelRT.anchorMin = Vector2.zero;
        itemLabelRT.anchorMax = Vector2.one;
        itemLabelRT.offsetMin = new Vector2(8, 2);
        itemLabelRT.offsetMax = new Vector2(-8, -2);
        var itemLabel = itemLabelGO.AddComponent<Text>();
        itemLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        itemLabel.fontSize = 15;
        itemLabel.color = new Color(0.9f, 0.9f, 0.9f);
        itemLabel.alignment = TextAnchor.MiddleLeft;

        itemToggle.targetGraphic = itemBGImg;

        templateScroll.viewport = viewportRT;
        templateScroll.content = contentRT;

        var dropdown = dropdownGO.AddComponent<Dropdown>();
        dropdown.targetGraphic = dropdownBG;
        dropdown.template = templateRT;
        dropdown.captionText = captionText;
        dropdown.itemText = itemLabel;

        var enumNames = Enum.GetNames(entry.SettingType);
        dropdown.options = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
        foreach (var name in enumNames)
            dropdown.options.Add(new Dropdown.OptionData(name));

        var currentVal = entry.BoxedValue.ToString();
        dropdown.value = Array.IndexOf(enumNames, currentVal);
        dropdown.RefreshShownValue();

        dropdown.onValueChanged.AddListener((UnityAction<int>)delegate(int idx)
        {
            entry.BoxedValue = Enum.Parse(entry.SettingType, enumNames[idx]);
        });
    }

    private static void Hide()
    {
        _isVisible = false;
        if (_panelGO != null)
            _panelGO.SetActive(false);
        if (_overlayGO != null)
            _overlayGO.SetActive(false);
    }

    private static GameObject CreateUIObject(string name, GameObject parent)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }
}

/// <summary>
/// Il2CppInterop-registered MonoBehaviour that runs Update every frame in all scenes.
/// Handles ConfigUI hotkey detection globally.
/// </summary>
public class ConfigUIBehaviour : MonoBehaviour
{
    public ConfigUIBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        ConfigUI.CheckToggle();
    }
}
