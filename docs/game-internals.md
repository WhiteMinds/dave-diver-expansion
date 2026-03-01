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

### Cpp2IL IsilDump（查看实际 native 实现）

当 interop 反编译只能看到 `il2cpp_runtime_invoke` 调用、无法了解方法的实际逻辑时，可使用 Cpp2IL 生成的 ISIL dump 查看伪汇编。

**路径**: `.tmp/cpp2il_out/IsilDump/Assembly-CSharp/ClassName.txt`

**用途示例**:
- 确认方法是否是空壳（如 `CatchableByItem.isPlayerCatchableThis` → `mov al,1; ret`，直接返回 true）
- 确认方法实际检查了哪些字段（如 `FishInteractionBody.CheckAvailableInteraction` 不检查 `isInteractable`，只检查 LootBox 满载和 drone 可用性）
- 理解 `ConditionFishInteraction.Update` 如何调用 `ContentsUnlockManager.IsUnlock` 控制交互启用

**注意**：IsilDump 是伪汇编/ISIL 中间表示，需要结合 interop 反编译的字段偏移量来理解。ISIL 中的 `Call` 指令会显示实际调用的方法名（如 `LootBox.CheckOverloadedState`、`ContentsUnlockManager.IsUnlock`），比纯汇编更容易阅读。

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

### 访问链

`SaveUserOptions` 不是单例，不是 MonoBehaviour。通过 `SaveSystem` 单例间接访问：

```
Singleton<SaveSystem>._instance.UserOptionManager.CurrentLanguage
```

- `SaveSystem` : `Singleton<SaveSystem>` — 存档系统单例
- `SaveSystem.UserOptionManager` : `SaveSystemUserOptionManager` — CallerCount=159，游戏各处广泛使用
- `SaveSystemUserOptionManager.CurrentLanguage` — CallerCount=19，**带 fallback 逻辑**（Data 为 null 时自动用 `Application.systemLanguage`）

注意：`SaveSystemUserOptionManager` 在 `ilspycmd -l type` 中找不到，但 `ilspycmd -t DR.Save.SaveSystemUserOptionManager` 可反编译。IsilDump 路径：`.tmp/cpp2il_out/IsilDump/Assembly-CSharp/DR/Save/SaveSystemUserOptionManager.txt`

### 游戏中的语言使用模式

通过搜索反编译代码发现：
- `FontManager` 使用 `SystemLanguage`（Unity 枚举）选择字体：`GetFont(SystemLanguage)`, `GetFontAsset(SystemLanguage)`
- `DataManager.GetText(ref string textID)` 是游戏本地化的核心，返回当前语言的文本
- `SystemLanguageExtension` 类提供 `chinaEUFilterList` 和 `EUFilterList` 用于区域过滤
- 游戏 UI 组件（`AutoButtonCell`、`DedicatedMouseCursorCell`）通过 `SaveSystemUserOptionManager option` 字段访问设置

### Mod 获取语言的推荐方式

**✅ 直接调用 SaveSystem API（推荐）**
```csharp
var saveSystem = Singleton<DR.Save.SaveSystem>._instance;
if (saveSystem != null)
{
    var optMgr = saveSystem.UserOptionManager;
    if (optMgr != null)
    {
        var lang = optMgr.CurrentLanguage;
        // 内部逻辑：Data != null → 读 Data.CurrentLanguage，否则 → Application.systemLanguage + ToLanguage()
    }
}
// SaveSystem 未就绪时 fallback: Application.systemLanguage
```

**⛔ 不要用 Harmony patch `SaveUserOptions.get_CurrentLanguage`**

`SaveUserOptions.CurrentLanguage` 是自动属性 getter（`mov eax,[rcx+4Ch]; ret`）。IL2CPP 下，Harmony Postfix 会被大量无关的 save-data 字段读取误触发，`__result` 返回垃圾值（实测：3, 4, 6, 75, 200, 999, 99999 等）。`Chinese=6` 枚举值极小，垃圾读取中非常容易碰到 → 英文游戏被误判为中文。

这是一个通用的 **IL2CPP 属性 getter Harmony patch 陷阱**：序列化数据类的自动属性 getter 可能被 IL2CPP 内部的字段偏移读取复用，不仅在"读取该属性"时触发。详见 [docs/game-classes.md](game-classes.md) § 游戏语言系统。

**备选 fallback：Application.systemLanguage**
```csharp
var lang = Application.systemLanguage;
bool isChinese = lang == SystemLanguage.Chinese
    || lang == SystemLanguage.ChineseSimplified
    || lang == SystemLanguage.ChineseTraditional;
```

