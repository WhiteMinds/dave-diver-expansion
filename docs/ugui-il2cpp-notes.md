# uGUI + IL2CPP 开发踩坑记录

在 BepInEx 6 IL2CPP（Unity 6）环境下，纯代码构建 uGUI 配置面板时遇到的问题和解决方案。

## 背景

Dave the Diver 使用 Unity 6 + IL2CPP 编译，IMGUI 模块被完全 strip。第三方配置管理器（官方 ConfigurationManager、sinai-dev BepInExConfigManager）均无法使用。因此自建了基于 UnityEngine.UI (uGUI) 的游戏内配置面板。

---

## 1. Harmony 补丁 ≠ 全局 Update

**问题**：将热键检测放在 `[HarmonyPatch(typeof(PlayerCharacter), "Update")]` 中，结果只有潜水场景能按 F1 打开面板——因为 `PlayerCharacter` 只在潜水时存在。

**解决方案**：使用 `ClassInjector.RegisterTypeInIl2Cpp<T>()` 注册自定义 MonoBehaviour，挂载到 `DontDestroyOnLoad` 的 GameObject 上，实现全场景 Update。

```csharp
// 在 Init() 中注册并创建
ClassInjector.RegisterTypeInIl2Cpp<ConfigUIBehaviour>();
var go = new GameObject("DDE_ConfigUIUpdater");
Object.DontDestroyOnLoad(go);
go.hideFlags = HideFlags.HideAndDontSave;
go.AddComponent<ConfigUIBehaviour>();

// MonoBehaviour 定义
public class ConfigUIBehaviour : MonoBehaviour
{
    // IL2CPP 注入类型必须有 IntPtr 构造函数
    public ConfigUIBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        ConfigUI.CheckToggle(); // 每帧检测热键，全场景生效
    }
}
```

**要点**：`(IntPtr ptr)` 构造函数是 IL2CPP 注入类型的硬性要求，缺少会导致运行时崩溃。

---

## 2. LayoutElement.flexibleHeight 默认值为 -1（可扩展）

**问题**：给 LayoutElement 设置了 `preferredHeight = 40`，期望元素固定 40px 高。但实际上元素被拉伸到远超 40px，导致标题区域出现大量空白、行高不一致。

**原因**：`flexibleHeight` 默认值为 `-1`，Unity 布局系统将其视为"可以分配额外空间"。即使 `preferredHeight` 正确，元素仍会被父容器的剩余空间撑大。

**解决方案**：**每个固定高度的 LayoutElement 都必须显式设置 `flexibleHeight = 0`**。

```csharp
var le = go.AddComponent<LayoutElement>();
le.preferredHeight = 40;
le.flexibleHeight = 0;  // 关键！阻止垂直扩展
```

**规则**：这是最常见的布局问题。只要有 `preferredHeight`，必须跟上 `flexibleHeight = 0`。

---

## 3. Slider 组件无视 LayoutElement 约束

**问题**：在 Slider 所在的 GameObject 上设置 `preferredHeight = 24` + `flexibleHeight = 0`，滑块仍然渲染成 150+ 像素高，溢出其所在行。

**原因**：Unity 的 `Slider` 组件在 IL2CPP interop 下似乎会覆盖或忽略 LayoutElement 的高度约束。直接在 Slider GO 上控制高度无效。

**解决方案**：将 Slider 包裹在一个容器 GameObject 中。容器拥有 LayoutElement 控制尺寸，Slider 通过 anchor 填充容器。

```csharp
// 容器参与布局，控制尺寸
var wrapperGO = CreateUIObject("SliderWrapper", parent);
var wrapperLE = wrapperGO.AddComponent<LayoutElement>();
wrapperLE.flexibleWidth = 1;
wrapperLE.preferredHeight = 24;
wrapperLE.flexibleHeight = 0;

// Slider 通过 anchor 填充容器（不参与布局计算）
var sliderGO = CreateUIObject("Slider", wrapperGO);
var sliderRT = sliderGO.GetComponent<RectTransform>();
sliderRT.anchorMin = Vector2.zero;
sliderRT.anchorMax = Vector2.one;
sliderRT.sizeDelta = Vector2.zero;
// ... 添加 Slider 组件、fill、handle 等
```

**通用原则**：如果某个 UI 组件无视 LayoutElement，就用容器包一层——容器管布局，组件填充容器。Scrollbar 也存在类似问题。

---

## 4. childForceExpandHeight 会拉伸所有子元素

**问题**：关闭按钮（X）被拉伸到标题行的全部高度，Toggle 复选框也被纵向拉长。

