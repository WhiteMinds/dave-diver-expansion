# 游戏类参考表

已确认的关键游戏类（通过 ilspycmd 反编译验证）。

## 核心类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `PlayerCharacter` : `BaseCharacter` | 玩家角色控制器 | `Update()`, `FixedUpdate()`, `Awake()`, `IsActionLock`(bool), `IsScenarioPlaying`(bool), `SetActionLock(bool)`, `SetInputLock(bool)` ⚠️锁全部, `inputAsset`(DRInputAsset), `OnFire_Performed()` ⚠️Harmony无效, `OnMelee_Performed()` ⚠️同上, `OnGrab_Performed()` ⚠️同上 |
| `InGameManager` : `Singleton<InGameManager>` | 游戏管理器 | `playerCharacter` (获取玩家实例), `GetBoundary()` (当前子区域边界), `SubBoundsCollection` (所有子区域) |
| `Singleton<T>` : `MonoBehaviour` | 单例基类 | `Instance` (静态属性) |
| `SingletonNoMono<T>` : `Il2CppSystem.Object` | 非 MonoBehaviour 单例基类 | `Instance` (静态属性) |
| `DataManager` : `Singleton<DataManager>` | 数据管理器 | `GetText(ref string textID)`（静态+实例两种） |

## 交互 & 物品类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `FishInteractionBody` | 可交互的鱼 | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `InteractionType`, `isInteractable`(bool), `IsEnableInteraction`(bool get/set) |
| `PickupInstanceItem` | 掉落物品基类（所有可拾取物品） | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `isNeedSwapSetID`, `usePreset`, `GetItemID()` |
| `PickupInstanceItem_SeaUrchin` | 海胆（PickupInstanceItem 子类） | `_grabLevel`(int) — 所需 grab level，低于该等级拾取会扣血 |
| `InstanceItemChest` | 宝箱 | `SuccessInteract(BaseCharacter)`, `IsOpen` |
| `CrabTrapZone` | 捕蟹笼区域 | `CheckAvailableInteraction(BaseCharacter)`, `SetUpCrabTrap(int)` |
| `OxygenArea` | 氧气补充区域 | `minHP`, `chargeTime`, `isCharging`（注意：不在宝箱上） |
| `FishInteractionBody` 内部枚举 | 鱼交互类型 | `FishInteractionType`: None=0, Carving=1, Pickup=2, Calldrone=3 |
| `ConditionFishInteraction` | 条件交互控制器（挂在需要解锁才能拾取的鱼上） | `conditionType`(ConditionType), `IsAbleToInteraction`(bool get), `itemUID`(int), `unlockedContent`(ContentsList) |
| `GrabObject` | 可抓取对象 | `_grabLevel`(int), `grabLevel`(int get), `_grabType`(GrabType enum), `_type`(Type: PickUp/NonPickUp) |
| `GrabHandler` | 玩家抓取处理器 | `grabLevel`(int get/set), `_grabLevel`(int) — 通过 `PlayerCharacter.grabHandler` 访问 |
| `CatchableByItem` | 可用道具捕捉的生物 | `isPlayerCatchableThis`(bool get) ⚠️**空壳，永远返回true** |

## 鱼交互条件系统

### ConditionFishInteraction（条件交互控制）

挂在需要特定条件才能拾取的鱼/生物身上（如海天使、海马等需要捕虫网的生物）。

**ConditionType 枚举**: `None=0, HasItem=1, UnlockedContents=2`

**工作机制**（通过 IsilDump 逆向确认）：
- `Awake()`: 将关联的 `FishInteractionBody.isInteractable` 设为 false，`IsEnableInteraction` 设为 false，`InteractionType` 设为 2（Pickup）
- `Update()`: 每帧检查 `conditionType == UnlockedContents`，若 `ContentsUnlockManager.IsUnlock(unlockedContent)` 返回 true，则启用 `FishInteractionBody.IsEnableInteraction` 并设 `InteractionType = Pickup`
- `IsAbleToInteraction` getter: 同上逻辑，但只读

### FishInteractionBody.CheckAvailableInteraction 实际逻辑

⚠️ **重要**：`CheckAvailableInteraction` **不检查** `isInteractable` 或 `IsEnableInteraction`！