备选方案检测的是系统语言而非游戏语言，但大多数情况下二者一致。适合 SaveSystem 尚未初始化时的早期 fallback。

---

## 3. 单例模式

游戏使用两种单例基类：

| 基类 | 特点 | 示例 |
|------|------|------|
| `Singleton<T>` : MonoBehaviour | 场景中的 GameObject 组件 | `DataManager`, `InGameManager` |
| `SingletonNoMono<T>` : Il2CppSystem.Object | 纯数据管理器，不绑定 GameObject | `StatusManager`, `DayManager`, `CalendarManager` |

两者都通过 `XXX.Instance` 静态属性访问。

**⚠️ 安全访问差异**：
- `Singleton<T>._instance` — 安全 null check（不会自动创建）
- `SingletonNoMono<T>.s_Instance` — 安全 null check（**没有** `_instance` 字段）
- `SingletonNoMono<T>.hasInstance` — bool 检查
- `Singleton<T>.Instance` / `SingletonNoMono<T>.Instance` — **⛔ 自动创建实例**，不要用于 null check

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
| **Cpp2IL** | 将 IL2CPP 二进制还原为 ISIL 伪代码（汇编级），能看到方法的实际实现逻辑 | ilspycmd 反编译失败的类型、需要理解方法实际逻辑时。CLI: `Cpp2IL-Win.exe --game-path="<GamePath>" --output-root=.tmp/cpp2il_out --use-processor=isil` ([GitHub](https://github.com/SamboyCoding/Cpp2IL/releases)) |
| **Cheat Engine** | 动态内存搜索，"什么改写了这个地址" 可反向定位函数 | 定位数值的存储位置和修改函数 |
| **Il2CppDumper** | 独立的元数据提取工具，解析 GameAssembly.dll + global-metadata.dat | BepInEx 已自动生成 interop DLL，通常不需要；但可生成 IDA/Ghidra 脚本做深度分析。CLI: `Il2CppDumper.exe <GameAssembly.dll> <global-metadata.dat> <output/>` ([GitHub](https://github.com/Perfare/Il2CppDumper)) |

### Cpp2IL 产物详情

- 产物路径：`.tmp/cpp2il_out/IsilDump/`，每个程序集一个子目录，每个类型一个 `.txt` 文件
- Assembly-CSharp 下 6986 个文件，总计 ~450 万行
- 文件内容：方法签名 + 汇编反汇编 + ISIL 伪代码（低级但有实现）
- **与 ilspycmd 互补**：ilspycmd 输出高级 C# 签名（易读但部分类型失败），Cpp2IL 输出 ISIL 伪代码（汇编级但覆盖更全）
- 搜索示例：`grep -r "MethodName" .tmp/cpp2il_out/IsilDump/Assembly-CSharp/`

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

### Harmony + IL2CPP 的两条独立约束

在 IL2CPP 游戏中 Harmony patch 有**两条独立的约束**，必须同时满足：

| 约束 | 条件 | 后果 | 诊断方式 |
|------|------|------|----------|
| **CallerCount 规则** | CallerCount > 0 | patch 静默失效（不崩溃，不报错，Prefix/Postfix 永远不执行） | 日志无输出 |
| **CallerCount 极高** | CallerCount > 1000 | 游戏启动即崩溃（控制台出现后消失） | 启动崩溃 |
| **Virtual 方法规则** | virtual/override 且有兄弟子类 | 运行时崩溃（NRE 在 trampoline 中，null guard 无效） | 运行时 NRE |

#### CallerCount 内联（IL2CPP AOT 编译器优化）

**⛔ CallerCount > 0 的方法会被 IL2CPP AOT 编译器内联**，Harmony patch 静默失效。

这不限于极高 CallerCount。实测中 CallerCount=1、2、3、12 的方法**全部被内联**，Harmony Prefix/Postfix 均不执行，无报错。只有 CallerCount=0 的方法（Unity 引擎通过 native dispatch 调用的回调，如 `Update`、`FixedUpdate`、`Awake`）不会被内联。

```
// ⛔ PlayerCharacter.DetermineMoveSpeed — CallerCount(1)，被内联，patch 静默失效
// ⛔ PlayerCharacter.UpdateMoveProperty — CallerCount(3)，被内联，patch 静默失效
// ⛔ SubHelperSpecData.get_BoosterSpeed — CallerCount(2)，被内联
// ⛔ SubHelperSpecData.get_BatteryDuration — CallerCount(12)，被内联

// ✅ PlayerCharacter.Update — CallerCount(0)，由 Unity 引擎调用，不会被内联
// ✅ PlayerCharacter.FixedUpdate — CallerCount(0)，同上
```

**诊断方法**：在 `decompiled/ClassName.cs` 中检查 `[CallerCount(N)]`。**只 patch CallerCount=0 的方法**。

#### CallerCount 极高的 Unity 消息方法

CallerCount 极高（如 29514）的特殊情况：method token 被数千个类共享。Harmony patch 会影响所有类，导致启动崩溃。

```
// ⛔ CrabTrapZone.Awake — CallerCount(29514)，patch 后游戏启动即崩溃
// ✅ CrabTrapZone.Start — CallerCount(0)，安全
```

#### Virtual 方法（独立于 CallerCount）

CallerCount=0 的方法如果是 virtual override 且有兄弟子类，仍然**不能 patch**。典型案例：

```
// ⛔ BoosterHandler.StartUseAlterSubHelper — CallerCount(0)，但是 SubHelperHandler 的
//    virtual override，其他子类（DroneHandler 等）共享 vtable → 运行时崩溃

// ✅ PlayerCharacter.Update — CallerCount(0)，虽然技术上是 MonoBehaviour 的 override，
//    但由 Unity 引擎 native message dispatch 调用（不走 C# vtable），且无兄弟子类 → 安全
```

**安全的 patch 目标**：CallerCount=0 且 (非 virtual/override 或 无兄弟子类的 Unity 回调)。

### ⛔ Unity 帧执行顺序与 Harmony Prefix 时序

Unity 每帧的执行顺序为：

```
FixedUpdate → 物理模拟 → OnTriggerEnter/Exit/Stay → Update
```

**Harmony Prefix on Update 在物理回调之后执行**。如果游戏在 `OnTriggerEnter` 中执行即时判定（如海马赛 `OnObstacle` 检查当前状态并立即 Fail），则在 Update Prefix 中设置的值对**当前帧的物理回调无效**——只能影响**下一帧**的物理。

这意味着需要提前（在物理触发前至少 1 帧）通过 lookahead 机制设好输入值，不能等到物理 trigger 触发后再反应。

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

### IL2CPP 类型信息丢失

IL2CPP interop 对象的 `GetType().Name` 可能返回**基类名**而非具体子类。例如 `BoxCollider.GetType().Name` 返回 `"Collider"` 而非 `"BoxCollider"`。调试 collider 类型时应使用 `collider.bounds` 判断形状，而非依赖类名

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

## 11. 存档系统（Save System）

### 存档加载管线

游戏通过 `DR.Save.SaveLoadManagerBase<T>.LoadData()` 加载存档，完整流程：

```
File.ReadAllText(path)                          // UTF-8 解码为 C# string
  ↓
Enum.ToString(SaveDataType)                     // → "GameData"（XOR 密钥）
  ↓
ObscuredString.EncryptDecrypt(fileContent, key)  // char-level XOR 解密
  ↓
SaveConvertSystem.Convert(json, version, ...)    // 版本迁移
  ↓
JsonConvert.DeserializeObject<SaveData>(json)    // Newtonsoft.Json 反序列化
```

### ObscuredString.EncryptDecrypt（CodeStage AntiCheat）

通过 IsilDump 逆向确认，核心实现等价于：

```csharp
// CodeStage.AntiCheat.ObscuredTypes.ObscuredString
static string EncryptDecrypt(string value, string key) {
    if (string.IsNullOrEmpty(value)) return value;
    if (string.IsNullOrEmpty(key)) key = defaultKey;  // 静态默认密钥

    char[] result = new char[value.Length];
    for (int i = 0; i < value.Length; i++) {
        result[i] = (char)(value[i] ^ key[i % key.Length]);
    }
    return new string(result);
}
```

**关键**：XOR 在 C# `char` (UTF-16 code unit) 级别操作，key cycling 是 per-char (`i % keyLen`)，**不是** per-byte。这与上游 DaveSaveEd (C++/Python) 的 byte-level XOR 实现有本质区别。

辅助方法：
- `GetBytes(string)` — `str.ToCharArray()` + `Buffer.BlockCopy` → 直接 UTF-16LE 内存拷贝，不是 UTF-8
- `GetString(byte[])` — `Buffer.BlockCopy` → `new char[len/2]` → `new string(chars)`
- 这两个方法仅用于 `InternalEncrypt`/`InternalDecrypt`（内部加密存储），不用于存档文件的加密

### 错误处理链

存档加载错误的传播路径：

```
SaveLoadManagerBase.LoadData()
  catch (Exception) → 记录错误信息到 failedToLoadReason
  ↓
SaveDataWithDate.HasFailedToLoad = true
  ↓
UIListSaveData 构造函数（5 个 JSON 解析器逐个尝试）
  ↓
SaveLoadPopupCell.SetErrorCell() → 显示 m_ErrorRoot UI（"存档文件错误"）
```

**没有 checksum/hash 校验** — 存档错误纯粹是 JSON 反序列化（Newtonsoft.Json）抛出的异常。

### SaveDebug 临时 Hook 点（调试用）

调试存档问题时可 Hook 的关键位置：

| 类 | 方法 | 用途 |
|---|------|------|
| `GameBase` | `LoadSavedData` | 存档加载入口 |
| `GameBase` | `_LoadSavedData_b__33_0` | 加载完成回调 |
| `SaveLoadPopup` | `Show` | UI 弹窗显示（⚠ 有重载，需指定参数类型避免 AmbiguousMatchException）|
| `SaveLoadPopup` | `Deserialize` | JSON 反序列化 |
| `SaveLoadPopup` | `Parse` | JObject.Parse |
| `SaveLoadPopupCell` | `SetData` / `SetErrorCell` | 存档槽位 UI 更新 |
| `Application.logMessageReceived` | Unity log listener | 捕获所有 `Debug.LogError`/`LogException` |

注意：`SaveLoadPopup.Show` 有多个重载，Harmony `[HarmonyPatch(typeof(SaveLoadPopup), "Show")]` 会触发 `AmbiguousMatchException`，需要用 `[HarmonyPatch]` + `MethodType.Normal` + 明确参数类型列表。

## 12. 运行时资源加载

### Sprite 按名称查找

游戏中很多图标（如 SubHelper 的 Booster 图标）存储在 ScriptableObject 或 prefab 中，不通过 Addressables/Resources.Load 暴露。可用 `Resources.FindObjectsOfTypeAll<Sprite>()` 搜索已加载的所有 Sprite（包括未激活的）：

```csharp
var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
foreach (var s in allSprites)
    if (s.name == "Booster_Thumbnail")
        return s;
```

**注意**：
- 返回结果包含所有已加载的资源，不仅限于当前场景
- 首次搜索后建议缓存到字典（`Dictionary<string, Sprite>`）
- 某些资源可能在特定场景才加载（如潜水场景的 SubHelper 图标在大厅可能已可用，取决于预加载）

### DelegateSupport.ConvertDelegate 限制

`Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<T>(managedDelegate)` 用于将 C# managed 委托转换为 IL2CPP 委托。**已知问题**：对某些 IL2CPP 委托类型（如 `UIDataText.OverrideTextFunc`），转换产生的委托对象非 null，但 IL2CPP native 代码调用时返回空值。workaround 见 [docs/game-classes.md](game-classes.md) § UIDataText 组件。

## 13. 标题画面系统（Title Screen）

### 场景流程

```
StartSceneController (Start 场景)
  → SceneLoader.CoLoadSceneAsync("DR_Logo")
LogoManager (Logo 场景, 继承 SceneLoaderManagedBehaviour)
  → AgeGradeEvent 协程 → 播放 Logo 动画
  → SceneLoader.GoToTitle()
    → SceneLoader.ChangeSceneAsync("DR_Title")
DR.Title.TitleManager (Title 场景, 继承 SceneLoaderManagedBehaviour)
  → Start() 协程（7 个 yield state）
  → 主菜单按钮: New Game / Continue / Load / Settings / Exit
```

### TitleManager 关键类

**命名空间**: `DR.Title`

**注意**: `ilspycmd -p` 整体反编译不会生成此类的 .cs 文件（DR.Title 目录为空），但 `ilspycmd -t DR.Title.TitleManager` 可以成功反编译，IsilDump 也有完整信息。

| 方法 | CallerCount | 作用 |
|------|-------------|------|
| `get_IsSceneInitialized` | — | 布尔属性，Start 协程 state 5 末尾设为 true |
| `Start()` | 0 | 协程入口，7 个 yield state 依次执行初始化 |
| `RefreshButtonList()` | 1 | 刷新菜单按钮列表（state 5 中调用） |
| `RefreshContinueGame()` | — | 刷新"继续"按钮状态（是否有存档） |
| `OnContinueGame()` | 0 | 继续游戏入口：创建 Action → CheckKeyboardDuplicate → OnContinueGame_Impl |
| `OnContinueGame_Impl()` | 0 | 完整继续流程：DLC 检查 → 版本检查 → ContinueGame |
| `ContinueGame()` | 2 | 最终执行：SaveSystem 存档操作 → GameBase.RestartGame → GoToLobbyEntry |
| `NewGame()` | 1 | 新游戏 |
| `OnLoadGame()` | 0 | 打开读档面板 |
| `GoToLobbyEntry()` | 1 | 调用 SceneLoader.GoToLobbyEntry 进入大厅 |
| `OnClickSetting()` | 0 | 打开设置面板 |
| `OnClickQuitGame()` | 0 | 退出游戏 |

### Start 协程初始化流程

```
State 0: 创建 DisplayClass33_0 → SpriteCollection.PreLoadCommonAtlas → DOTween 动画序列
         → yield return WaitUntil(condition)
State 1: TitleManager.ResetHapticSoundVol → GameBase.StartGame(callback)
         → yield return StartGame 协程
State 2: 等待额外协程
         → yield return
State 3: GameBase.LoadSpriteAtlas
         → yield return LoadSpriteAtlas 协程
State 4: AntiCheat.Init → DRGameMode.Init → 检查 HasPrevData/loadFailed
         → 设置 Continue 按钮 active（根据是否有存档）
         → RefreshButtonList → SetFocus 到第一个按钮
         → DayManager.ReloadSaveData → 播放 BGM_Title + AMB_DeepSea
         → SceneLoaderManagedBehaviour.set_IsSceneReady(true)
         → CanvasGroup.set_alpha(1) → yield return WaitForEndOfFrame
State 5: 启用菜单按钮面板 → IsSceneInitialized = true
         → StartCoroutine(CheckInstalledDLCs)
         → yield return CheckInstalledDLCs 协程
State 6: CheckKeyboardDuplicate → CameraResolution.CheckResoultionForWindow → 结束
```

### ⚠️ IsSceneInitialized 时序陷阱

`IsSceneInitialized` 在 state 5 开头就被设为 true，此时 `CheckInstalledDLCs` 协程尚未完成、`CheckKeyboardDuplicate` 也未执行。如果在 `IsSceneInitialized` 变 true 的同一帧就调用 `ContinueGame()`，会导致：
- 左上角出现 "Saving" 提示
- 主菜单输入系统卡死（上下左右空格无响应）

**正确做法**：在 `IsSceneInitialized` 变 true 后等待数秒（当前实现使用 3 秒延迟）再触发继续，确保所有异步初始化完成。

### ContinueGame 内部流程（ISIL 逆向）

```csharp
void ContinueGame() {
    var saveSystem = Singleton<SaveSystem>.Instance;
    var gdm = saveSystem.GameDataManager;
    var gameData = gdm.???;  // virtual call, 可能是 GetCurrentGameData
    if (gameData == null) return;
    if (gameData[0x28] != 0) gameData[0x28] = 0x100;  // 某个标志位操作
    this.menuButtonPanel?.SetActive(false);  // [this+64] = 菜单按钮面板
    GameBase.RestartGame();
    SaveSystem.SaveAllData();
    GoToLobbyEntry();
}
```

### TitleMenuButton

`DR.Title.TitleMenuButton` — 标题菜单的单个按钮组件。

- `Init(TitleManager titleManager)` — 初始化时绑定 TitleManager 引用
- `SetFocus(bool focus)` — 设置选中状态（高亮）

按钮通过 Unity Editor Inspector 配置 `ButtonName` 枚举值，TitleManager 通过 `OnSelect(ButtonName)` 路由到对应的 `OnNewGame()`/`OnContinueGame()`/`OnLoadGame()` 等方法。

## 14. 参考 Mod 项目

| 项目 | 作者 | 功能 | 参考价值 |
|------|------|------|----------|
| [dave-the-diver-mods](https://github.com/devopsdinosaur/dave-the-diver-mods) | devopsdinosaur | 无限氧气、无敌、自动拾取、加速 | Hook 点选择、BepInEx 6 项目结构、ConfigFile 用法 |
| [DaveDiverMap](https://github.com/qe201020335/DaveDiverMap) | qe201020335 | 屏幕地图覆盖 | 运行时 UI 构建、坐标系统读取、Canvas 动态创建 |
