# iDiver 升级系统逆向文档

## 类层级

```
IDiverPanel (PhoneAppBase)              ← 手机 App 入口 (PhoneAppList.iDiver = 14060003)
  └── LobbyEquipUpgradeScrollPanel      ← 装备类型滚动列表 (BaseScrollController)
        └── LobbyUpgradeEquipScrollCell ← 每个装备类型的单元格
  └── IDiverItemDetailPanel             ← 详情面板（当前/下级属性对比）
  └── IDiverLevelUpPanel                ← 升级动画面板
        └── LobbySubEquipmentUpgradePopupPanel ← 升级弹窗动画
```

**状态机**: `LobbyEquipUpgradeScrollPanel.IDiverState { Scroll, Detail, LevelUP }`

## SubEquipmentType 枚举

```csharp
public enum SubEquipmentType {
    none = 0,
    o2_tank = 1,        // 氧气瓶
    diving_suit = 2,    // 潜水服（深度+HP）
    inventory = 3,      // 货箱（负重）
    Harpoon = 4,        // 鱼叉（特殊 UI 路径）
    drone = 5,          // 无人机（需解锁 0x273D=10045）
    basic_knife = 6,    // 近战刀（需解锁 0x2B05=11013）
    crab_trap = 7,      // 蟹笼（需解锁 0x2753=10067）
    max = 8
}
```

## 数据结构

### DR.SubEquipment（JSON 数据源：`DR_GameData_Equipment`）

| 偏移 | 属性 | 类型 | 说明 |
|------|------|------|------|
| +16 (0x10) | TID (Key) | int | 唯一 ID，**下一级 = TID + 1** |
| +24 (0x18) | NameTextID | string | 本地化名称 key |
| +32 (0x20) | DescTextID | string | 本地化描述 key |
| +40 (0x28) | UIIcon | string | 图标 sprite 名 |
| +48 (0x30) | SubEquipmentType | int | 装备类别 |
| +52 (0x34) | SubEquipmentLevel | int | 等级编号 |
| +56 (0x38) | Price | int | 升级价格（金币） |
| +60 (0x3C) | UnlockMission | int | 前置任务 ID（0=无） |
| +64 (0x40) | MaxHP | float | HP 加成 |
| +68 (0x44) | MaxDepthWater | float | 最大深度加成 |
| +72 (0x48) | LootboxWeight | float | 负重加成 |
| +76 (0x4C) | OverwightThreshold | float | 超重阈值 |
| +80 (0x50) | EquipmentItemID | int | 关联 IntegratedItem TID |
| +84 (0x54) | DroneCount | int | 无人机数量 |
| +88 (0x58) | TrapCount | int | 蟹笼数量 |

构造器：`new DR.SubEquipment(tID, nameTextID, descTextID, uIIcon, subEquipmentType, subEquipmentLevel, price, unlockMission, maxHP, maxDepthWater, lootboxWeight, overwightThreshold, equipmentItemID, droneCount, trapCount)`（15 参数）

命名空间：`DR`，继承 `DesignSheetDataHelper<Int32, SubEquipment>`。
⚠️ 在 interop 反编译输出中**没有独立的 .cs 文件**（ilspycmd 未输出），但类型存在于 Assembly-CSharp 中，`DataManager` 可引用。需验证是否能直接 `new`，如不行需用 IL2CPP 低级 API 创建。

### IntegratedItem（运行时武器/物品包装）

- 继承 `Il2CppSystem.Object`（不是 DesignSheetDataHelper）
- 无参构造器，所有属性有 getter/setter
- 关键属性：`TID`, `specData` (SpecDataBase), `IntegratedType` (int)
- 通过 `IntegratedItem.BuildEquip(DR.EquipmentItem)` 或 `BuildItem(DR.Items)` 创建（AutoMapper 复制字段 + `BuildInternal` 设置 specData）
- 手动创建时需自行设置 `specData`

### SpecDataBase（武器/装备规格数据）

- 继承 `SerializedScriptableObject` → `ScriptableObject`
- 关键字段（全有 getter/setter）：`_TID`, `_Name`, `_Damage` (int), `_IsMaxLevel`, `EquipObjectReference`
- 可通过 `ScriptableObject.CreateInstance<SpecDataBase>()` 创建
- 也可通过 `UnityEngine.Object.Instantiate(existing)` 克隆