通过 IsilDump 逆向确认的实际逻辑：
1. 读取 `InteractionType`
2. `Carving(1)` 或 `Pickup(2)` → 检查 `LootBox.CheckOverloadedState`（背包是否满载），取反返回
3. `Calldrone(3)` → 检查 `PlayerCharacter.IsDroneAvailable`
4. 其他 → 返回 `true`

**影响**：Mod 的 AutoPickup 调用 `CheckAvailableInteraction` 会绕过条件限制（捕虫网等），**必须手动检查** `fish.isInteractable` 来阻止拾取未解锁的条件鱼。

### PickupInstanceItem_SeaUrchin（海胆 grab level 检查）

`PickupInstanceItem_SeaUrchin` 是 `PickupInstanceItem` 的真实子类，有独立的 `NativeClassPtr`。
- 使用 `item.TryCast<PickupInstanceItem_SeaUrchin>()` 可安全检测
- `_grabLevel` 字段标识拾取所需的手套等级
- 与 `PlayerCharacter.grabHandler.grabLevel` 比较判断是否安全

### 捕虫网装备系统

| 类 | 字段/属性 | 说明 |
|---|---|---|
| `NetGear` : MonoBehaviour | `m_CaptureLevel`, `m_CaptrueSize`(NetCaptrueSize), `m_Strength`, `m_CapturedFishes` | 捕虫网装备组件 |
| `GearParameter` | `netCaptureLevel`, `netSize`(NetCaptrueSize), `captureCount`, `netStrength` | 玩家装备参数 |
| `NetCaptrueSize` (enum) | `None=0, Small=1, Medium=2, Big=4, UnCaptureableSize=8, All=7` | 捕虫网可捕获的尺寸 |
| `ItemType` (enum) | `Ingredient_Catch=11` | 需要捕虫网才能获得的食材类型 |

### CatchableByItem 空壳警告

⚠️ `CatchableByItem.isPlayerCatchableThis` 通过 IsilDump 确认实现为 `mov al,1; ret`（直接返回 true），**不能用于判断玩家是否有能力抓取**。`OnGrabCalled()` 同样是空方法（`ret 0`）。

## 鱼 AI 类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `DR.AI.SABaseFishSystem` : `SpecialAttackerFishAISystem` | 鱼 AI 系统 | 所有鱼都有此组件。`FishAIData`(SAFishData)、`IsAggressive`(virtual bool)。**注意**：继承链依赖 `Sirenix.Serialization`，interop 中缺少 `SerializedScriptableObject` 包装类，**不能直接用 `GetComponent<SABaseFishSystem>()`**，需通过 IL2CPP 低级 API 访问 |
| `DR.AI.AwayFromTarget` : `AIAbility` | 逃跑 AI 能力 | 大多数非攻击性鱼有此组件。**但部分攻击性鱼也有**（如虾、海马、鱿鱼），不能仅凭此判断攻击性 |
| `SAFishData` : `SerializedScriptableObject` | 鱼配置数据 | `AggressionType`(FishAggressionType 枚举)、`PeckingOrder`(int)。通过 `SABaseFishSystem.FishAIData` 访问 |
| `BoidGroup` : `DRMonoBehaviour` | 鱼群管理器 | `Start()` 中启动 `InitRoutine` 协程，动态 Instantiate 子鱼 GameObject。每个鱼群是一个 `Boid_SA_*` prefab 克隆，children 数即鱼数 |
| `InhancedAutoActivatorForFish` : `AIAbilityData` | 鱼距离激活器 | `Init(SABaseFishSystem)` 注册到 `AutoActivatorJob`，订阅 `LODData.IsInRangeRP` 回调控制 Behaviour/Renderer 启用 |
| `AutoActivatorJob.LODData` | 距离 LOD 数据 | `IsInRangeRP`(ReactiveProperty\<bool\>) — 玩家是否在范围内。鱼的激活阈值 0.73f / 停用阈值 1.1f |

## 鱼的生成与激活机制

### 生成方式

鱼**不是**场景加载时全部预创建的。`BoidGroup`（鱼群管理器）在 `Start()` 中启动 `InitRoutine` 协程，**按需动态 Instantiate** 子鱼 GameObject。玩家游到新区域时，该区域的 BoidGroup 触发 Start → 生成鱼 → 鱼的 `FishInteractionBody.Awake()` 触发 → 注册到 EntityRegistry。