**原因**：`HorizontalLayoutGroup.childForceExpandHeight` 默认为 `true`，会强制所有子元素扩展到行的最大高度。

**解决方案**：

```csharp
var hlg = go.AddComponent<HorizontalLayoutGroup>();
hlg.childForceExpandHeight = false;  // 子元素不强制扩展
hlg.childAlignment = TextAnchor.MiddleLeft;  // 垂直居中对齐
```

---

## 5. Toggle 关闭状态在深色背景上不可见

**问题**：Toggle 的背景色 `(0.25, 0.25, 0.3)` 与面板背景 `(0.12, 0.12, 0.16)` 对比度不够。当 `isOn = false` 时，勾选图形隐藏，只剩几乎不可见的深色背景。

**解决方案**：提高 Toggle 背景亮度到 `(0.35, 0.35, 0.4)`，无论开关状态都能清晰看到复选框。

---

## 6. 构建 uGUI 需要的 interop DLL 引用

纯代码构建 uGUI 需要引用比预期更多的 interop DLL：

| DLL | 提供的类型 |
|-----|-----------|
| `UnityEngine.UI.dll` | Button, Toggle, Slider, ScrollRect, InputField, Dropdown, Text, Image, LayoutGroup 等 |
| `UnityEngine.UIModule.dll` | Canvas, CanvasScaler, GraphicRaycaster, RectMask2D, CanvasGroup |
| `UnityEngine.InputLegacyModule.dll` | `Input.GetKeyDown()`, `Input.GetKey()` |
| `UnityEngine.TextRenderingModule.dll` | `Font`, `FontStyle`, `TextAnchor` |

在 `Directory.Build.props` 的 Unity ItemGroup 中添加：

```xml
<Reference Include="UnityEngine.UI" HintPath="$(InteropPath)\UnityEngine.UI.dll" Private="False" />
<Reference Include="UnityEngine.UIModule" HintPath="$(InteropPath)\UnityEngine.UIModule.dll" Private="False" />
<Reference Include="UnityEngine.InputLegacyModule" HintPath="$(InteropPath)\UnityEngine.InputLegacyModule.dll" Private="False" />
<Reference Include="UnityEngine.TextRenderingModule" HintPath="$(InteropPath)\UnityEngine.TextRenderingModule.dll" Private="False" />
```

**技巧**：编译报错会明确指出缺少哪个模块，按需添加即可。

---

## 7. 第三方配置管理器在本游戏上不可用

| 工具 | 问题 |
|------|------|
| 官方 BepInEx ConfigurationManager | 依赖 IMGUI，Unity 6 IL2CPP 中被完全 strip（managed + native 层均无） |
| sinai-dev BepInExConfigManager | 三重问题：Il2Cpp 命名空间过时、Unity 6 TypeLoadException、AssetBundle iCall 缺失 |

**结论**：必须自建 UI。uGUI 组件在 interop DLL 中完整可用，是唯一可靠的选择。

---

## 8. Dropdown 模板的正确结构（纯代码构建）

Unity 的 `Dropdown` 组件在展开时克隆 Template 子树。`Dropdown.Show()` 内部**手动计算并设置每个 item 的 `anchoredPosition` 和 `sizeDelta`**，因此模板结构和 RectTransform 必须精确匹配其预期。

### 核心原则：不要在 Content 上使用 VerticalLayoutGroup

> **教训**：最初尝试在 Content 上添加 `VerticalLayoutGroup` + `ContentSizeFitter` 来管理 item 布局。
> 结果 VLG 与 `Dropdown.Show()` 的手动定位逻辑**互相冲突**，导致 item 被严重偏移（文字超出下拉列表左边界）。
>
> Unity 源码 `DefaultControls.CreateDropdown()` 的 Content 上**没有任何布局组件**。
> `Dropdown.Show()` 自行计算每个 item 的位置和 Content 总高度。让 Dropdown 自己管理即可。

### 标准层级结构

```
Dropdown (root)              [Image, Dropdown, LayoutElement]
├── Label                    [Text]  — 当前选中值（alignment = MiddleLeft）
├── Arrow                    [Text]  — "v" 箭头
└── Template                 [Image, ScrollRect]  — SetActive(false)!
    └── Viewport             [Image(透明), RectMask2D]
        └── Content          [无布局组件！Dropdown.Show() 手动定位]
            └── Item         [Toggle]
                ├── ItemBG   [Image]  — Toggle.targetGraphic
                └── ItemLabel [Text]  — alignment = MiddleLeft
```

### 关键 RectTransform 设置