### SubEquipmentUIInfo（UI 显示数据桥梁）

```csharp
public class SubEquipmentUIInfo {
    Sprite subEquipIcon;    // +16
    string subEquipName;    // +24
    int subEquipID;         // +28 (当前 TID)
    int subEquipLevel;      // +2C
    int subEquipPrice;      // +30 (下一级价格)
    int UnlockMission;      // +34
    bool isMaxLevel;        // +38
    SubEquipmentStatusInfo subEquipStatus;  // 属性对比
}
```

### SubEquipmentStatusInfo（属性比较结构）

```csharp
public struct SubEquipmentStatusInfo {
    StatusDefine statusName;      // 哪个属性
    float currentStatusValue;     // 当前值
    float nextStatusValue;        // 下一级值
    float currentLevel;
    float nextLevel;
}
```

## 鱼叉数据

### SubEquipment 表（Harpoon, type=4）

| TID | Level | Price | EquipmentItemID | 说明 |
|-----|-------|-------|-----------------|------|
| 3060301 | 1 | 0 | 3013101 | Old Harpoon Gun |
| 3060302 | 2 | 300 | 3013111 | Iron Harpoon Gun |
| 3060303 | 3 | 700 | 3013121 | Pump Harpoon Gun |
| 3060304 | 4 | 1500 | 3013131 | Merman Harpoon Gun |
| 3060305 | 5 | 4500 | 3013132 | NewMV Harpoon Gun |
| 3060306 | 6 | 9700 | 3013133 | Alloy Harpoon Gun |

### IntegratedItem 表（对应武器伤害）

| TID | WeaponDamage | Name |
|-----|-------------|------|
| 3013101 | 3 | OldHarpoonGun |
| 3013111 | 10 | IronHarpoonGun |
| 3013121 | 17 | PumpHarpoonGun |
| 3013131 | 24 | SeamanHarpoonGun |
| 3013132 | 32 | NewMVHarpoonGun |
| 3013133 | 40 | AlloyHarpoonGun |

起始 TID 来自 `GameConstValueInfo.startHarpoonSubEquipID`（运行时值 3060301）。

## 核心流程

### 满级判定

`DataManager.GetSubEquipment(currentTID + 1)` 返回 `null` → `isMaxLevel = true`

纯数据驱动，无硬编码上限。

### 升级流程

1. `CalcUpgradeState()` → `Enable`/`NoMoney`/`MaxLevel`/`Lock`
2. `OnUpgradeButtonPressed()`:
   - 获取 `DataManager.GetSubEquipment(currentTID + 1)` → nextItem
   - `IDiverLevelUpPanel.ShowLevelUpPanel(currentTID, callback)`
   - 回调中：`AddSubEquip(nextTID)` + `EquipSubEquip(nextTID)` + `CommonDefine.AddPlayerGoods(1, -price, 0x7FFFFFFF, 0)` 扣金
3. 存档更新：`SaveData.UpdateSubEquipData(newTID, true)` + `RemoveSubEquipData(oldTID)`

### AddCellData（类型列表构建）

```
for (int type = 1; type < 8; type++) {
    if (type == 5 && !ContentsUnlockManager.IsUnlock(10045)) continue;
    if (type == 6 && !ContentsUnlockManager.IsUnlock(11013)) continue;
    if (type == 7 && !ContentsUnlockManager.IsUnlock(10067)) continue;
    cellDataList.Add(new ScrollerCellData { subEquiptype = type });
}
```

循环范围 1-7 **硬编码**（`cmp ebx, 8`）。新增类型需 Postfix 追加。
⚠️ `AddCellData` 是 `protected virtual override`（CallerCount=0）。

### MakeStatusDic 属性映射

| SubEquipment 字段 | StatusDefine | 取值方式 |
|---|---|---|
| MaxHP (+64) | max_hp (2) | 直接读取 float |
| MaxDepthWater (+68) | max_depth_water (1) | 直接读取 float |
| LootboxWeight (+72) | loot_box_weight (9) | 直接读取 float |
| EquipmentItemID (+80) | damage (12) | **间接**：`GetIntegratedItem(id)` → `specData` (+112) → `_Damage` (+104) → int→float |
| DroneCount (+84) | dronecount (13) | 直接读取 int→float |
| TrapCount (+88) | crabTrapcount (14) | 直接读取 int→float |