日志实测数据（单次潜水）：registry 中鱼数从 3 → 20 → 50 → 101 随玩家探索逐步增长，且每次增长都对应 `BoidGroup.Start` 日志。

### 距离激活系统（AutoActivatorJob + LODData）

鱼创建后，`InhancedAutoActivatorForFish.Init` 将其注册到 `AutoActivatorJob` 距离管理系统：

1. 调用 `AutoActivatorJob.RequestManagement(fishAI)` 注册
2. 设定距离阈值：激活 0.73f / 停用 1.1f（归一化比例）
3. 订阅 `LODData.IsInRangeRP`（ReactiveProperty\<bool\>）的变化回调
4. 当 `_isIn=true`（进入范围）：`Behaviour.enabled = true`、`Renderer.enabled = true`
5. 当 `_isIn=false`（离开范围）：`Behaviour.enabled = false`、`Renderer.enabled = false`、停止声音

### EntityRegistry 捕获验证

通过 `FindObjectsOfType<FishInteractionBody>` 与 `EntityRegistry.AllFish` 的对比诊断（已验证并移除）：
- **`missingFromRegistry=0`** — EntityRegistry 通过 `Awake` patch 捕获了所有鱼，无遗漏
- **`sceneFish < registry fish`** — `FindObjectsOfType` 只返回 active 对象；远距离鱼被 `SetActive(false)` 后不会被 FindObjectsOfType 返回，但仍存在于 EntityRegistry 中
- **结论**：DiveMap 标记"靠近才出现"是因为鱼本身是 BoidGroup 按需动态创建的，不是 EntityRegistry 遗漏

### 鱼攻击性检测（FishAggressionType）

通过 IL2CPP 低级 API 读取 `SABaseFishSystem.FishAIData.AggressionType` 字段值。

`FishAggressionType` 枚举：`None=0, OnlyRun=1, Attack=2, Custom=3, Neutral=4, OnlyMoveWaypoint=5`

判定逻辑：
- `Attack(2)` → 攻击性
- `Custom(3) + 无 AwayFromTarget` → 攻击性
- `Custom(3) + 有 AwayFromTarget` → 可捕捉生物（虾、海马等）
- 其余 → 非攻击性
- ⚠️ **不能仅用 `AwayFromTarget == null` 判断**：鱿鱼有 AwayFromTarget 但实为攻击性

已验证的鱼种分类（日志实测）：

| AggressionType | AwayFromTarget | 鱼种举例 | 地图颜色 |
|---|---|---|---|
| Attack | 无 | 鲨鱼、海鳗、狮子鱼、梭鱼、水母 | 红 |
| Attack | 有 | 长枪乌贼、洪堡鱿鱼 | 红 |
| Custom | 无 | 河豚、鹦鹉螺 | 红 |
| Custom | 有 | 白对虾、黑虎虾、海马 | 浅绿 |
| OnlyRun | 有 | 大多数普通鱼 | 蓝 |
| OnlyMoveWaypoint | 无 | 金枪鱼（路径移动） | 蓝 |

**IL2CPP 低级 API 读取方式**（绕过 Sirenix 类型引用限制）：

```csharp
// 缓存类指针和字段偏移（只需初始化一次）
var classPtr = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "DR.AI", "SABaseFishSystem");
var fieldPtr = IL2CPP.GetIl2CppField(classPtr, "FishAIData");
var dataClassPtr = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "SAFishData");
var aggrFieldPtr = IL2CPP.GetIl2CppField(dataClassPtr, "AggressionType");
// 读取：遍历 GetComponents<Component>() 找到 SABaseFishSystem（通过 GetIl2CppType().FullName 匹配）
// 然后用 Marshal.ReadIntPtr / Marshal.ReadInt32 + il2cpp_field_get_offset 读取字段值
```