| 元素 | anchorMin | anchorMax | pivot | sizeDelta / offset | 说明 |
|------|-----------|-----------|-------|-------------------|------|
| Template | (0, 0) | (1, 0) | (0.5, 1) | (0, 150) | 挂在 root 底部，pivot 在顶部使其向下展开 |
| Viewport | (0, 0) | (1, 1) | **(0, 1)** | (0, 0) | **pivot 必须为 (0,1)** (top-left)，否则内容偏移错误 |
| Content | (0, 1) | (1, 1) | (0.5, 1) | **(0, 28)** | 初始高度=1个item，Dropdown.Show() 会自动调整 |
| **Item** | **(0, 0.5)** | **(1, 0.5)** | — | **(0, 28)** | **全宽+固定高度，Dropdown 保留 X 锚点、覆盖 Y** |
| ItemBG | (0, 0) | (1, 1) | — | (0, 0) | 填满 Item |
| ItemLabel | (0, 0) | (1, 1) | — | offsetMin=(8,2) offsetMax=(-8,-2) | 四周留 padding |

### Item 的 RectTransform 极其关键

`Dropdown.Show()` 克隆 Item 后的处理逻辑（Unity 源码）：

```csharp
// 保留 X 轴锚点，覆盖 Y 轴为底部锚定
itemRect.anchorMin = new Vector2(itemRect.anchorMin.x, 0);  // X 保留！
itemRect.anchorMax = new Vector2(itemRect.anchorMax.x, 0);  // X 保留！
itemRect.anchoredPosition = new Vector2(itemRect.anchoredPosition.x, ...);
itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, itemSize.y);
```

因此 Item 模板的 **X 轴锚点决定了最终 item 宽度**：
- `anchorMin.x = 0, anchorMax.x = 1` → item 撑满 Content 宽度 ✅
- `anchorMin.x = 0.5, anchorMax.x = 0.5`（默认值）→ item 零宽度，文字全部溢出左边界 ❌

### ScrollRect 设置

```csharp
var scroll = templateGO.AddComponent<ScrollRect>();
scroll.horizontal = false;
scroll.movementType = ScrollRect.MovementType.Clamped;  // 不要用 Elastic
scroll.viewport = viewportRT;
scroll.content = contentRT;
```

### 完整代码示例

```csharp
// Content — 不加任何布局组件，Dropdown.Show() 手动管理
var contentGO = CreateUIObject("Content", viewportGO);
var contentRT = contentGO.GetComponent<RectTransform>();
contentRT.anchorMin = new Vector2(0, 1);
contentRT.anchorMax = new Vector2(1, 1);
contentRT.pivot = new Vector2(0.5f, 1);
contentRT.sizeDelta = new Vector2(0, 28);

// Item — anchorMin/Max.x 必须为 0/1（全宽），sizeDelta.y = item 高度
var itemGO = CreateUIObject("Item", contentGO);
var itemRT = itemGO.GetComponent<RectTransform>();
itemRT.anchorMin = new Vector2(0, 0.5f);
itemRT.anchorMax = new Vector2(1, 0.5f);
itemRT.sizeDelta = new Vector2(0, 28);
var itemToggle = itemGO.AddComponent<Toggle>();

// ItemBG — 填满 Item，作为 Toggle.targetGraphic
var itemBG = CreateUIObject("ItemBG", itemGO);
var itemBGRT = itemBG.GetComponent<RectTransform>();
itemBGRT.anchorMin = Vector2.zero;
itemBGRT.anchorMax = Vector2.one;
itemBGRT.sizeDelta = Vector2.zero;
var itemBGImg = itemBG.AddComponent<Image>();
itemToggle.targetGraphic = itemBGImg;

// ItemLabel — 填满 Item 并留 padding，居中对齐
var itemLabelGO = CreateUIObject("ItemLabel", itemGO);
var itemLabelRT = itemLabelGO.GetComponent<RectTransform>();
itemLabelRT.anchorMin = Vector2.zero;
itemLabelRT.anchorMax = Vector2.one;
itemLabelRT.offsetMin = new Vector2(8, 2);
itemLabelRT.offsetMax = new Vector2(-8, -2);
var itemLabel = itemLabelGO.AddComponent<Text>();
itemLabel.alignment = TextAnchor.MiddleLeft;
```

### 常见错误

