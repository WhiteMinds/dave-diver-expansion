# 海马赛系统逆向

## 概述

海马赛是 Dave the Diver 娱乐场中的小游戏。玩家控制海马在赛道上奔跑，通过加速、躲避障碍物和使用 Tag 来赢得比赛。

入口场景：`SceneLoader.k_SceneName_MV_Casino_Inside`（娱乐场内部）

## 核心类

| 类名 | 说明 | CallerCount 安全性 |
|------|------|-------------------|
| `SeahorseRacer` | 海马赛跑者（玩家和 AI 共用） | Update: CC=0 ✅ |
| `SeahorseRacer.InputValue` | 输入容器（move, IsTag, OnAccel） | — |
| `SeahorseRacerStateMachine` | 状态机，管理赛跑状态 | CheckMoveInput: CC=1 ❌ |
| `SeahorseRacerValue` | 赛跑者数据（isPlayer 标识玩家） | — |
| `SeahorseRacerGauge` | 加速仪表 | — |
| `SeahorseRacerAI` | AI 赛跑者 | — |
| `SeahorseRacerAIData` | AI 参数（加速冷却、障碍成功率、tag概率） | — |
| `SeahorseRaceTrackObstacle` | 障碍物基类 | OnTriggerEnter: CC=0 ✅, OnFall: CC=0 ✅ |
| `SeahorseRaceTrackObstacle_Hurdle` | 跨栏障碍（需跳跃） | OnFail_Impl: CC=0 但 virtual ⛔ |
| `SeahorseRaceTrackObstacle_Crawl` | 低矮障碍（需蹲下） | — |
| `SeahorseRaceTrackTag` | Tag 加速区域 | — |
| `SeahorseRaceSessionPlay` | 赛事会话 | Start_Impl: CC=0 ✅ |
| `SeahorseRaceTrack` | 赛道，含 `_obstacles`/`_tags` 列表 | — |

## 状态机 (StateName enum)

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Wait | 等待 |
| 1 | Ready | 准备 |
| 2 | Run | 奔跑（基础状态） |
| 3 | MaxRun | 最高速奔跑 |
| 4 | OverRun | 过度加速（减速） |
| 5 | Jump | 跳跃（躲避跨栏） |
| 6 | Crawl | 蹲下（躲避低障碍） |
| 7 | Fail | 撞击失败 |
| 8 | Fall | 坠落 |
| 9 | Finish | 完成 |

## 加速仪表 (Gauge)

- `gauge.gauge`: 当前值 (0-100)
- 区间：0-50=Run, 50-75=**MaxRun**（最快）, 75-100=**OverRun**（减速）
- `inputValue.OnAccel()`: 记录加速时间戳 `_lastAccelTime = Time.unscaledTime`
- `inputValue.IsAccel`: 判断 `_lastAccelTime + accelDuration > Time.unscaledTime`
- 最优策略：保持 gauge 在 75 附近（MaxRun 区间上限），目标值设 76

## 输入系统

- `SeahorseRacer.inputValue` (offset 0xD0) — `InputValue` 对象
- `inputValue.move`: Vector2, W/S 映射到 y 轴
- `inputValue.IsTag`: bool, Tag 按钮
- `inputValue.OnAccel()`: 加速

### 状态转换触发 (CheckMoveInput)

`SeahorseRacerStateMachine.CheckMoveInput()` 在 Run/MaxRun/OverRun 状态的 `OnStateUpdate_Impl` 中被调用：

```
读取 inputValue.move.y
if move.y > 上阈值 → ChangeState(5=Jump)
if move.y < -下阈值 → ChangeState(6=Crawl)
否则 → 不切换
```

## ⛔ 障碍物碰撞机制（关键时序）

### Unity 帧执行顺序

```
1. FixedUpdate → 物理模拟 → OnTriggerEnter/Exit 回调
2. Update → 我们的 Harmony Prefix → 游戏逻辑
```

**关键**：物理回调（OnTriggerEnter）在 Update **之前**执行。

### 碰撞体配置（运行时实测）

- **Racer**: 1 个 Collider, `isTrigger=true`, `bounds.size=(2.0, 2.0, 2.0)`, 半宽=**1.0**
- **Obstacle**: 1 个 Collider, 极薄（~0.2 宽），半宽≈**0.1**
- **Trigger 触发距离**: ≈ **1.1** 单位（= racer半宽 1.0 + obstacle半宽 0.1，中心到中心）

### OnTriggerEnter → OnObstacle 流程