### 武器伤害路径（战斗中）

```
HarpoonWeaponHandler.EquipItem(SpecDataBase)
  → HarpoonProjectile.SetEquipData(damage, buff, renderer)
    → 存入 static m_ProjectileDamage
      → BuffedProjectileDamage getter 读取 + 应用 buff 系数
        → CollisionDetection → AttackData.DoDamage(target)
```

**伤害 ≠ StatusDefine.damage 属性**。实际战斗伤害来自 `specData._Damage`，经 `SetEquipData` 存入 static 字段，经 `BuffedProjectileDamage` 应用 buff。`StatusDefine.damage` 仅用于 iDiver UI 显示。

## 存档结构

### SubEquipmentInfoSave

```csharp
Dictionary<int, SubEquipmentInfoSave> m_SubEquipmentData  // on SaveData
// Each entry: { ObscuredInt SubEquipID, ObscuredBool IsEquipped, ObscuredBool IsNew }
```

Key = SubEquipment.TID。升级时旧条目移除、新条目添加。

### SubEquipmentManager.Init() 存档加载

1. 遍历 `SaveData.subEquipList`，对每个 entry 调 `AddSubEquip(tid)` + `EquipSubEquip(tid)`
2. `AddSubEquip` 内部调 `DataManager.GetSubEquipment(tid)`，如返回 null 则跳过
3. 调 `AddBasicSubEquip()` 确保每个类型至少有基础装备
4. 补全缺失类型（loop 1-7）

**卸载 mod 安全性**：孤儿 TID 的 `GetSubEquipment()` 返回 null → `AddSubEquip` 返回 false → 跳过

### 可开关功能的 Patch 分层设计

自定义升级项需要支持运行时开关（关闭时隐藏 UI + 移除效果，但保留存档数据，重新开启后立即恢复）。Patch 按职责分为两层：

| 层 | 受开关控制 | Patch | 说明 |
|----|-----------|-------|------|
| 数据层 | ❌ 始终生效 | GetSubEquipment, GetIntegratedItem, SubEquipmentManager.Init | 保证存档数据正常加载，不会因关闭功能而丢失升级进度 |
| 表现层 | ✅ 受 `_enabled` 控制 | AddCellData, SetSubEquipUIInfo, SetUIData, BuffedProjectileDamage | 控制 UI 显示和战斗效果 |

这种分层使得关闭后存档中的 SubEquipmentInfoSave 条目仍被正常读取和维护，重新开启时无需重新升级。

## DataManager 数据字典访问

```csharp
// SubEquipment 字典
DataManager dm = Singleton<DataManager>._instance;
Dictionary<int, SubEquipment> subEquipDic = dm.SubEquipmentDataDic; // CallerCount=2

// IntegratedItem 字典
Dictionary<int, IntegratedItem> itemDic = dm.IntegratedItemDic; // CallerCount=0

// 单条查询
SubEquipment se = dm.GetSubEquipment(tid);       // CallerCount=25, 非 virtual
IntegratedItem item = dm.GetIntegratedItem(tid);  // CallerCount=117, 非 virtual
```

## SubEquipmentManager 关键方法

| 方法 | CallerCount | Virtual | 安全 |
|------|------------|---------|------|
| `Init()` | 5 | ✗ | ✓ |
| `SetSubEquipUIInfo(type)` | 1 | ✗ | ✓ |
| `GetEquipedSubEquipByType(type)` | — | ✗ | ✓（用 ContainsKey，缺 key 返 null） |
| `AddSubEquip(int id)` | — | ✗ | ✓（内部调 GetSubEquipment，null → return false） |
| `EquipSubEquip(int id)` | — | ✗ | ✓ |
| `MakeStatusDic(SubEquipment, dict)` | — | ✗ | ✓ |

### m_EquipSubEquips（offset +24）

`Dictionary<SubEquipmentType, int>` — key=类型, value=当前装备的 TID。`GetEquipedSubEquipByType` 先 `ContainsKey` 再 `get_Item`，缺 key 返回 null 不抛异常。

## 自定义升级项定义