| 症状 | 原因 |
|------|------|
| **item 文字超出左边界** | Item 的 anchorMin.x/anchorMax.x 使用了默认值 (0.5, 0.5)，导致 item 零宽度 |
| **item 文字偏移、位置混乱** | Content 上加了 VLG/ContentSizeFitter，与 Dropdown.Show() 手动定位冲突 |
| item 高度异常（过高/过矮） | Item 的 sizeDelta.y 未设置，或 Content 初始 sizeDelta.y 为 0 |
| 文字显示不全 | ItemLabel 缺少 `alignment = MiddleLeft`（默认 UpperLeft），或 offset 不合理 |
| 下拉列表内容位置偏移 | Viewport 的 `pivot` 不是 `(0, 1)` |
| 下拉列表弹性回弹 | ScrollRect.movementType 未设为 Clamped |
| KeyCode 等大枚举卡顿 | 不要对 100+ 值的枚举用 Dropdown，改用 InputField 或直接跳过 |

---

## 9. 运行时生成 Sprite（Texture2D 像素绘制）

**场景**：需要不同形状的标记图标（圆形、菱形、三角、方形），但不想嵌入外部 PNG 资源，也不想依赖游戏的 SpriteCollection/Addressables。

**方案**：启动时用 `Texture2D` 逐像素绘制形状，通过 `Sprite.Create()` 转为 Sprite，按形状缓存到 static Dictionary。颜色通过 `Image.color` 动态着色（纹理本身为白色+深色边框，Unity Image 的 color 属性会乘以 sprite 颜色）。

```csharp
// 生成 20×20 白色圆形 + 深色描边的 Sprite
var tex = new Texture2D(20, 20, TextureFormat.RGBA32, false);
tex.filterMode = FilterMode.Bilinear;
for (int y = 0; y < 20; y++)
    for (int x = 0; x < 20; x++)
    {
        float dist = Mathf.Sqrt((x-9.5f)*(x-9.5f) + (y-9.5f)*(y-9.5f)) - 7f;
        Color32 pixel;
        if (dist < -1.5f)       pixel = new Color32(255,255,255,255);  // 白色填充
        else if (dist < 0.5f)   pixel = new Color32(20,20,20,240);     // 深色描边
        else if (dist < 2.5f) { float t = (dist-0.5f)/2f; pixel = new Color32(0,0,0,(byte)(160*(1-t))); }  // 外发光
        else                    pixel = new Color32(0,0,0,0);          // 透明
        tex.SetPixel(x, y, pixel);
    }
tex.Apply(false);
var sprite = Sprite.Create(tex, new Rect(0,0,20,20), new Vector2(0.5f,0.5f), 20f);

// 使用时通过 Image.color 着色
img.sprite = sprite;
img.color = new Color(1f, 0.3f, 0.2f); // 红色
```

**要点**：
- 纹理大小（如 20×20）要匹配最终渲染大小——太大浪费内存，太小描边不可见
- 描边宽度在纹理空间中的像素数 ÷ (纹理大小 / 屏幕渲染大小) = 屏幕上的像素数。如果标记在屏幕上只有 5px，20px 纹理中 2px 的描边只有 0.5 屏幕像素
- 外发光带使用渐变 alpha (`(0,0,0,fading_alpha)`)，因为 RGB 是 0 所以 `Image.color` 乘法不会改变发光颜色（始终是黑色阴影效果）
- 形状判断使用 SDF（Signed Distance Field）思路：计算像素到形状边缘的距离，根据距离决定填充/描边/发光/透明
- 缓存为 static Dictionary，跨场景复用，不需要在 Cleanup 中销毁

---

## 10. 语言即时切换模式

**场景**：用户在 ConfigUI 面板中切换 Language dropdown 后，所有已渲染的 UI 文本（标签、section 名、图例等）应立即更新，不需要关闭重开或重启。

**方案**：在 `Update()`/`CheckToggle()` 中检测 `I18n.IsChinese()` 变化，变化时重建/刷新文本。

### ConfigUI 面板（重建策略）

ConfigUI 使用完整重建：检测语言变化后调用 `RebuildEntries()` 销毁并重建所有条目 UI。因为 ConfigUI 的条目包含复杂控件（Slider、Toggle、Dropdown），缓存-刷新的复杂度不亚于重建。

```csharp
private static bool _lastLangChinese;

// 在 CheckToggle() 中，面板可见时检测
if (_isVisible && _canvasGO != null)
{
    bool isChinese = I18n.IsChinese();
    if (isChinese != _lastLangChinese)
    {
        _lastLangChinese = isChinese;
        _titleText.text = I18n.T("DaveDiverExpansion Settings");
        RebuildEntries(); // 销毁所有条目 UI 并重建
    }
}
```

### DiveMap 图例（刷新策略）

DiveMap 图例是纯展示型（图标+文本），使用缓存刷新：创建时保存 `List<(Text text, string key)>`，语言变化时遍历更新 `text.text = I18n.T(key)`。

