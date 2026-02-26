# Godzilla DLC 结构分析

## 架构概述

DLC 采用 **代码预埋 + 资源分发** 模式：
- **代码逻辑**：全部编译在游戏本体 `GameAssembly.dll` 中（反编译 `Assembly-CSharp.dll` 即可看到所有 Godzilla 类）
- **DLC 安装**：仅下载 5 个 AssetBundle 到 `DLC/StandaloneWindows64/Godzilla/`（共 ~73MB）
- **解锁检查**：`DLCUtils.CheckOwnDLC(DLC_GODZILLA_TID)` 通过 Steam API 验证拥有权

## DLC 标识

- `DRGameMode.DLCName.Godzilla` = 3
- `DLCUtils.DLC_GODZILLA_TID` — Steam DLC App ID
- `ContentsList`: `DLC_Godzilla = 17001`, `DLC_GodzillaFight = 17002`, `DLC_GodzillaCodex = 17003`
- `ItemType.GodzillaFigure` = 22
- `CustomerSubType.Godzilla` — 寿司店哥斯拉 NPC 客人

## 场景结构

| SceneType | ID | 场景名 | 说明 |
|-----------|-----|--------|------|
| `godzilla_intermission` | 81 | Godzilla 过场入口 | 主 intermission |
| `godzilla_intermission_lobbyFight` | 82 | Godzilla_Lobby_Fight | 大厅战斗 |
| `godzilla_intermission_sushiBar` | 83 | Godzilla_SushiBar | 寿司店 |
| `godzilla_intermission_level` | 84 | 水下关卡 | 主游戏关卡 |

## 核心类

### 场景行为（namespace `Godzilla`）

| 类 | 说明 |
|----|------|
| `GodzillaLobbySceneBehaviour` | 大厅/入口区域 |
| `GodzillaSushibarSceneBehaviour` | 寿司店场景 |
| `GodzillaIngameSceneBehaviour` | 主游戏关卡（潜艇 Timeline 控制） |
| `GodzillaChasingSceneBehaviour` | 追逐战序列 |
| `GodzillaLobbyFightSceneBehaviour` | 战斗场景（含开战/结束 Timeline） |
| `GodzillaBossEbirahSceneBehaviour` | Ebirah Boss 战 |

### Intermission 系统

| 类 | 说明 |
|----|------|
| `GodzillaIntermissionBase` | 抽象基类（场景加载/卸载、fade in/out） |
| `IGodzillaIntermission` | 过场控制接口 |
| `GodzillaStartIntermission` | 开场剧情 → 加载 `Godzilla_Lobby` |
| `GodzillaFightIntermission` | 战斗序列：寿司店 → 大厅 → 追逐战 → 大厅战斗 |
| `GodzillaIntermissionCanvas` | 过场 UI 画布 |

### 战斗系统

| 类 | 说明 |
|----|------|
| `CaveGodzilla` | 洞穴哥斯拉 Boss（命中计数 `m_HitCountMax`/`m_CurHitCount`、Buff、攻击数据） |
| `CaveGodzillaSceneManager` | 洞穴场景管理 |
| `EbirahBattleSubmarine` | Ebirah Boss 战专用潜艇 |
| `GodzillaSubmarineController` | 主潜艇控制（`PlayerCharacter.submarineController`） |
| `GodzillaSubmarineBarricadeTrigger` | 路障碰撞（带旋转动画） |
| `GodzillaDroppedMineEvent` | 战斗掉落水雷事件 |
| `GiantMonsterInputHandler` | 巨兽战斗输入处理 |

### 输入系统（namespace `DRInput`）

| 类 | 说明 |
|----|------|
| `Handler_Godzilla` | 主输入处理 |
| `DirectHandler_Godzilla` | 直接按键输入 |
| `ActionHandler_Godzilla` | Action 输入 |
| `PhaseHandler_Godzilla` | 阶段性输入变化 |
| `IOnDirectHandler_Godzilla` | 输入接口 |

### 收集品

| 类 | 说明 |
|----|------|
| `SpawnerChestItem_GodzillaFigure` | 手办宝箱生成器（Addressable） |
| `PickupInstanceItem_GodzillaFigure` | 手办拾取处理（触发对话） |
| `GodzillaAppGridPanel` | 手办图鉴 Grid 视图 |
| `GodzillaAppDetailPanel` | 手办详情（名称、描述、所属电影、上映年份、大小、音效） |
| `GodzillaAppGridRowCellView` / `ColCellView` | Grid 单元格 |

