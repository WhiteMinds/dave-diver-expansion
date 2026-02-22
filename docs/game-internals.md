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

---

## 5. 逆向工程补充工具

除 ilspycmd + BepInEx interop DLL 外，以下工具在特定场景下有用：

| 工具 | 用途 | 何时使用 |
|------|------|----------|
| **UnityExplorer** (IL2CPP 版) | 运行时场景浏览器，可实时查看 Hierarchy、Inspector、调用方法 | 探索 UI 结构、定位 Canvas/组件、调试运行时状态 |
| **Cpp2IL** | 将 IL2CPP 二进制还原为伪代码，比 interop DLL 能看到更多实现逻辑 | ilspycmd 只能看签名时，需要理解方法的实际实现。CLI: `Cpp2IL-Win.exe --game-path="<GamePath>"` ([GitHub](https://github.com/SamboyCoding/Cpp2IL/releases)) |
| **Cheat Engine** | 动态内存搜索，"什么改写了这个地址" 可反向定位函数 | 定位数值的存储位置和修改函数 |
| **Il2CppDumper** | 独立的元数据提取工具，解析 GameAssembly.dll + global-metadata.dat | BepInEx 已自动生成 interop DLL，通常不需要；但可生成 IDA/Ghidra 脚本做深度分析。CLI: `Il2CppDumper.exe <GameAssembly.dll> <global-metadata.dat> <output/>` ([GitHub](https://github.com/Perfare/Il2CppDumper)) |

---

## 6. Job System / Burst Compiler 与 Hook 限制

游戏的鱼群 AI（蜂群行为、碰撞检测）等高性能逻辑可能使用了 Unity 的 **C# Job System** + **Burst Compiler**。

### 为什么 Harmony 无法 hook Burst 编译的代码

- Burst 将 Job 代码编译为高度优化的原生机器码（LLVM），绕过 IL2CPP 的标准方法调用约定
- 这些函数不在 GameAssembly.dll 的常规导出表中
- 被内联或以紧凑汇编形式存在，Harmony 的 method detour 机制无法定位

### 迂回策略

不要尝试 hook Job 的 `Execute()` 方法。应该 hook：

1. **Job 调度阶段 (Schedule)** — 主线程中初始化 Job 结构体的代码，修改传入 Job 的参数（如速度乘数）
2. **Job 完成后的数据同步 (Complete)** — LateUpdate 中将 Job 结果应用回 Transform 的系统，在结果应用前修正
3. **Job 外围的管理类** — 如 FishManager、SpawnSystem 等 MonoBehaviour，这些仍然是普通 IL2CPP 方法

### 识别方法

```bash
# 在反编译代码中搜索 Job 相关模式
grep -r "IJob\|IJobParallelFor\|JobHandle\|Schedule(" decompiled/ --include="*.cs"
grep -r "NativeArray\|NativeList" decompiled/ --include="*.cs"
```

---

## 7. 官方立场与分发注意事项

- **MINTROCKET 官方反对 mod** — Discord 规则明确禁止讨论 Hack/Cheats/Mods 和解包游戏数据
- **无 Steam 创意工坊支持**
- **分发渠道**：Nexus Mods（主要）、GitHub（源码与技术交流）
- **2026 DLC "Into the Jungle"** — 大更新会重编译 GameAssembly.dll，所有函数偏移变化，interop DLL 需重新生成
  - 我们的 Harmony patch 基于方法签名（`typeof(ClassName)` + `nameof(Method)`）而非硬编码地址，抗更新能力较强
  - 但类/方法重命名或签名变化仍会导致 patch 失效，需重新适配

---

## 8. 场景对象层级与实体结构

### 潜水场景层级

潜水场景中，鱼、宝箱等实体都位于 `RuntimeObjects` 容器下：

```
RuntimeObjects/
├── Boid_SA_2010002_ClownFish_3(Clone)/     ← 鱼群 (BoidGroup)
│   ├── SA_2010002_ClownFish/               ← 单条鱼 (FishInteractionBody)
│   ├── SA_2010002_ClownFish (1)/
│   └── SA_2010002_ClownFish (2)/
├── Thresher_Shark/                          ← 单体鱼分配器
│   └── SA_2010132_Thresher_Shark01(Clone)/  ← 攻击性鱼
├── Stellate_Puffer/                         ← 单体鱼分配器
│   └── SA_2010027_Stellate_Puffer(Clone)/
├── Allocator__Moray_Eel02_R/               ← 固定位置鱼分配器
│   └── SA_2010054_Moray_Eel02/
├── SeahorseSpawner_Seahorse_15/            ← 海马生成器
│   └── SA_2012015_Racing_Seahorse_15(Clone)/
├── Chest_O2(Clone)                          ← 氧气宝箱
├── Chest_Item(Clone)                        ← 道具宝箱
├── Chest_Weapon(Clone)                      ← 武器宝箱
├── Chest_Rock(Clone)                        ← 岩石宝箱
├── Chest_IngredientPot_A(Clone)            ← 食材罐
├── Loot_StarFish004(Clone)                 ← 掉落物品
└── ...
```

### 鱼的组件结构

**普通群体鱼**（Boid）：
```
SA_2010002_ClownFish:
  Transform, Rigidbody2D, Damageable, FishInteractionBody, BuffHandler,
  CapsuleCollider2D, SABaseFishSystem, FishSubSMBManager, FindTargetHelper,
  Seeker, SACustomMovement, ActivityAreaManager, BoidsSmallGroupAI,
  Moveable2D, AwayFromTarget, AIDatasManager, RotableByLook2D, ...
  └── Body: [Transform]
  └── Direction: [Transform]
```

**攻击性鱼**（鲨鱼、河豚、海鳗等）：
```
SA_2010132_Thresher_Shark01(Clone):
  Transform, Rigidbody2D, Damageable, FishInteractionBody, BuffHandler,
  SABaseFishSystem, Painable, SkillDescInitializer, FishSubSMBManager,
  FindTargetHelper, DefaultSprintable, AIDatasManager, Rotable, ...
  ★ 没有 AwayFromTarget 和 BoidsSmallGroupAI
  └── Body: [Transform, SpecialAttackerBodyController]
  └── Damageables: [Transform, DamagerAbility, DamagerByDamageables]
  └── Damagers: [Transform]
  └── TailSpearDamager: [Transform, BoxCollider2D, BodyDamager]
```

### 区分攻击性鱼的方法

> **⚠️ 已过时**：仅用 `AwayFromTarget == null` 判断攻击性是不准确的（鱿鱼有 AwayFromTarget 但实为攻击性）。
> 更准确的方案请参见 [docs/game-classes.md](game-classes.md) § 鱼攻击性检测（FishAggressionType）。

以下是早期基于组件的观察，仍有参考价值：

| 鱼类型 | AwayFromTarget | 额外攻击组件 |
|--------|----------------|-------------|
| Boid 群鱼（小丑鱼等） | ✅ 有 | 无 |
| 鲨鱼 (Thresher_Shark) | ❌ 无 | Painable, DefaultSprintable, BodyDamager |
| 河豚 (Stellate_Puffer) | ❌ 无 | Damager, BodyDamager |
| 海鳗 (Moray_Eel) | ❌ 无 | Attackable |

> 宝箱/物品的 prefab 分类详见 [docs/game-classes.md](game-classes.md) § 宝箱 prefab 名称分类 / PickupInstanceItem 物品系统。

### 关卡边界

#### ⚠️ `GetBoundary()` 的陷阱

`InGameManager.GetBoundary()` **并非**返回整个关卡的完整边界。它实际上返回的是 **当前相机子区域** 的边界（即玩家所在的那一小块区域），而非整个关卡的合并边界。

在标准潜水场景中差异不大，但在 **冰河区域 (Glacier)** 等多子区域关卡中，`GetBoundary()` 返回的范围远小于实际关卡范围（例如仅 34 单位宽，而完整关卡约 129 单位宽）。

#### 正确方案：合并 CameraSubBoundsCollection

```csharp
// InGameManager.SubBoundsCollection → CameraSubBoundsCollection
// CameraSubBoundsCollection.m_BoundsList → List<CameraSubBounds>
// CameraSubBounds.Bounds → Unity Bounds (center, size, min, max)

var igm = Singleton<InGameManager>._instance;
Vector2 boundsMin, boundsMax;

// 先用 GetBoundary() 作为初始值
var b = igm.GetBoundary();
boundsMin = new Vector2(b.min.x, b.min.y);
boundsMax = new Vector2(b.max.x, b.max.y);

// 合并所有子区域，获取完整关卡范围
var subBoundsCol = igm.SubBoundsCollection;
if (subBoundsCol != null)
{
    var boundsList = subBoundsCol.m_BoundsList;
    for (int i = 0; i < boundsList.Count; i++)
    {
        var sb = boundsList[i];
        var bounds = sb.Bounds;
        boundsMin = Vector2.Min(boundsMin, new Vector2(bounds.min.x, bounds.min.y));
        boundsMax = Vector2.Max(boundsMax, new Vector2(bounds.max.x, bounds.max.y));
    }
}
```

#### 各方案对比

| 方案 | 返回值 | 可靠性 |
|------|--------|--------|
| `InGameManager.GetBoundary()` | 当前相机子区域边界 | ⚠️ 多子区域关卡不完整 |
| `InGameManager.SubBoundsCollection` → 合并 | 完整关卡边界 | ✅ 推荐 |
| `InGameManager.CurrentCameraBounds` | 当前相机区域 | ⚠️ 同上 |
| `OrthographicCameraManager.m_BottomLeftPivot/m_TopRightPivot` | 相机框 | ❌ `CalculateCamerabox()` 前为 (0,0) |

#### 关键类型

| 类 | 说明 |
|---|---|
| `DR.CameraSubBoundsCollection` | 管理所有子区域，字段 `m_BoundsList: List<CameraSubBounds>` |
| `DR.CameraSubBounds` | 单个子区域，属性 `Bounds` 返回 Unity `Bounds` |

#### 实际数据示例

**冰河区域 (Glacier)**：
- `GetBoundary()` 返回：仅当前子区域（约 34 单位宽）
- 合并 SubBounds 后：`(-64.48, -242.05)` 到 `(64.48, 29.71)`，宽 129，高 272，aspect ≈ 0.47

**注意**：不要尝试用逃生点坐标动态扩展边界 — 逃生点可能在距关卡很远的位置（如 1000+ 单位外），会导致地图比例严重失真。

### Harmony + IL2CPP Virtual 方法陷阱

**⛔ 不要 patch 继承链中的 virtual 方法（如 `OnDie`），除非该类是最终类。**

原因：Harmony 在 IL2CPP 上 patch virtual 方法时，会替换原生方法指针。当基类或兄弟子类调用该 virtual 方法时，IL2CPP trampoline 尝试将 `IntPtr` 参数转换为 patch 目标类型会产生 `NullReferenceException`——这发生在 Postfix/Prefix 代码之前，**null guard 无效**。

```
// 错误示例：InteractionGimmick_Mining.OnDie 是 virtual，
// InteractionGimmick 的其他子类也会调用 OnDie，触发 trampoline 崩溃
[HarmonyPatch(typeof(InteractionGimmick_Mining), nameof(InteractionGimmick_Mining.OnDie))]  // ⛔ 会崩溃
```

**安全替代方案**：
- 用非 virtual 的生命周期方法（`OnEnable`, `Awake`）做注册
- 用 `Purge()` 周期性清理代替精确的注销补丁
- 用业务逻辑过滤（如 `IsDead()`, `isClear`）代替注销

**已知安全的 patch 目标**：`OnEnable`（MonoBehaviour 原生回调，非 virtual dispatch）、`Awake`（同上）、`SuccessInteract`（具体业务方法）。

### 游戏对象激活距离（Object Streaming）

游戏对远处的场景对象执行 `SetActive(false)`，只在玩家附近时激活。这意味着：
- `activeInHierarchy` 不能用于过滤 **静态** 对象（矿物、固定物品等）——远处的有效对象也是 inactive
- inactive 对象的 `transform.position` 仍然有效（位置不变）
- 对于移动实体（鱼）仍应检查 `activeInHierarchy`，因为 inactive 鱼的位置可能过时
- `OnEnable` 在对象首次进入激活范围时触发，可用于注册；但对象再次变为 inactive 时不会从注册表移除，需靠 `Purge()` 或业务过滤（`IsDead()`）处理

### IL2CPP 命名空间陷阱

运行时组件名不一定与 interop DLL 中的完全限定名匹配。例如：
- 运行时报告 `AwayFromTarget` → 实际是 `DR.AI.AwayFromTarget`
- 搜索时用 `ilspycmd -l type dll | grep -i "ClassName"` 可找到完整命名空间
- `decompiled/` 目录中可能找不到某些类（反编译失败），但 interop DLL 中仍存在

---

## 9. UI 弹窗与暂停菜单系统

### MainCanvasManager (核心 UI 管理器)

`MainCanvasManager` 继承 `Singleton<MainCanvasManager>`，是游戏内所有 UI 面板的中央管理器。

```csharp
// 安全访问（避免 auto-create）
var mcm = Singleton<MainCanvasManager>._instance;
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `pausePopupPanel` | `PausePopupMenuPanel` | ESC 暂停菜单面板实例 |
| `quickPausePopup` | `QuickPausePopup` | 快速暂停弹窗实例 |
| `IsQuickPause` | `bool` | 是否有快速暂停弹窗正在显示 |

### 检测暂停菜单是否打开

潜水时按 ESC 会打开 `PausePopupMenuPanel`（继承 `BaseUI : MonoBehaviour`），包含「继续」「设置」「返回大厅」等按钮。

```csharp
bool isPaused = false;
try
{
    var mcm = Singleton<MainCanvasManager>._instance;
    if (mcm != null)
    {
        var pausePanel = mcm.pausePopupPanel;
        if (pausePanel != null && pausePanel.gameObject.activeSelf)
            isPaused = true;
    }
}
catch { }
```

**Mod 应用场景**：
- 暂停菜单打开时隐藏自定义 HUD（如 DiveMap 小地图）
- 暂停菜单打开时禁用自定义热键（如 M 键切换大地图）
- 避免在暂停状态下执行游戏逻辑

### PausePopupMenuPanel 关键方法

| 方法 | 说明 |
|------|------|
| `OnPopup()` | 面板显示时调用 |
| `OnClickContinue()` | 点击「继续」按钮 |
| `OnClickSettings()` | 点击「设置」按钮 |
| `ShowReturnLobbyPanel()` | 显示「返回大厅」确认 |
| `CancelReturnLobby()` | 取消返回大厅 |

---

## 10. InputSystem 与 Harmony 限制

### ⛔ Harmony 无法拦截 InputSystem 回调方法

游戏使用 Unity 新 InputSystem（`Unity.InputSystem.dll`）+ 自定义封装（`InputSystemWrapper.dll`）。玩家战斗输入通过 InputSystem action 回调触发：

```
InputAction.performed → PlayerCharacter.OnFire_Performed()
InputAction.performed → PlayerCharacter.OnMelee_Performed()
InputAction.performed → PlayerCharacter.OnGrab_Performed()
```

**这些回调方法的 CallerCount=0**（仅被 InputSystem 的 native delegate 调用），Harmony 的 Prefix/Postfix 补丁**完全不生效**——既不报错也不执行，静默失败。

同样，`CanMeleeAttack` 属性 getter（CallerCount=1）在实测中也未被 Harmony 拦截，可能是 IL2CPP AOT 内联导致。

### ⛔ `SetInputLock` / `ActionLock` 不可用于精细控制

- `PlayerCharacter.SetInputLock(bool)` 会禁用**整个** Handler 组件（含移动），不是按 action 粒度
- `ActionLock.availables` 数组在运行时为空（length=0），启用 ActionLock 后**所有操作**被锁定
- `ActionLock.SetEnable(bool)` 本质是 `Behaviour.enabled = enable`，OnEnable 时调用 `inputAsset.Lock(this)`

### ✅ 正确方案：直接禁用 InputAction

通过 `player.inputAsset`（`DRInputAsset`）获取 Unity `InputActionAsset`，找到具体 `InputAction` 并调用 `Disable()`/`Enable()`：

```csharp
// DRInputAsset → Entry (offset 0x18) → DRInputAssetEntry.inputActionAsset
var drAsset = player.inputAsset;
var entryPtr = Marshal.ReadIntPtr(drAsset.Pointer, 0x18);
var entry = new DRInput.DRInputAsset.DRInputAssetEntry(entryPtr);
var actionAsset = entry.inputActionAsset; // Unity InputActionAsset

var inGameMap = actionAsset.FindActionMap("InGame", false);
var fireAction = inGameMap.FindAction("Fire", false);
fireAction.Disable();  // 阻止 InputSystem 触发回调
fireAction.Enable();   // 恢复
```

**引用要求**：`Directory.Build.props` 需添加 `Unity.InputSystem.dll` 和 `InputSystemWrapper.dll` 引用。

### DRInputAsset 结构

```
DRInputAsset : InputAsset<SchemeName, MapName>
├── ActionMap_Dave    — 地面操作 (Move, Interaction, Menu, ...)
├── ActionMap_InGame  — 潜水操作 (Move, Aim, Fire, Melee, Grab, Item, ...)
├── ActionMap_UI      — UI 操作 (Ok, Cancel, Navigation, ...)
├── ActionMap_QTE     — QTE 操作
└── ... (MiniGame, BattleQTE, Concert, etc.)
```

**ActionMap_InGame.ActionName 枚举**：
`Move, Interaction, InteractionMouse, Aim, AimAxis, AimAxisMouse, Fire, Melee, Grab, QTE, Rotate, RotateMouse, Item, SwitchItem, SwitchWeapon, ActiveItem, Menu, AimAxisRight, ShortDash, BreakItem, LongDash, FireItem, MVMinimapInGame, QTE_Harpoon, MVCallTaxi, MVReturnTaxi, PDA, MoveP2, InteractionP2, Exploration`

---

## 11. 参考 Mod 项目

| 项目 | 作者 | 功能 | 参考价值 |
|------|------|------|----------|
| [dave-the-diver-mods](https://github.com/devopsdinosaur/dave-the-diver-mods) | devopsdinosaur | 无限氧气、无敌、自动拾取、加速 | Hook 点选择、BepInEx 6 项目结构、ConfigFile 用法 |
| [DaveDiverMap](https://github.com/qe201020335/DaveDiverMap) | qe201020335 | 屏幕地图覆盖 | 运行时 UI 构建、坐标系统读取、Canvas 动态创建 |