```csharp
private List<(Text text, string key)> _legendTexts;
private bool _legendLangChinese;

// 创建时缓存
_legendTexts.Add((text, "Catchable Fish"));

// 每帧检测（仅大地图可见时）
bool isChinese = I18n.IsChinese();
if (isChinese != _legendLangChinese)
{
    _legendLangChinese = isChinese;
    foreach (var (text, key) in _legendTexts)
        if (text != null) text.text = I18n.T(key);
}
```

**选择策略**：
- 复杂面板（含交互控件）→ 重建整个面板
- 纯文本展示 → 缓存 Text 引用 + 遍历刷新

---

## 开发 Checklist

在 IL2CPP 环境下开发 uGUI 时，对照检查：

- [ ] 自定义 MonoBehaviour 使用 `ClassInjector.RegisterTypeInIl2Cpp` 注册
- [ ] 自定义 MonoBehaviour 包含 `(IntPtr ptr) : base(ptr)` 构造函数
- [ ] 全局 Update 逻辑挂载到 `DontDestroyOnLoad` 的 GameObject 上
- [ ] 每个固定高度的 LayoutElement 都设置了 `flexibleHeight = 0`
- [ ] 复杂 UI 组件（Slider、Scrollbar）包裹在布局容器中
- [ ] LayoutGroup 的 `childForceExpandHeight` 根据需要设为 `false`
- [ ] 深色背景上的交互元素有足够的颜色对比度
- [ ] 引用了所有必要的 interop DLL（UI、UIModule、InputLegacy、TextRendering）
- [ ] 事件回调使用 IL2CPP 委托转换：`(UnityAction<bool>)delegate(bool v) { ... }`
- [ ] Dropdown 模板：Content 上**不加** VLG/ContentSizeFitter（Dropdown.Show() 自行管理）
- [ ] Dropdown 模板：Item 的 `anchorMin.x = 0, anchorMax.x = 1`（全宽，不用默认值）
- [ ] Dropdown 模板：Item 的 `sizeDelta.y` = 期望的 item 高度
- [ ] Dropdown 模板：Viewport 的 `pivot = (0, 1)`
- [ ] Dropdown 模板：ItemLabel 设置 `alignment = TextAnchor.MiddleLeft` + 合理的 offset
- [ ] 大枚举（KeyCode 等）不使用 Dropdown，改用 InputField 或直接跳过
- [ ] 需要自定义图标时，使用 Texture2D 像素绘制 + Sprite.Create()，不嵌入外部资源
- [ ] I18n key 使用空格分词的英文（如 `"Catchable Fish"`），因为英文模式下 key 直接作为显示文本
- [ ] 包含 I18n 文本的 UI 实现了语言即时切换（检测 `IsChinese()` 变化 → 重建或刷新）

---

## 参考资料

纯代码构建 uGUI 时，最可靠的参考是 Unity 的 uGUI 源码。官方文档只描述编辑器用法，不提供 RectTransform 具体数值和内部定位逻辑。

### Unity uGUI 源码（最重要）

- **DefaultControls.cs** — 每种内置 UI 组件的完整纯代码构建方式（含所有 RectTransform 数值）
  - https://github.com/Unity-Technologies/uGUI/blob/2019.1/UnityEngine.UI/UI/Core/DefaultControls.cs
  - 实现 Button、Toggle、Slider、Scrollbar、InputField、**Dropdown** 等的 `Create*()` 静态方法
  - **开发新 UI 组件时首先参考此文件**
- **Dropdown.cs** — Dropdown 组件的内部逻辑（Show/Hide/SetupTemplate/item 定位）
  - https://github.com/Unity-Technologies/uGUI/blob/2019.1/UnityEngine.UI/UI/Core/Dropdown.cs
  - 理解 `Show()` 如何克隆 item、手动计算 anchoredPosition、设置 Content 高度
- **Slider.cs** / **ScrollRect.cs** — 了解各组件对 RectTransform 的预期
  - https://github.com/Unity-Technologies/uGUI/tree/2019.1/UnityEngine.UI/UI/Core

### Unity 官方文档

- uGUI 手册: https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/
- 纯代码创建 UI: https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/HOWTO-UICreateFromScripting.html
- ContentSizeFitter: https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/HOWTO-UIFitContentSize.html

### 注意事项

- uGUI 源码的分支/tag 对应 Unity 版本，但核心组件（Dropdown、Slider 等）多年未大改，2019.1 分支的代码适用于 Unity 6
- IL2CPP interop 下组件行为与源码一致，区别仅在委托转换（`UnityAction<T>`）和类型注册
- 遇到布局问题时，先在 DefaultControls.cs 中找到对应组件的 `Create*()` 方法，对比每个 RectTransform 设置