## 相机 & 场景类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `OrthographicCameraManager` : `Singleton<>` | 主相机管理器 | `m_Camera`（主 Camera）。注意：`m_BottomLeftPivot`/`m_TopRightPivot` 在 `CalculateCamerabox()` 前为 (0,0)，不可靠 |
| `DR.CameraSubBoundsCollection` | 相机子区域集合 | `m_BoundsList`（`List<CameraSubBounds>`）。通过 `InGameManager.SubBoundsCollection` 访问 |
| `DR.CameraSubBounds` | 单个相机子区域 | `Bounds`（Unity `Bounds`，含 center/size/min/max） |
| `Interaction.Escape.EscapePodZone` | 逃生舱区域 | `transform.position`，`OnTriggerEnter2D`，标准潜水场景约 9 个 |
| `Interaction.Escape.EscapeMirror` | 逃生镜区域 | `transform.position`，`IsAvaiableUseMirror()`，冰川区域可能有 |
| `SceneLoader` | 场景加载器 | `k_SceneName_MermanVillage`（鲛人村场景名常量） |

## UI & 场景切换类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `Common.Contents.MoveScenePanel` | 场景切换菜单面板 | `OnPlayerEnter(bool)`, `ShowList(bool)`, `OnOpen()`, `OnClick()`, `OnCancel()`, `IsOpened`, `IsEntered` |
| `Common.Contents.MoveSceneTrigger` | 场景切换触发器（碰撞体） | `OnPlayerEnter(bool)`, 引用 `m_Panel`(MoveScenePanel) |
| `Common.Contents.MoveSceneElement` | 场景切换菜单选项 | `sceneName`(枚举: Lobby/SuShi/Farm/FishFarm/SushiBranch/Dredge), `OnClick()`, `IsLocked`, `CheckUnlock()` |
| `LobbyMainCanvasManager` : `Singleton<>` | Lobby 场景 UI 管理器 | `MoveScenePanel`(属性), `OnDREvent(DiveTriggerEvent)`, `isInitFinish`(bool, 见下方 § 各场景玩家类) |
| `DiveTrigger` | 潜水出发触发器 | `m_Panel`(LobbyStartGamePanelUI), `OnPlayerEnter(bool)` |
| `LobbyStartGamePanelUI` | 潜水出发面板（长按出发） | `Activate(bool)`, `StartGame(StartParameter)`, `StartGameByMirror(string, SceneConnectLocationID)` |
| `MainCanvasManager` : `Singleton<>` | 游戏内 UI 画布管理器 | `pausePopupPanel`(PausePopupMenuPanel), `quickPausePopup`(QuickPausePopup), `IsQuickPause`(bool) |
| `PausePopupMenuPanel` : `BaseUI` | ESC 暂停菜单面板 | `OnPopup()`, `OnClickContinue()`, `OnClickSettings()`, `ShowReturnLobbyPanel()` 。检测是否打开：`gameObject.activeSelf` |
| `BaseUI` : `MonoBehaviour` | 游戏 UI 面板基类 | `uiDepth` 字段 |

## 语言 & 本地化类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `DR.Save.SaveUserOptions` : `SaveDataBase` | 用户设置（语言、音量、按键等） | `CurrentLanguage`, `CheckLanguage(SystemLanguage)` |
| `DR.Save.Languages` | 游戏语言枚举 | `Chinese=6`, `ChineseTraditional=41`, `English=10` |
| `FontManager` | 字体管理器 | `GetFont(SystemLanguage)`, `GetFontAsset(SystemLanguage)` |

> 所有交互类使用 `OnTriggerEnter2D` 检测玩家碰撞，`SuccessInteract` 触发实际拾取。

## 矿物 & 采矿类

| 类名 | 作用 | 关键方法/字段 |
|------|------|---------------|
| `BreakableLootObject` : `MonoBehaviour` | 可破坏的掉落物（矿石、碎石堆、海藻） | `OnEnable()`, `OnDie()` (virtual), `IsDead()` (virtual, bool), `lootObjectType` (BreakableLootObjectType) |
| `InteractionGimmick_Mining` : `InteractionGimmick` | 采矿交互节点（钻头矿点） | `Awake()` (override), `OnDie()` (virtual), `isClear` (bool) |

### BreakableLootObjectType 枚举

```
Ore_Opal=0, Ore_Lead=1, Ore_Copper=2, Ore_Iron=3, Ore_Diamond=4, Ore_Amethyst=5, Pile=6, SeaWeed=7
```

- 0-5 为矿石类型，6-7 为非矿石（碎石堆、海藻）
- 注册矿物时需过滤 `Pile` 和 `SeaWeed`

### 矿物对象生命周期与 Harmony 补丁

