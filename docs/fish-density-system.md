# 鱼群密度系统逆向与实现文档

## 核心机制

### FishAllocator 生成流程

```
FishAllocator.OnEnable()         ← streaming 系统激活（玩家接近时）
  └── Spawn(bool force)          ← CallerCount(0), Virtual_New
        └── InstanceCheckRoutine  ← 协程（状态机 MoveNext）
              ├── DoInstanceFishOrGroup(prefab)  ← CallerCount(3), 不可 patch
              │     ├── 如果 prefab 含 BoidGroup → 创建鱼群 → BoidGroup.Start 触发
              │     └── 如果 prefab 是单体鱼 → 直接 Instantiate，无 BoidGroup
              └── IsInstanced = true  ← 在 DoInstanceFishOrGroup 之后设置（ISIL offset 0xAA/170）
```

### 关键时序

`IsInstanced` 在 `DoInstanceFishOrGroup` **之后**才设为 true（ISIL 行 431→454）。
因此当 `BoidGroup.Start` 触发时（在 DoInstanceFishOrGroup 内部），当前 allocator 的 `IsInstanced` 仍为 false。
Boid 路径依赖后续 BoidGroup.Start 事件来"追赶"处理之前变成 instanced 的 allocator。

### 两类鱼的生成差异

| 类型 | 深度范围 | 例子 | BoidGroup.Start 触发 |
|------|----------|------|---------------------|
| Boid 鱼群 | 全深度 | Bluetang, Parrotfish, Catfish | ✅ 触发 |
| 单体鱼 | 主要 130m+ | Clione, Nautilus, Stargazer, Puffer | ❌ 不触发 |

这导致仅依赖 `BoidGroup.Start` 作为触发点时，130m+ 深处的单体鱼无法被加倍。

### 相关游戏类

- **`InGameManager.FishAllocators`** — `List<FishAllocator>` 属性，游戏维护的 allocator 列表。直接读取，无需 `FindObjectsOfType` 搜索。CallerCount(0) getter。
- **`FishAllocator.IsInstanced`** — bool 字段（offset 0xAA），表示 allocator 已完成鱼的生成。
- **`FishAllocator.DoInstanceFishOrGroup(GameObject, Action<FishAISystem>, bool)`** — CallerCount(3)，不可 patch。处理 Boid 鱼群和单体鱼的统一工厂方法。
- **`FishAllocator.FishPrefabOrGroup`** — Default 类型的 prefab 引用。
- **`FishAllocator.GetRandomFishGroup()`** — RandomSelect 类型，返回加权随机选择的 prefab。
- **`FishAllocator.Spawn(bool)`** — CallerCount(0)，Virtual_New。`FishWaveAllocator` 继承 `FishAllocator`，可能 override，patch 有 vtable 风险。
- **`FishAllocator._IsDespawnByDisabled`** — bool，OnDisable 时是否销毁已生成的鱼。
- **`AreaStopFishAlloc`** — 区域触发器，玩家进入时调用 `StopAlloc()` 停止/重置 allocator。
- **`BoidGroup.Start()`** — CallerCount(0)，Unity 消息。通过 UniRx `Observable.FromCoroutine(InitRoutine)` 启动初始化协程。只在首次激活时触发，disable/enable 不再触发。

### 场景结构

Blue Hole 的所有子区域（Blue Hole、Blue Hole Depths、Underwater Lake 等）在**同一个 Unity 场景**中，通过 streaming（AutoActivator）控制 allocator 的 enable/disable。场景内移动不触发 `SceneManager.sceneLoaded`。

进入 Records Room、Control Room 等是**真正的场景切换**（Single mode），allocator 对象被销毁重建（新 instance ID）。Additive 场景（如 `B06_02_02`、`C04_02_01`）加载时包含深处区域的 allocator。

### null prefab Allocator

约 7-10% 的 allocator 的 prefab 为 null（`FishPrefabOrGroup` 返回 null 且 `GetRandomFishGroup()` 也返回 null）。这些 allocator 无法加倍，属于正常现象。

## 实现方案对比

### 方案 A：每帧扫描（推荐 ✅ — 当前实现）

- 在 `PlayerCharacter.Update` 中每帧迭代 `InGameManager.FishAllocators`
- 检查 `IsInstanced && !processedSet.Contains(id)` → 调用 `DoInstanceFishOrGroup`
- 成本：~300 次 bool 读取 + HashSet 查找 ≈ < 0.01ms/帧，可忽略
- 延迟：0（allocator 变 instanced 的下一帧立即处理）
- 完全不依赖 BoidGroup.Start 触发
- 最简单，无多路触发复杂性

### 方案 B：InGameManager.FishAllocators + 0.2s 间隔扫描

- 同方案 A 但降频到每 0.2s 扫描
- 保留 BoidGroup.Start 作为即时路径（Boid 鱼立即处理）
- 非 Boid 鱼有最多 0.2s 延迟
- 适合对每帧开销更敏感的场景

### 方案 C：BoidGroup.Start + FishInteractionBody.Awake 双触发

- BoidGroup.Start 覆盖 Boid 鱼
- FishInteractionBody.Awake（已被 EntityRegistry patch）覆盖所有鱼
- 用 `InGameManager.FishAllocators` 迭代
- 延迟接近 0，但有同样的时序问题（最后一个 allocator 仍需 fallback 扫描兜底）
- 比方案 A 复杂，需要多路触发 + fallback 扫描

## 已确认的关联 Bug

### MoveSpeed 场景切换后丢失

**根因**: `AllEffects_Patch` 在 PlayerCharacter 实例变化时（进出子场景）重置了 trap/drone 跟踪，但漏了 `_lastMoveLevel`。新 PlayerCharacter 的 BuffHandler 上没有 speed buff，但代码认为已应用（`level == _lastMoveLevel`）。

**修复**: 在 `__instance != _lastPlayer` 分支中加 `_lastMoveLevel = -1`。
