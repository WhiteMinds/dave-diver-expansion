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

**推荐**：检查 `DR.AI.AwayFromTarget` 组件的有无
```csharp
bool isAggressive = fish.GetComponent<DR.AI.AwayFromTarget>() == null;
```

| 鱼类型 | AwayFromTarget | 额外攻击组件 |
|--------|----------------|-------------|
| Boid 群鱼（小丑鱼等） | ✅ 有 | 无 |
| 鲨鱼 (Thresher_Shark) | ❌ 无 | Painable, DefaultSprintable, BodyDamager |
| 河豚 (Stellate_Puffer) | ❌ 无 | Damager, BodyDamager |
| 海鳗 (Moray_Eel) | ❌ 无 | Attackable |

### 宝箱类型识别

通过 `gameObject.name` 区分：
- `Chest_O2(Clone)` — 氧气宝箱
- `Chest_Item(Clone)` — 道具宝箱
- `Chest_Weapon(Clone)` — 武器宝箱
- `Chest_Rock(Clone)` — 岩石宝箱
- `Chest_IngredientPot_A/B/C(Clone)` — 食材罐

### 关卡边界

- `InGameManager.GetBoundary()` — 返回关卡完整边界 `Bounds`（推荐）
- `InGameManager.CurrentCameraBounds` — 备选
- `OrthographicCameraManager.m_BottomLeftPivot/m_TopRightPivot` — **不可靠**，在 `CalculateCamerabox()` 调用前为 (0,0)

### IL2CPP 命名空间陷阱

运行时组件名不一定与 interop DLL 中的完全限定名匹配。例如：
- 运行时报告 `AwayFromTarget` → 实际是 `DR.AI.AwayFromTarget`
- 搜索时用 `ilspycmd -l type dll | grep -i "ClassName"` 可找到完整命名空间
- `decompiled/` 目录中可能找不到某些类（反编译失败），但 interop DLL 中仍存在

---

## 9. 参考 Mod 项目

| 项目 | 作者 | 功能 | 参考价值 |
|------|------|------|----------|
| [dave-the-diver-mods](https://github.com/devopsdinosaur/dave-the-diver-mods) | devopsdinosaur | 无限氧气、无敌、自动拾取、加速 | Hook 点选择、BepInEx 6 项目结构、ConfigFile 用法 |
| [DaveDiverMap](https://github.com/qe201020335/DaveDiverMap) | qe201020335 | 屏幕地图覆盖 | 运行时 UI 构建、坐标系统读取、Canvas 动态创建 |