**关键发现**：游戏对远处矿物执行 `SetActive(false)`，但 `transform.position` 仍然有效（矿物是静态的，不会移动）。因此在地图标记等场景中，不应用 `activeInHierarchy` 过滤矿物。

**Harmony 补丁注意事项**：

- ✅ `BreakableLootObject.OnEnable` — 可安全 patch（注册矿物）
- ✅ `BreakableLootObject.OnDie` — 可安全 patch（移除矿物）
- ✅ `InteractionGimmick_Mining.Awake` — 可安全 patch（注册矿点）
- ⛔ `InteractionGimmick_Mining.OnDie` — **不能 patch**！这是 virtual 方法，IL2CPP trampoline 在被基类/兄弟类调用时会 NRE 崩溃（`__instance` 转型失败），即使加 null guard 也无效（crash 发生在 Postfix 之前的 trampoline 代码中）
- 清理策略：依赖 `EntityRegistry.Purge()` 的周期性 `RemoveWhere(null)` + 扫描时的 `isClear` 过滤

## 玩家状态锁定系统

游戏通过多层锁定机制控制玩家操作：

| 属性/系统 | 作用 | 过场动画期间 |
|---|---|---|
| `PlayerCharacter.IsScenarioPlaying` | 脚本剧情（cutscene/scenario）正在播放 | ✅ **实际生效** |
| `PlayerCharacter.IsActionLock` | 动作锁定（对话、Boss 演出等） | ❌ 过场中不一定为 true |
| `DRInput.ActionLock_*` | 输入层锁定（Player/UI/Shooting/InGame） | 底层系统 |
| `InputLockBundlePopup` | 统一锁定管理：`LockPlayer()`, `LockAll()` 等 | 底层系统 |
| `TimelineController` | `LockPlayerControl()` / `UnlockPlayerControl()` | Timeline 专用 |
| `ScenarioManager` | `SetInputLock(bool)` | Scenario 专用 |

**Mod 开发建议**：检查玩家是否可操作时，用 `player.IsActionLock || player.IsScenarioPlaying` 双重判断。

### InputSystem 精细控制（禁用特定操作）

`SetInputLock(bool)` 和 `ActionLock` 系统都只能锁定全部输入。要精细控制（如只禁用战斗、保留移动），需直接操作 Unity `InputAction`：

| 类 | 命名空间 | 作用 |
|---|---|---|
| `DRInputAsset` | `DRInput` | 游戏 InputAsset 封装，含 ActionMap_Dave/InGame/UI 等嵌套类型 |
| `DRInputAsset.DRInputAssetEntry` | `DRInput` | Entry 内部类，提供 `inputActionAsset` 属性返回 Unity `InputActionAsset` |
| `InputActionAsset` | `UnityEngine.InputSystem` | Unity InputSystem 核心类，`FindActionMap(string)` |
| `InputActionMap` | `UnityEngine.InputSystem` | action map，`FindAction(string)` 返回 `InputAction` |
| `InputAction` | `UnityEngine.InputSystem` | 单个 action，`Disable()` / `Enable()` 控制是否触发回调 |

详见 [docs/game-internals.md](game-internals.md) § InputSystem 与 Harmony 限制。

## PickupInstanceItem 物品系统

**关键发现**: 游戏中所有可拾取物品的 IL2CPP 运行时类型都是 `PickupInstanceItem`（包括武器）。区分物品类型只能通过 **prefab 名称**（`gameObject.name`），不能通过 C# 类型。

**双对象机制**: 每个动态掉落物品（`usePreset=False`）在场景中存在 **两个** GameObject：
- 一个 `isNeedSwapSetID = itemID`（"幽灵"标记对象，不可交互）
- 一个 `isNeedSwapSetID = 0`（真正可拾取的对象）
- 生成点固定物品（`usePreset=True`）只有一个对象（`swapSetID=0`）

**Prefab 名称分类**:

| Prefab 名称 | 类型 | usePreset | 可自动拾取 |
|---|---|---|---|
| `Loot_Wood`, `Loot_Rope`, `Loot_ShellFish*` 等 | 材料（生成点） | True | ✅ |
| `BulletBox` | 弹药 | True | ✅ |
| `Loot_Fragment` | 碎片 | True | ✅ |
| `SeaWeedsTemplate` | 海藻 | False | ✅ |
| `OreLootObject` | 矿石 | False | ✅ |
| `SeaUrchinItem` | 海胆 | False | ✅ |
| `SeasoningTemplate` | 调料 | False | ✅ |
| `PickupUpgradeItemKit` | 升级套件 | False | ✅ |
| `PickupInstanceMelee` | 近战武器 | False | ❌ 会触发换武器循环 |
| `PickupInstanceWeapon` | 远程武器 | False | ❌ 会触发换武器循环 |
| `PickupElecHarpoonHead` | 电鱼叉头 | False | ❌ 装备类 |
| `PickupChainHarpoonHead` | 连锁鱼叉头 | False | ❌ 装备类 |

**武器换装循环问题**: 自动拾取武器 → 触发 `SuccessInteract` → 游戏执行装备切换，掉落当前武器 → 新掉落的武器被自动拾取 → 无限循环。解决方案：按 prefab 名称黑名单跳过 `PickupInstance*` 和 `*HarpoonHead*`。

**`UpgradeKitInstanceItem` 子类**: 是 `PickupInstanceItem` 的子类，调用 `GetItemID()` 会抛出 `NullReferenceException`，需要 try-catch 保护或避免调用。

## 宝箱 prefab 名称分类

- `Chest_O2(Clone)` — 浅海氧气宝箱
- `Loot_ShellFish004(Clone)` — 深海氧气宝箱（也是 O2）
- `Chest_Item(Clone)` — 普通物品箱 | `Chest_Weapon(Clone)` — 武器箱
- `Chest_IngredientPot_A/B/C(Clone)` — 食材罐
- `Chest_Rock(Clone)` — 岩石箱 | `Quest_Drone_*_Box` — 任务箱
- `OxygenArea` 组件不在宝箱上（所有宝箱 `hasOxygenArea=False`），不能用于检测 O2 箱
- O2 检测：`name.Contains("O2") || name.Contains("ShellFish004")`

## 游戏语言系统

### 类型层次

| 类名 | 作用 | 关键成员 |
|------|------|----------|
| `DR.Save.SaveSystem` : `Singleton<SaveSystem>` | 存档系统入口（单例） | `UserOptionManager`(SaveSystemUserOptionManager), `PlayerDataManager`, `GameDataManager` |
| `DR.Save.SaveSystemUserOptionManager` : `SaveLoadManagerBase<SaveUserOptions>` | 用户设置管理器 | `CurrentLanguage`(Languages get, CallerCount=19), `CurrentSystemLanguage`(SystemLanguage get), `SetLanguage(Languages)` |
| `DR.Save.SaveUserOptions` : `SaveDataBase` | 用户设置数据类（序列化） | `CurrentLanguage`(Languages get/set) — ⛔ **不要直接读取，见下方陷阱** |
| `DR.Save.Languages` | 游戏语言枚举 | `Unknown=0, Chinese=6, English=10, Japanese=22, Korean=23, ChineseTraditional=41` |

### Mod 获取游戏语言的正确方式

直接调用 `SaveSystem` 单例 API（与游戏自身 159 处调用方相同的路径）：

```csharp
var saveSystem = Singleton<DR.Save.SaveSystem>._instance;
if (saveSystem != null)
{
    var optMgr = saveSystem.UserOptionManager;
    if (optMgr != null)
    {
        var lang = optMgr.CurrentLanguage;  // 正确！带 fallback 逻辑
        bool isChinese = lang == DR.Save.Languages.Chinese
            || lang == DR.Save.Languages.ChineseTraditional;
    }
}
```

### SaveSystemUserOptionManager.get_CurrentLanguage 内部逻辑（IsilDump 逆向）

1. 获取内部 `SaveUserOptions` Data 实例
2. 如果 Data 不为 null → 读取 `Data.CurrentLanguage`（backing field 偏移 0x4C）
3. **如果 Data 为 null** → fallback 到 `Application.systemLanguage` → `LanguageExtension.ToLanguage()`

### ⛔ 不要 Harmony patch `SaveUserOptions.get_CurrentLanguage`

`SaveUserOptions.CurrentLanguage` 是自动属性（`mov eax,[rcx+4Ch]; ret`），IL2CPP 下该 getter 会被大量**无关的 save-data 字段读取**误触发，产生垃圾值（实测出现：3, 4, 6, 75, 200, 999, 99999 等）。`Chinese=6` 这个枚举值太小，垃圾读取极易碰到，导致语言误判。