`SeahorseRaceTrackObstacle.OnTriggerEnter(Collider other)`:
1. 获取 `other` 上的 `SeahorseRacer` 组件
2. 调用 `SeahorseRacer.OnObstacle(obstacle, enter=true)`

`SeahorseRacer.OnObstacle(obstacle, enter)` (ISIL 逆向):
1. `enter=false` → 清空 `this.obstacle = null`，返回
2. `enter=true` → 设置 `this.obstacle = obstacle`
3. 获取当前状态 `stateMachine.currentState.stateName`
4. **如果 obstacle.type==0(hurdle)**:
   - 当前状态==Jump(5) → 清空 obstacle 并返回（**安全通过**）
   - 否则 → **ChangeState(7=Fail)**
5. **如果 obstacle.type==1(crawl)**:
   - 当前状态==Crawl(6) → 返回（**安全通过**）
   - 当前状态==Jump(5) → 清空 obstacle 并返回（**安全通过**）
   - 否则 → **ChangeState(7=Fail)**

### ⛔ 时序陷阱

OnObstacle 在 OnTriggerEnter 回调中**立即**检查状态。不等待下一帧。

如果在 OnTriggerEnter 触发时海马不在 Jump/Crawl 状态，**同帧直接进入 Fail**。

因此，自动躲避必须在 trigger 触发 **前至少一帧** 通过 `CheckMoveInput` 完成状态转换。

### 正确的躲避时机计算

```
obstacle trigger 前沿 = collider.bounds.min.x
racer 前沿 = racerPos.x + racerHalfX
触发点 = racer前沿 >= obstacle trigger前沿
需要开始躲避 = racer前沿 >= obstacle trigger前沿 - buffer
```

- **每帧实时读取**障碍物的 `collider.bounds.min.x`（trigger 朝向 racer 的前沿）
- 缓存 racer 的 `collider.bounds.extents.x`（半宽，同一 racer 不变）
- buffer ≈ 0.1 单位（60fps 下约 5 帧余量）

### ⛔ 不要缓存障碍物的 collider.bounds

不同赛道的障碍物 GameObject 是**复用**的——同一批 `SeahorseRaceTrackObstacle` 对象在不同场次出现在完全不同的 x 坐标位置（例如第一场在 x=1912~2063，第三场在 x=1312~1342）。

`SeahorseRaceTrackObstacles.Init(index, type)` 通过遍历子障碍物调用 `obstacle.Init()` 并 `SetActive(bool)` 来激活/禁用，但不会销毁/创建新对象。不同赛道配置决定了哪些障碍物被激活以及它们的位置。

如果在首次扫描时缓存 `collider.bounds.min.x`，后续场次的 `FindObstacleAhead` 会用过期坐标比较，导致完全无法检测到前方障碍物（lookahead 失效）。racer 只能依赖游戏的 `OnTriggerEnter` 回调，但该回调与 Update 同帧，来不及完成状态转换。

此外 `SessionStart_Patch`（hook `SeahorseRaceSessionPlay.Start_Impl`）并非每场比赛都会触发（同一 session 内多场比赛可能不经过此路径），缓存清除时机也不可靠。

**正确做法**：每帧用 `FindObjectsOfType<SeahorseRaceTrackObstacle>()` 获取活跃障碍物，实时读取 `collider.bounds.min.x`。Unity 内部已缓存 physics bounds，80 个障碍物的遍历开销可忽略。

### ⛔ `SeahorseRacer.speed` 不是位移速度

`speed` 属性返回 `gauge.actionSpeed` 或 `gauge.speed`（仪表速度值，数值 5~15+），**不是**每帧位移距离。实测每帧位移约 0.002~0.02 单位。不要用 `speed` 计算 lookahead 距离。

## 障碍物类型

| `type` 值 | 枚举名 | 躲避方式 | 安全状态 |
|-----------|--------|----------|----------|
| 0 | hurdle | `move.y = +1` → Jump | Jump(5) |
| 1 | crawl | `move.y = -1` → Crawl | Crawl(6) 或 Jump(5) |

## Tag 系统 & 团队赛交棒

### Tag 触发流程

1. 进入 tag zone → `SeahorseRacer.trackTag` 被设置
2. Run 状态 `OnStateUpdate_Impl` 检查 `trackTag != null && inputValue.IsTag`
3. 如果都为 true → 调用 `CalcGaugeTransRatio()` 计算进度 → 调用 `SeahorseRaceSessionPlay.OnTag(lane, ratio)`
4. **Tag 检查优先于 CheckMoveInput**（先 tag 后障碍物）