| TypeId | 名称 | 每级效果 | 满级 | 价格规则 | 效果方式 |
|--------|------|---------|------|---------|---------|
| 100 | 鱼叉伤害强化 | +8 伤害 | 5级 | 10000g | BuffedProjectileDamage Postfix | Harpoon 图标 |
| 101 | 戴夫移速强化 | +5% | 5级 | 5000×level | BuffDataContainer.AddMoveSpeedParam | diving_suit 图标 |
| 102 | 推进器速度强化 | +5% | 5级 | 5000×level | 每帧从 spec base 设目标值 | `Booster_Thumbnail` |
| 103 | 推进器持续时间强化 | +10s | 5级 | 5000g | 逐帧 drain 补偿 | `Booster_Thumbnail` |

TID 范围：SubEquipBaseTID = 9900000/9900100/9900200/9900300, IntItemBaseTID = 9990000/9990100/9990200/9990300

## 效果实现架构

### 统一效果入口

所有效果统一在 `PlayerCharacter.Update` (CallerCount=0) 的 Prefix/Postfix 中实现，避免分散 patch。

### IL2CPP CallerCount 内联约束（比预期更严格）

**CallerCount > 0 的方法全部被 IL2CPP 内联**，Harmony patch 静默失败。即使 CallerCount=1、2、3、12 的方法也不安全。只有 CallerCount=0 的 Unity 回调（Update、FixedUpdate、Awake 等）可以安全 patch。

这与 virtual 方法约束是**两条独立规则**：
| 约束 | 条件 | 后果 |
|------|------|------|
| CallerCount 内联 | CallerCount > 0 | patch 静默无效 |
| Virtual 方法 | 继承链中的 virtual override | IL2CPP trampoline 崩溃 |

两条必须同时满足。例如 `BoosterHandler` 方法 CallerCount=0 但是 SubHelperHandler 的 virtual override → 不安全。

### 各升级项效果实现

#### Harpoon Damage (TypeId=100)
- **方式**: `HarpoonProjectile.BuffedProjectileDamage` getter Postfix
- **公式**: `__result += level * 8`
- CallerCount=1，但作为 getter 可以安全 patch

#### Movement Speed (TypeId=101)
- **方式**: `BuffDataContainer.AddMoveSpeedParam(MOVE_SPEED_PARAM_TID, multiplier)`
- **原理**: `DetermineMoveSpeed()` 内部乘以 `MoveSpeedParameter`（聚合 MoveSpeedParamDic 所有条目），纯数据字典，无 buff 图标
- **参考**: `F:\Repos\SuperDave2.0` 用相同机制
- **关键**: 只在 level 变化时更新（避免每帧重复操作）。禁用时调 `RemoveMoveSpeedParam(tid)`

**失败方案**:
1. 直接写 `m_MoveSpeed` 字段（0x350 偏移）+ Prefix/Postfix restore → 不可靠，DetermineMoveSpeed 被内联，读取时机不可预测
2. 固定值不 restore → 有效但不优雅，且 `m_MoveSpeed` 可能被其他系统读取

#### Booster Speed (TypeId=102)
- **方式**: 每帧从 `SubHelperSpecData.BoosterSpeed` 读取不变的原始值，直接设置 `handler._multiplyMoveSpeed = baseFromSpec * (1 + bonus)`
- **条件**: `handler._multiplyMoveSpeed > 1f`（handler 正在 boost 中）且找到活跃的 Booster/BoosterMk2 slot（`remainTime > 0`）
- **⚠️ 不能用 Prefix/Postfix save/restore**：Postfix 还原后游戏可能还没读取修改后的值（时序问题，同移速一样）
- **⚠️ 不能捕获 `_multiplyMoveSpeed` 当前值作为 base**：handler 短暂 null 或 `_multiplyMoveSpeed` 瞬间 ≤1 时捕获标记重置，重新捕获读到的是已增强的值 → 指数级增长

#### Booster Duration (TypeId=103)
- **方式**: 逐帧检测 `remainTime` 减少量，回补 `drained * bonus / (baseDuration + bonus)`
- **公式**: 使有效消耗速率 = `drained * baseDuration / totalDuration`，总持续时间 = baseDuration + bonus
- **⚠️ 关键**: 分母必须是 `baseDuration + bonus`，不能是 `baseDuration`！否则当 `bonus > baseDuration` 时补偿超过消耗 → 无限续航