### 存档数据

`SaveData.SaveDataGodzilla` — 嵌套类，存储手办收集进度：
- 字段：`m_Figures` — `Dictionary<int, bool>`（key = Figure TID，value = 是否已查看/非 New）
- 访问：`SaveData.GodzillaData`（属性）
- API：`HasFigure(tid)` / `AddNewFigure(tid)` / `GetFiguresCount()` / `IsNew(tid)` / `CheckNewFigure(tid)` / `HasNewFigure()` / `Clear()`

## 手办数据表

数据来源：`DR_GameData_Item.json` 的 `GodzillaFigure` section。
Figure TID 范围 **1030001–1030020**，对应物品 ItemDataID **1010301–1010320**。

| # | Figure TID | ItemDataID | 怪兽 | 电影年份 | Size |
|---|-----------|-----------|------|---------|------|
| 1 | 1030001 | 1010301 | Godzilla | 1994 | 100 |
| 2 | 1030002 | 1010302 | Ebirah | 1966 | 50 |
| 3 | 1030003 | 1010303 | MechaGodzilla | 1975 | 50 |
| 4 | 1030004 | 1010304 | Jet Jaguar | 1973 | 50 |
| 5 | 1030005 | 1010305 | King Ghidorah | 1991 | 150 |
| 6 | 1030006 | 1010306 | Gigan | 1972 | 65 |
| 7 | 1030007 | 1010307 | Minilla | 1967 | 13 |
| 8 | 1030008 | 1010308 | Burning Godzilla | 1995 | 100 |
| 9 | 1030009 | 1010309 | Mothra | 1961 | 60 |
| 10 | 1030010 | 1010310 | Hedorah | 1971 | 60 |
| 11 | 1030011 | 1010311 | Biollante | 1989 | 120 |
| 12 | 1030012 | 1010312 | Rodan | 1993 | 70 |
| 13 | 1030013 | 1010313 | Shin Godzilla | 2016 | 112 |
| 14 | 1030014 | 1010314 | Space Godzilla | 1994 | 120 |
| 15 | 1030015 | 1010315 | Destoroyah | 1995 | 120 |
| 16 | 1030016 | 1010316 | Godzilla (1965) | 1965 | 50 |
| 17 | 1030017 | 1010317 | Mecha King Ghidorah | 1991 | 150 |
| 18 | 1030018 | 1010318 | Anguirus | 2004 | 90 |
| 19 | 1030019 | 1010319 | King Caesar | 1974 | 50 |
| 20 | 1030020 | 1010320 | Little Godzilla | 1994 | 30 |

### UI

| 类 | 说明 |
|----|------|
| `MissionRadarPanelGodzilla` | 雷达面板（距离/角度追踪、标记缩放、刷新冷却） |
| `MissionTargetRadarGodzilla` | 任务目标追踪（条件触发） |
| `TimelineControllerImplGodzilla` | 哥斯拉专用 Timeline 控制 |

### 本地化

| 类 | 说明 |
|----|------|
| `DLC_GodzillaText` | ScriptableObject 文本容器 |
| `DLC_GodzillaTextData` | 文本条目（name/english/korean/japanese/chinese/chinesetraditional/french/italian/germany/spanish） |

## AssetBundle 内容

5 个 bundle（`DLC/StandaloneWindows64/Godzilla/`），解包后 ~731MB：

| 资源类型 | 数量 | 解包大小 | 内容 |
|----------|------|----------|------|
| Scenes | 6 | 325M | 完整场景（大厅、寿司店、追逐、Boss 战等） |
| AnimationClip | 292 | 229M | Ebirah 战斗/追逐动画、Bancho 2D 动画、潜艇动画 |
| Texture2D | 257 | 26M | 角色贴图、UI、Boss、场景 |
| AudioClip | 75 | 32M | Ebirah 战斗音效、Timeline 过场音乐 |
| Mesh | 66 | 5.4M | Ebirah 石球、追逐关卡 3D 地形 |
| Prefab | 140 | 34M | 角色/物件预制体 |
| MonoBehaviour | 2141 | 11M | Timeline 轨道配置数据（Activation Track 等） |
| Material | 86 | 538K | 材质 |
| Sprite | 332 | 2.8M | 2D 精灵 |