### CalcGaugeTransRatio()

返回 racer 在 tag zone 中的进度比例，**线性增长**（起点→终点 = 0→1）。

### 单人赛 vs 团队赛

`SeahorseRaceSessionPlay.OnTag(lane, ratio)` 内部通过 `lane` 区分：
- **`lane != 4`**: 普通 tag 加速
- **`lane == 4`**: 团队赛交棒，切换到下一个海马

两种模式共享同一个 `IsTag` 输入，不需要额外实现交棒逻辑。

### ⛔ ratio 直接影响速度继承

`SeahorseRaceSession.OnTag` 中的速度继承公式（ISIL 逆向）：

```
newGauge = (oldGauge / oldGaugeMax) × ratio × newGaugeMax
```

**ratio 是直接乘数**：
- ratio ≈ 0 → 几乎不继承速度（Bad）
- ratio ≈ 0.5 → 继承 50%（Good）
- ratio ≈ 0.9+ → 继承 90%+（Perfect）

### 最优策略

等到 `CalcGaugeTransRatio() >= 0.9` 才设 `IsTag = true`，在 tag zone 末端触发以获得 Perfect 评级和最大速度继承。

## AI 系统

`SeahorseRacerAIData` 只有 5 个参数：
- `_runAccelCooltime` / `_maxrunAccelCooltime` / `_overrunAccelCooltime`: 不同状态下的加速冷却
- `_obstacleSuccessRatio`: 障碍物躲避成功率（概率决定）
- `_tagRatio`: Tag 使用概率

AI 也使用 trigger 检测障碍物（不是预测性 lookahead），只是通过 `obstacleSuccessRatio` 概率决定是否"正确"响应。

## 赛道结构

- `SeahorseRaceTrack._obstacles`: `List<SeahorseRaceTrackObstacles>`（按区块分组）
- `SeahorseRaceTrackObstacles._obstacles`: `List<SeahorseRaceTrackObstacle>`（单个障碍物列表）
- `SeahorseRaceTrackObstacles.Init(index, type)`: 遍历子障碍物，调用 `obstacle.Init()` 获取 bool，通过 `SetActive(bool)` 激活/禁用，清除 offset 0x20 字段
- 障碍物沿 x 轴排列（正方向前进），z 轴区分赛道
- 赛道间距：z=5, 7, 9, 11, 13（观测值，取决于赛道类型和 lane 数量）
- 障碍物间距约 10 单位（1313, 1323, 1333, 1343...）
- 场景中约 80 个 `SeahorseRaceTrackObstacle` 对象（`FindObjectsOfType` 返回数），跨赛道/场次复用
- **障碍物复用机制**：同一批 GameObject 在不同场次出现在不同 x 坐标（第一场 x=1912~2063 vs 第三场 x=1312~1342），通过 `Init` 重新配置。详见上方「不要缓存障碍物的 collider.bounds」

## 调试 & 场景切换

### F5 快速跳转

AutoSeahorseRace 内置 F5 热键，可从任意场景（如快艇）直接跳转到娱乐场（`MV_Casino_Inside`），省去手动走到鲛人村再进入娱乐场的步骤：

```csharp
var sceneLoader = Object.FindObjectOfType<SceneLoader>();
sceneLoader.ChangeSceneAsync(SceneLoader.k_SceneName_MV_Casino_Inside, SceneTransitionType.FadeOutIn);
```

跳转后需手动找海马赛 NPC 对话开始比赛。

### ⛔ 不要直接调用 SeahorseRace.Show()

`LocalMonoSingleton<SeahorseRace>` 仅在特定场景中存在。即使通过 `Resources.FindObjectsOfTypeAll` 找到实例并调用 `Show()`，也会因缺少 NPC 对话前置条件而卡在无法操作的状态（类似剧情锁定）。

### 调试用 Harmony 补丁

开发过程中可在 `SeahorseRaceTrackObstacle` 上挂 Postfix 辅助调试：

- **`OnTriggerEnter(Collider)`** (CC=0 ✅)：记录 trigger 触发时 racer 位置、障碍物位置、当前状态。用于校准躲避时机
- **`OnFall(SeahorseRacer)`** (CC=0 ✅)：记录实际撞击发生时的位置。如果玩家被撞说明躲避时机不对
- **⛔ `OnFail_Impl`** (CC=0 但 virtual)：不安全，不要 patch