### PlayerCharacter 移动架构

```
PlayerCharacter.Update() (CallerCount=0)
  → ApplyMovement()  // 读 m_MoveSpeed[0x1CC] 缩放输入
    → DetermineMoveSpeed()  // 读 [0x350] 作为基础速度, 乘以 MoveSpeedParameter

PlayerCharacter.FixedUpdate() (CallerCount=0)
  → Rigidbody.MovePosition()  // 仅做物理位移
```

两个偏移 0x1CC 和 0x350 在 interop 中都映射到 `m_MoveSpeed` 属性。

### SubHelper（小摩托）系统

- `SubHelperItemInventory.subHelperSlots` (小写 s): `Il2CppReferenceArray<SubHelperSlotData>`
- `SubHelperSlotData.subHelper` → `SubHelperSpecData`（数据）; `.remainTime` → float（剩余电量）
- `SubHelperItemInventory.currentUsingHandler` → `SubHelperHandler`（当前使用中的 handler）
- `SubHelperHandler._multiplyMoveSpeed`: 速度倍率
- `SubHelperHandler._absoluteMoveSpeed`: 绝对速度
- `SubHelperSpecData.BatteryDuration` (CallerCount=12): 基础续航时间
- `SubHelperSpecData.BoosterSpeed`: 基础推进速度
- `CheatEquip` 是空 stub（`ret 0`），需用 `StoreInstanceItem(SubHelperSpecData)` + `ResourceManager._SubHelperSpecDataList` 替代

### MaxLevel 变更与存档降级

降低 `MaxLevel` 后存档中可能有超范围的 TID。通过两阶段 init 模式处理：

| 阶段 | `_initInjected` | `GetSubEquipment` 对 level > MaxLevel 的行为 |
|------|-----------------|----------------------------------------------|
| 存档加载 (Init 内部) | false | 创建对象（让旧存档的超范围 TID 成功加载） |
| Init 完成后 | true | 返回 null（让 `GetSubEquipment(currentTID+1)==null` 的满级判断正确） |

**降级流程** (`SubEquipInit_Patch`)：
1. `_initInjected = true`（先标记，让后续的 GetSubEquipment 正确拒绝超范围）
2. 遍历所有 UpgradeDef，读 `equipped.TID`（原始未 clamp 的值）
3. 如果 `savedLevel > MaxLevel`，调 `AddSubEquip(clampedTID)` + `EquipSubEquip(clampedTID)` 降级

**⚠️ 不能在 `CreateSubEquipment` 中 clamp TID**：
- 会导致 `m_EquipSubEquips` 字典存原始 TID（如 9900110），但对象 TID 变成 9900105
- `SubEquipInit_Patch` 通过 `equipped.TID` 检测降级时读到 clamp 后的值 → 误判不需要降级
- `GetLevel()` 读到 clamp 后的 TID 也可能出界 → 返回 0 → 所有效果失效

**`FindBySubEquipTID` 范围**: 使用 `TID_RANGE = 99`（而非 MaxLevel）确保旧存档 TID 能被识别。

### 自定义图标

`UpgradeDef` 支持两种图标来源（优先级从高到低）：

1. **`IconSpriteName`**：通过 `Resources.FindObjectsOfTypeAll<Sprite>()` 按名称搜索，适用于非 SubEquipment 系统的图标（如 `"Booster_Thumbnail"`）。查找结果缓存在 `_spriteCache` 字典中
2. **`IconSource`** (SubEquipmentType)：从 `SubEquipmentManager.GetSubEquipUIInfo(type).subEquipIcon` 获取，适用于已有 SubEquipment 类型的图标

已知可用的推进器相关 Sprite：
- `Booster_Thumbnail` — 水下摩托缩略图（用于 iDiver 面板）
- `Booster_Thumbnail_32` — 32px 版本
- `Item_Booster` — 物品栏图标
- `Item_Booster_LongRange` — 长距离推进器物品图标
- `Booster_LongRange_Thumbnail` — 长距离推进器缩略图
- `Drone_Motor_Thumbnail` / `Drone_Motor` — 无人机马达图标

### 调试工具