### 关键贴图资产

| 贴图前缀 | 说明 |
|----------|------|
| `01_Bancho_*` / `02_Bancho_*` | 番长（Bancho）2D 动画帧 |
| `01_Fish_*` / `01_Sub_*` / `01_Hand_*` | 鱼/潜艇/手部 2D 帧 |
| `02_Ebira_*` / `Godzila_Ebira_*` | Ebirah 身体部件（头/触角/左右钳/身体+遮罩） |
| `03_Godzilla_*` | 哥斯拉贴图（9 张） |
| `03_Eel_*` | 鳗鱼贴图 |
| `Godzilla_Boss_Ebira_*` | Boss 战场景物件（木板/铁/绳索/石球） |
| `Godzilla_Heisei_01_Head` | 平成哥斯拉头部 |
| `UI_GiantMonsterBattle_*` | 巨兽对战 UI（血条、VS、角色立绘、攻击图标） |
| `UI_MonsterFight_Introduce_*` | 怪兽出场介绍画面 |
| `UI_Submarine_*` | 潜艇 HUD（血条、武器、雷达、修理） |
| `UI_godzilla_Radar_*` | 哥斯拉雷达 UI |
| `sushibar_miki_*` | Miki NPC 寿司店 2D 帧 |
| `Chest_Godzilla_*` | 手办宝箱（开/关） |
| `BoatDeco_024_Godzilla` | 船装饰 |
| `Boss_Ebira_Artwork` | Ebirah Boss 立绘 |

## 手办收集详细流程（IsilDump 逆向确认）

### 宝箱生成：`SpawnerChestItem_GodzillaFigure.Start()`

1. 检查 `DRGameMode.IsDLCInstalled(3)` — DLC 未安装则跳过
2. 获取 `ChestDropList`（TID `3080001`）
3. 检查 `SaveData.HaveBeenLooted(itemTID)` — **已拾取则 `SetActive(false)` 隐藏宝箱**
4. 检查 `CheckAlreadyUsedInteractionItem(uid)` — 当次潜水是否已开过
5. 调用 `SpawnerChestItem.Start()` → `SpawnerBase.Start()` → `TrySpawnObject()` 生成可交互宝箱

### 开箱：`InstanceItemChest.SuccessInteract(player)`

1. 调用 `ISpawnObjectHandler.HandleSpawnObject` — 生成 `PickupInstanceItem_GodzillaFigure` 到地面
2. 播放开箱动画/音效
3. 调用 `OnOpenStartChestItem` → `StoreUseInteractionItemUID(uid)` — 标记宝箱已开
4. **不调用** `AddLootingSaveData` 或 `AddNewFigure`（这些只在手办被拾取时触发）

### 拾取手办：`PickupInstanceItem_GodzillaFigure.SuccessInteract(player)`

1. `player.vtable[0x218](this)` — 基类角色交互回调
2. `UnityEvent.Invoke` — 拾取事件回调
3. 设置 `ActiveFigureItem` 静态字段（供对话框显示手办名称）
4. `ScenarioManager.StartScenario(dialogueBundleID, ...)` — 启动对话（纯展示）
5. **`SaveData.AddLootingSaveData(itemTID, true)`** — 标记物品已拾取
6. `GodzillaItemFigureMap.TryGetValue(itemTID) → figureTID` — 查找手办 TID
7. `DataManager.GetGodzillaFigure(figureTID)` — 获取手办数据
8. **`SaveDataGodzilla.AddNewFigure(figureTID)`** — 注册手办到存档

关键点：所有存档写入发生在 `SuccessInteract` 内部，**不依赖对话完成**。

### 手办拾取初始化：`SetItemPickUpData(itemId)`

调用 `SaveData.HaveBeenLooted(itemTID)` 检查是否已拾取，若已拾取则 `SetActive(false)` 隐藏。

### 存档数据三处关联

手办收集涉及 **三个独立的存档数据结构**，修改存档时必须同时修改三处（已验证）：