实测 Harmony Postfix 在前 50 次调用中出现：`raw=10(English), 3, 3, 3, ..., 6(Chinese!误触发), 8, 75, 29, 95, ...`——第 15 次调用的垃圾值 6 恰好等于 `Chinese` 枚举值。

## 游戏场景切换系统

Lobby/寿司店/农场等非潜水场景之间的切换：

```
MoveSceneTrigger (碰撞触发器，检测玩家进入)
  ↓ OnPlayerEnter(true) → m_Panel.OnPlayerEnter(true)
MoveScenePanel (UI 面板，显示可选场景列表)
  ↓ ShowList(true) → 显示已解锁的 MoveSceneElement
  ↓ OnOpen() / OnClick() → 选择场景
MoveSceneElement (单个选项)
  ↓ sceneName 枚举: Lobby, SuShi, Farm, FishFarm, SushiBranch, Dredge
  ↓ OnClick() → CheckUnlock() → 执行场景切换
```

- 每个非潜水场景有各自的 `MoveScenePanel` 实例
- 潜水出发是另一个系统：`DiveTrigger` → `LobbyStartGamePanelUI`（长按出发）

## 各场景玩家类型与状态检测（⚠️ 未验证）

> **注意**：以下信息来自反编译代码分析和 IsilDump 推断，尚未通过实际 mod 开发验证。部分结论可能不准确。

每个游戏场景使用**完全不同的玩家类**，没有统一的"玩家是否可操作"接口：

| 场景 | 场景名 | 玩家类 | 状态检测 |
|------|--------|--------|----------|
| Lobby | `DR_Lobby` | `LobbyPlayer` | `CurrentState` 枚举（Idle=可操作） |
| 潜水 | `DR_InGame` | `PlayerCharacter` | `IsActionLock`, `IsScenarioPlaying` |
| 寿司店 | `DR_SushiBar` | `SushiBarPlayerHanlder`（注意游戏原始拼写错误） | `IsMovable`(方法), 无锁定属性 |
| 农场 | `DR_Farm` | `FarmPlayerView` | 无已知锁定属性 |
| 鱼塘 | `DR_FishFarm` | `FishFarmPlayerView` | 无已知锁定属性 |

### LobbyPlayer 状态枚举

```
LobbyPlayerState: Idle=0, Call, Die, Clear, MaskOffClear, Diving, ThumbUp,
                  MorningStart, AfternoonStart, EveningStart, Memo, EnterBoat, InBoat
```

- 只有 `Idle` 表示玩家可自由移动
- `IsMoving`(bool) 属性表示是否正在移动动画中

### Lobby 初始化序列（⚠️ 未验证）

加载 Lobby 场景时的初始化顺序（基于 IsilDump 推断，实际时序可能不同）：

1. `LobbyPlayer` 创建，初始状态为 `Idle`（**此时玩家尚未准备好，但 state=Idle**）
2. `LobbyMainCanvasManager` 初始化，`isInitFinish=false`
3. `LobbyPostRoutine.AfterNewMissionFinished()` 协程执行（对话、过场、UI 设置等）
4. 协程中调用 `LobbyPlayer.ChangeLobbyPlayerState(MorningStart)`（开始早晨动画）
5. 协程完成后设置 `LobbyMainCanvasManager.isInitFinish=true`

**⚠️ 关键间隙**：步骤 1-3 之间存在若干帧的间隙，此时 `LobbyPlayer.CurrentState==Idle` 但场景尚未准备好。可用 `Singleton<LobbyMainCanvasManager>._instance?.isInitFinish` 检测是否初始化完成。

### 跨场景玩家检测的难点（⚠️ 未验证）

- `PlayerCharacter.IsActionLock` 在非潜水场景（寿司店、农场等）中**永远为 true**——不能作为通用判断
- `PlayerCharacter.IsScenarioPlaying` 是可靠的剧情播放检测，但仅存在于 `PlayerCharacter`
- 非潜水场景（寿司店、农场）没有已知的统一锁定 API
- `MoveScenePanel` 的存在可作为"场景已加载且可交互"的近似判断（proxy）
- 场景切换期间，对象会短暂销毁/重建，造成状态检测的"闪烁"（brief windows of incorrect state）