- `DebugCheapUpgrades = true`: 所有升级价格改为 1 金币
- **F4 热键**: 潜水中按下可生成 Booster + BoosterMk2 到 SubHelper 背包
  - 通过 `ResourceManager._SubHelperSpecDataList` 查找 spec → `SubHelperItemInventory.StoreInstanceItem(spec)`

## HarpoonProjectile 关键方法

| 方法 | CallerCount | 说明 |
|------|------------|------|
| `get_BuffedProjectileDamage` | 1 | 最终伤害 getter，安全 Postfix 点 |
| `SetEquipData(int, BuffDebuffEffectData[], SpriteRenderer)` | 2 | 装备时设伤害到 static 字段 |

⚠️ `HarpoonIDamager.OnDoAttack` CallerCount=29514 — **禁止 patch**

## UI 分支：Harpoon 特殊路径

`SubEquipmentManager.IsWeaponType(type)` 仅当 `type == 4` 返回 true。
对于 type==4 的 cell：
- `SetUIData` 额外调 `LobbyUpgradeEquipDetailPanel.InitHarpoonData(uiInfo, state)`
- `ShowDetailPanel` 额外调 `SetHarpoonItemData(type, state)`
- 使用 `harpoonDetailPanel`（而非 `normalDetailPanel`）

非 Harpoon 类型使用标准布局（图标+名称+等级+属性对比面板）。

## 实现踩坑记录（Harpoon Enhancement）

以下是在实现自定义升级项 "鱼叉强化"（SubEquipmentType=100）时遇到的问题及解决方案。

### 1. SingletonNoMono<T> 无 `_instance` 字段

**问题**：`SingletonNoMono<SubEquipmentManager>._instance` 编译错误。

**原因**：`SingletonNoMono<T>` 与 `Singleton<T>` 不同，没有 `_instance` 字段。

**解决**：使用 `SingletonNoMono<T>.s_Instance`（静态字段），或用 `SingletonNoMono<T>.hasInstance` 判断是否存在。

### 2. SpecDataBase 不能用 `new` 或 `ScriptableObject.CreateInstance`

**问题**：`new SpecDataBase()` 编译通过但运行时创建的对象字段写入无效（specData._Damage 始终为 0）。`ScriptableObject.CreateInstance<SpecDataBase>()` 编译失败（需要 Sirenix 引用）。

**原因**：`SpecDataBase` 继承 `SerializedScriptableObject`（Sirenix）→ `ScriptableObject`。用 `new` 创建的 IL2CPP 对象没有正确初始化 Unity 内部状态。

**解决**：添加 `Sirenix.Serialization.dll` 引用，在运行时通过 `Object.Instantiate(existingSpecData)` 从游戏已有的 AlloyHarpoon specData 克隆，再修改 `_Damage` 等字段。

### 3. GetStausInfo 跳过零值条目

**问题**：Level 0 时 UI 显示 "Lv.0 → Lv.0" 和 "0 → 0"，即使下一级的数据是正确的。

**原因**：`SubEquipmentManager.GetStausInfo()` 遍历 `MakeStatusDic` 结果时，**跳过 `currentStatusValue == 0` 的条目**。Level 0 的 damage = 0*8 = 0，因此 SubEquipmentStatusInfo 结构体保持全零。

**解决**：在 `SetSubEquipUIInfo` Postfix 中手动构建 `SubEquipmentStatusInfo`，不依赖 `GetStausInfo`。

### 4. UIDataText.SetText 使用 TextManager 本地化 key

**问题**：`subEquipName` 设为 "鱼叉强化" 后，名字显示为空白。

**原因**：`UIDataText.SetText(string textKey)` 的参数是 **TextManager 本地化 key**（`TextManager.Instance.GetText(textKey)`），不是直接显示文本。传入不存在的 key → 返回空字符串。

**解决**：添加新的 Harmony Patch 在 `LobbyUpgradeEquipScrollCell.SetUIData` Postfix 中直接获取 `TMP_Text` 组件并设置 `text` 属性，绕过 TextManager。需添加 `Unity.TextMeshPro.dll` 引用。

### 4.1 ⛔ UIDataText Refresh 覆盖直接 TMP_Text 赋值

**问题**：Postfix 中直接设置 `TMP_Text.text` 生效了（日志确认 `'伤害' → '推进速度'`），但用户看到的仍然是"伤害"。