| 存档位置 | key | 写入时机 | 用途 |
|----------|-----|---------|------|
| `Godzilla.figures` | Figure TID (如 `1030001`) → bool | `AddNewFigure` | 图鉴显示、`HasFigure`/`GetFiguresCount` |
| `Looting` | Item ID (如 `1010301`) → `{lootID, isNew}` | `AddLootingSaveData` | 控制宝箱是否重新生成 (`HaveBeenLooted`) |
| `AchieveUserData.AchieveKeyToCount` | `RootingItem_<ItemID>` → count | 基类交互回调 | 成就/统计计数 |

修改存档时**必须同时修改这三处**，否则游戏报存档错误。

### 手办收集任务

- Mission ID: `10011003`，Task ID: `10051010`
- `missionState: 2` = 进行中，`4` = 已完成
- `nowCounts` / `savedCounts` 记录进度（如 `[5]` 表示 5/20）
- 任务计数器为**事件驱动**，仅在拾取时递增，不轮询 `GetFiguresCount()`。实测存档中 figures 有 19 条但任务计数为 5，确认 14 个手办在任务被接受前收集

### `GlobalSignal.EType.NewGodzillaFigure`

存在枚举值定义，但在反编译代码中**未发现任何发送此信号的调用**。任务计数的触发机制可能在基类交互回调 (`player.vtable[0x218]`) 或 `AddLootingSaveData` 内部。

## 存档编辑注意事项

- **⛔ 绝对不能** 用 `JSON.parse()` / `JSON.stringify()` 处理存档 JSON — 大整数（如 `LastUpdateTime: 639076411699703820`）会丢失精度，导致游戏报"存档文件错误"
- 必须用**纯文本操作**编辑 pretty-printed JSON，保持原始字符不变
- 删除条目后需检查尾逗号，但**不要用全局正则** `text.replace(/,(\s*[}\]])/g, ...)` — 会破坏字符串内容。只在被修改的 section 内做局部修复
- 游戏**没有 checksum/hash 校验** — 存档错误纯粹是 JSON 反序列化失败（Newtonsoft.Json 异常）
- 不需要同步修改 PZ 文件

### 已验证的手办重置流程

同时修改以下四处，可成功重置手办收集进度（已验证存档可加载）：

1. **`Godzilla.figures`** → 清空为 `{}`
2. **`Looting`** → 移除 item ID `1010301`–`1010320` 的条目（共 19 条，部分可能不存在）
3. **`AchieveUserData.AchieveKeyToCount`** → 移除 `"RootingItem_1010301"` 至 `"RootingItem_1010320"` 的条目
4. **Mission `10011003`** → `nowCounts` 和 `savedCounts` 重置为 `[0]`

工具：用 `tools/save-codec/decode.mjs` 解码/编码，用纯文本脚本修改 JSON（参考 `.tmp/full_edit.mjs`）。

## 游戏流程推测

1. **开场** (`GodzillaStartIntermission`) → 加载大厅 (`Godzilla_Lobby`)
2. **寿司店** → Miki NPC 交互、剧情对话
3. **追逐战** (`C00_02_01_GodzillaChasing`) → 潜艇躲避 Ebirah 攻击（QTE + 障碍物）
4. **大厅战斗** (`Godzilla_Lobby_Fight`) → 巨兽对战 UI（哥斯拉 vs Ebirah 格斗）
5. **Boss 战** → 潜艇 vs Ebirah（鱼雷攻击、石球攻击、Groggy/Parrying 机制）
6. **洞穴哥斯拉** (`CaveGodzilla`) → 命中计数 + Buff 机制
7. **手办收集** → 场景中开宝箱获得哥斯拉手办，手机 App 图鉴查看

## 解包命令

```bash
# 用 AssetRipper 解包 Godzilla DLC bundles
"F:/Tools/CSharp/AssetRipper_1.3.10/AssetRipper.GUI.Free.exe" --headless --port 8889 &
sleep 6
curl -X POST http://localhost:8889/LoadFolder \
  -d 'Path=F:\SteamLibrary\steamapps\common\Dave the Diver\DLC\StandaloneWindows64\Godzilla'
curl --max-time 300 -X POST http://localhost:8889/Export/UnityProject \
  -d 'Path=F:\Projects\dave-diver-expansion\.tmp\godzilla-ripped'
# 产物在 .tmp/godzilla-ripped/ExportedProject/Assets/
```
