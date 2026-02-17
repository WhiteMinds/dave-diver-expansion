# 游戏内部结构探索记录

Dave the Diver (v1.0.5.1784) 的 IL2CPP interop 代码结构探索笔记。

---

## 1. 反编译方法与局限

### 单类反编译 vs 整体反编译

| 方法 | 命令 | 优点 | 缺点 |
|------|------|------|------|
| 单类 | `ilspycmd -t ClassName dll` | 快速查看单个类 | 无法搜索跨类引用，效率低 |
| 整体 | `ilspycmd -p -o decompiled/ dll` | 生成 6700+ .cs 文件，支持全文搜索 | 部分类反编译失败（exit code 70） |

**结论**：开发时应优先使用整体反编译到 `decompiled/` 目录，配合 Grep 全文搜索。对于反编译失败的类，再用 `ilspycmd -t` 单独查看。

### 反编译失败的类型

以下类型无法通过 `ilspycmd -p` 或 `ilspycmd -t` 完整反编译：
- `DR.Save.SaveUserOptions` — 可用 `ilspycmd -t` 获取部分信息（NativeMethodInfoPtr、NativeFieldInfoPtr），但 `-p` 不会生成文件
- `SaveSystemUserOptionManager` — `ilspycmd -l type` 找不到，`ilspycmd -t` 报 "Could not find type definition"，但 `grep` 可在 DLL 二进制中找到字符串
- `SaveSystem` — 类似问题

**原因**：IL2CPP interop DLL 的类型系统与标准 .NET 程序集不同，某些类型在 interop 生成过程中可能不完整。

---

## 2. 游戏语言系统

### 类型层次

```
DR.Save.SaveDataBase (抽象基类)
├── SaveData           — 游戏存档数据（29000+ 行，巨大）
├── SavePhotoData      — 照片数据
└── SaveUserOptions    — 用户设置（语言、音量、分辨率、按键绑定等）

DR.Save.Languages (枚举)
├── Unknown = 0
├── Chinese = 6
├── English = 10
├── Japanese = 22
├── Korean = 23
├── ChineseTraditional = 41
└── ... (更多语言)
```

### SaveUserOptions 关键成员

通过 `ilspycmd -t DR.Save.SaveUserOptions` 获取的部分信息：

```
属性：
- CurrentLanguage : Languages (get/set)
- MusicVolume : float
- SoundsVolume : float
- VibratePower : float
- AutoButton : bool
- UseDedicatedMouseCursor : bool
- ResolutionWidth : int
- ResolutionHeight : int
- WindowModeType : WindowModeType
- VSync : bool

方法：
- CheckLanguage(SystemLanguage) : Languages  — 实例方法，转换 Unity 语言枚举
- GetKeyString() : string
- Clear() : void
```

### 访问链问题

`SaveUserOptions` 不是单例，不是 MonoBehaviour。实例由 `SaveSystemUserOptionManager` 持有，但：
- `SaveSystemUserOptionManager` 不在 `ilspycmd -l type` 列表中（遍历全部 177 个 interop DLL 均无结果）
- PowerShell 反射无法加载 interop DLL（缺少 IL2CPP 运行时依赖）
- DLL 二进制中存在 "SaveSystemUserOptionManager" 字符串，但 decompiler 找不到类型定义

### 游戏中的语言使用模式

通过搜索反编译代码发现：
- `FontManager` 使用 `SystemLanguage`（Unity 枚举）选择字体：`GetFont(SystemLanguage)`, `GetFontAsset(SystemLanguage)`
- `DataManager.GetText(ref string textID)` 是游戏本地化的核心，返回当前语言的文本
- `SystemLanguageExtension` 类提供 `chinaEUFilterList` 和 `EUFilterList` 用于区域过滤
- 游戏 UI 组件（`AutoButtonCell`、`DedicatedMouseCursorCell`）通过 `SaveSystemUserOptionManager option` 字段访问设置

### Mod 获取语言的推荐方式

**方案一：Harmony 补丁（推荐）**
```csharp
[HarmonyPatch(typeof(DR.Save.SaveUserOptions), "get_CurrentLanguage")]
class LanguageCachePatch
{
    static void Postfix(DR.Save.Languages __result)
    {
        // 游戏启动时，设置系统/字体系统/UI 都会读取此属性
        // Postfix 自动缓存最新值
        CachedLanguage = __result;
    }
}
```

**方案二：Application.systemLanguage（备选）**
```csharp
var lang = Application.systemLanguage;
bool isChinese = lang == SystemLanguage.Chinese
    || lang == SystemLanguage.ChineseSimplified
    || lang == SystemLanguage.ChineseTraditional;
```

备选方案检测的是系统语言而非游戏语言，但大多数情况下二者一致。

---

## 3. 单例模式

游戏使用两种单例基类：

| 基类 | 特点 | 示例 |
|------|------|------|
| `Singleton<T>` : MonoBehaviour | 场景中的 GameObject 组件 | `DataManager`, `InGameManager` |
| `SingletonNoMono<T>` : Il2CppSystem.Object | 纯数据管理器，不绑定 GameObject | `StatusManager`, `DayManager`, `CalendarManager` |

两者都通过 `XXX.Instance` 静态属性访问。

### 已知的 SingletonNoMono 子类

```
AniSpeedManager, BlackBoxManager, CalendarManager, CommonDefine,
CostumeManager, DayManager, DRGameMode, GlobalSignalManager,
IngameSaveDataManager, IngredientsStorage, LootBox,
MVInventoryItemStorage, RendererExtensionManager, SceneContext,
SNSInfoManager, SpecialCustomerDataManager, SpriteCollection,
StatusManager, SubEquipmentManager, SushiBarAnalyticsManager,
SushiBarDispatchListManager, SushiBarKitchen, SushiBarMenuManager,
TimeManager, TutorialManager, WeatherManager
```

---

## 4. 搜索技巧

### 高效搜索模式

```bash
# 搜索谁引用了某个类型
grep -r "SaveUserOptions" decompiled/ --include="*.cs"

# 搜索某个方法的实现
grep -r "CurrentLanguage" decompiled/ --include="*.cs"

# 搜索继承关系
grep -r "class.*: Singleton<" decompiled/ --include="*.cs"
grep -r "class.*: SingletonNoMono<" decompiled/ --include="*.cs"

# 搜索特定模式的 NativeMethodInfo（ilspycmd -t 输出中）
ilspycmd -t ClassName dll | grep "NativeMethodInfoPtr_get_"  # 列出所有属性 getter
ilspycmd -t ClassName dll | grep "NativeMethodInfoPtr_set_"  # 列出所有属性 setter
ilspycmd -t ClassName dll | grep "Public.*Static"            # 列出所有静态方法
```

### 当搜不到类型时的排查步骤

1. `ilspycmd -l type dll | grep TypeName` — 检查类型是否在类型列表中
2. `grep -rl "TypeName" interop/*.dll` — 在 DLL 二进制中搜索字符串
3. `grep -r "TypeName" decompiled/` — 在已反编译代码中搜索引用
4. 如果 DLL 中有但 `-l type` 找不到 → 可能是嵌套类型或反编译器无法处理的类型
5. 尝试带完整命名空间：`ilspycmd -t "Namespace.TypeName" dll`