**原因**：`UIDataText` 在我们的 Postfix 之后会再次 `Refresh()`（通过 Unity 事件/语言变更回调等触发），重新从 `_textKey` 查 `DataManager.GetText()` 覆盖我们设置的文本。直接设 `TMP_Text.text` 是一次性的、不持久的。

### 4.2 ⛔ DelegateSupport.ConvertDelegate 在 BepInEx 6 IL2CPP 中产生无效委托

**问题**：尝试通过 `UIDataText.SetOverride(OverrideTextFunc, true)` 持久覆盖文本。`OverrideTextFunc` 有 `implicit operator` 从 `System.Func<string, string>` 转换（内部调 `DelegateSupport.ConvertDelegate`）。创建的委托对象非 null，但 UI 显示空白。

**原因**：`DelegateSupport.ConvertDelegate<OverrideTextFunc>(managedFunc)` 创建了一个 IL2CPP 委托对象（非 null），但当 IL2CPP native 代码在 `UIDataText.Element.Refresh()` 中调用该委托时，managed→IL2CPP 回调桥接失败，返回 null/空字符串。这是当前 BepInEx 6 BE + Il2CppInterop 版本的限制。

**尝试过的方案（均失败）**：
1. 直接设 `TMP_Text.text` → 被 Refresh 覆盖
2. `SetText("", func, true)` → 空 textKey 导致不渲染
3. `SetOverride(func)` 不改 textKey → 委托返回空
4. 显式 `DelegateSupport.ConvertDelegate` + GC roots + `SetOverride(il2cppFunc, true)` → 同样空

**最终解决**：**禁用 UIDataText 组件 + 直接设 TMP_Text**。`uiDataText.enabled = false` 阻止 Refresh 机制覆盖文本，然后 `GetComponentInChildren<TMP_Text>().text = value` 持久生效。

**注意**：detail panel 是复用的（同一个 `IDiverItemDetailPanel` 实例切换显示不同升级项），因此需要在 `SetItemDetailData` 的 **Prefix** 中恢复所有 UIDataText 的 `enabled = true`，确保切换到原版升级项时 UIDataText 正常工作，然后在 **Postfix** 中对自定义类型再次禁用。Scroll cell 是独立实例，不需要恢复。

**UIDataText 内部结构**（IsilDump 分析）：
- `UIDataText.Element` 核心字段：`_textKey`(+16), `_ezText`(+24), `_overrideTextFunc`(+32), `_language`(+48)
- `Element.SetText(textKey)`: 设 `_textKey`，**清除** `_overrideTextFunc` 为 null，调 Refresh
- `Element.SetOverride(func)`: 保留 `_textKey`，设 `_overrideTextFunc`，调 Refresh
- `Element.Refresh` 流程：空 textKey + 无 override → 使用 EzText 原始文本；有 override → 调 `overrideFunc(textKey)`；无 override → `ToDataText(textKey)` → `DataManager.GetText(key)`
- `OverrideTextFunc` 是 `UIDataText` 的嵌套类型，有 `implicit operator` 从 `System.Func<string, string>` 转换

### 5. BaseScrollController.m_ScrollCellDataList 命名

**问题**：`__instance.m_CellDataList` 编译错误。

**原因**：cell data 列表字段在基类 `BaseScrollController` 上，名为 `m_ScrollCellDataList`（带 "Scroll" 前缀）。

### 6. CallerCount=0 的 virtual 方法 Patch 风险

**现状**：`AddCellData` 和 `SetUIData` 都是 `protected virtual override`、CallerCount=0。目前 patch 正常工作。

**潜在风险**：按照 IL2CPP virtual 方法 patch 规则，如果有兄弟子类也实现了这些方法，可能导致 trampoline 崩溃。CallerCount=0 意味着仅通过 vtable 调用，影响范围相对有限。但未来游戏更新如果添加新的 scroll panel 子类，可能触发问题。

**缓解**：这两个 Patch 的 Postfix 都以 type check 开头（`if ((int)type != CustomType) return;`），不会影响正常装备类型的显示。如果未来出现崩溃，备选方案是改用 `FindObjectOfType<LobbyEquipUpgradeScrollPanel>()` 在 `SubEquipmentManager.Init` Postfix 中直接操作。
