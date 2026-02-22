# DaveDiverExpansion - AI 开发指南

## 项目概述

Dave the Diver 游戏 Mod，基于 BepInEx 6 Bleeding Edge + HarmonyX。
游戏使用 **IL2CPP** 编译（非 Mono），所有游戏类型通过 BepInEx 生成的 interop DLL 访问。

## 技术栈

- C# / .NET Framework 4.8 (`net480`)
- BepInEx 6 Bleeding Edge (IL2CPP), version 6.0.0-be.753
- HarmonyX (运行时方法补丁)
- Il2CppInterop (IL2CPP 类型桥接)
- Unity 6000.0.52f1 (Unity 6)
- BepInEx 运行时: .NET 6.0.7（通过兼容层加载 net480 插件）

## 关键命令

```bash
# 构建（自动部署 DLL 到游戏 BepInEx/plugins/）
dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj

# 查看 BepInEx 日志（游戏运行后）
cat "<GamePath>/BepInEx/LogOutput.log"

# 添加新子项目
dotnet new classlib -n <Name> -o src/<Name>
dotnet sln add src/<Name>/<Name>.csproj

# 添加 NuGet 包
dotnet add src/DaveDiverExpansion/DaveDiverExpansion.csproj package <PackageName>

# 安装 BepInEx 到游戏目录
bash scripts/setup-bepinex.sh

# 发布新版本（自动触发 GitHub Actions 构建 + Release）
git tag v0.2.0
git push origin main --tags

# 游戏更新后，同步引用 DLL 到 lib/（供 CI 构建使用）
bash scripts/update-lib.sh
```

## 反编译游戏代码（ilspycmd）

开发新 Harmony 补丁时，需要反编译 interop DLL 查看游戏类结构。
使用 `ilspycmd`（dotnet global tool），无需打开 GUI 反编译器。

```bash
# 安装（首次）
dotnet tool install -g ilspycmd

# 列出所有类型（搜索关键类名）
ilspycmd -l type "<GamePath>/BepInEx/interop/Assembly-CSharp.dll" | grep -i "关键词"

# 反编译指定类（查看方法签名、字段、属性）
ilspycmd -t ClassName "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"

# 只看公开方法列表
ilspycmd -t ClassName "<GamePath>/BepInEx/interop/Assembly-CSharp.dll" | grep "public unsafe void [A-Z]"

# 反编译整个 DLL 到项目 decompiled/ 目录（一次性，6700+ 文件）
ilspycmd -p -o decompiled "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"
```

### 项目级反编译（推荐）

将整个 `Assembly-CSharp.dll` 反编译到项目的 `decompiled/` 目录，方便长期使用 Grep 全文搜索：

```bash
# 在项目根目录运行（只需执行一次，已在 .gitignore 中排除）
ilspycmd -p -o decompiled "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"
```

**注意事项**：
- `ilspycmd -p` 对 IL2CPP interop DLL 会有部分类反编译失败（exit code 70），这是正常的
- 已验证可成功反编译约 6700+ 文件，覆盖绝大多数游戏类
- 部分类型（如 `SaveUserOptions`、`SaveSystemUserOptionManager`）在反编译中会失败
- 对于反编译失败的类，仍可用 `ilspycmd -t ClassName` 单独反编译查看部分信息
- `decompiled/` 已添加到 `.gitignore`，不会提交到仓库

**为何推荐项目级反编译**：
- 逐个类用 `ilspycmd -t` + `grep` 搜索效率极低（每次调用需数秒）
- 反编译到目录后，可直接用 Grep 工具跨 6700+ 文件搜索，瞬间定位引用关系
- 例如：搜索谁引用了某个类型、某个方法的调用者、某个字段在哪里被设置

### 新功能开发工作流

1. 先确保 `decompiled/` 目录存在（`ilspycmd -p -o decompiled ...`）
2. 用 Grep 在 `decompiled/` 中搜索关键类名/方法名，快速定位相关代码
3. 用 `ilspycmd -t ClassName ...` 查看完整类定义（特别是 Grep 找不到的类）
4. 确认方法参数类型（`BaseCharacter`、`PlayerCharacter` 等）
5. 编写 `[HarmonyPatch]` + 在 `Plugin.cs` 的 `Load()` 中初始化
6. `dotnet build` → 启动游戏测试 → 查看 `LogOutput.log`

### 已确认的关键游戏类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `PlayerCharacter` : `BaseCharacter` | 玩家角色控制器 | `Update()`, `FixedUpdate()`, `Awake()`, `IsActionLock`(bool), `IsScenarioPlaying`(bool), `SetActionLock(bool)`, `SetInputLock(bool)` |
| `FishInteractionBody` | 可交互的鱼 | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `InteractionType` |
| `PickupInstanceItem` | 掉落物品基类（所有可拾取物品） | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `isNeedSwapSetID`, `usePreset`, `GetItemID()` |
| `InstanceItemChest` | 宝箱 | `SuccessInteract(BaseCharacter)`, `IsOpen` |
| `CrabTrapZone` | 捕蟹笼区域 | `CheckAvailableInteraction(BaseCharacter)`, `SetUpCrabTrap(int)` |
| `InGameManager` : `Singleton<InGameManager>` | 游戏管理器 | `playerCharacter` (获取玩家实例), `GetBoundary()` (当前子区域边界), `SubBoundsCollection` (所有子区域) |
| `Singleton<T>` : `MonoBehaviour` | 单例基类 | `Instance` (静态属性) |
| `SingletonNoMono<T>` : `Il2CppSystem.Object` | 非 MonoBehaviour 单例基类 | `Instance` (静态属性) |
| `DR.Save.SaveUserOptions` : `SaveDataBase` | 用户设置（语言、音量、按键等） | `CurrentLanguage`, `CheckLanguage(SystemLanguage)` |
| `DR.Save.Languages` | 游戏语言枚举 | `Chinese=6`, `ChineseTraditional=41`, `English=10` |
| `DataManager` : `Singleton<DataManager>` | 数据管理器 | `GetText(ref string textID)`（静态+实例两种） |
| `FontManager` | 字体管理器 | `GetFont(SystemLanguage)`, `GetFontAsset(SystemLanguage)` |
| `OrthographicCameraManager` : `Singleton<>` | 主相机管理器 | `m_Camera`（主 Camera）。注意：`m_BottomLeftPivot`/`m_TopRightPivot` 在 `CalculateCamerabox()` 前为 (0,0)，不可靠 |
| `Interaction.Escape.EscapePodZone` | 逃生舱区域 | `transform.position`，`OnTriggerEnter2D`，标准潜水场景约 9 个 |
| `Interaction.Escape.EscapeMirror` | 逃生镜区域 | `transform.position`，`IsAvaiableUseMirror()`，冰川区域可能有 |
| `OxygenArea` | 氧气补充区域 | `minHP`, `chargeTime`, `isCharging`（注意：不在宝箱上） |
| `SceneLoader` | 场景加载器 | `k_SceneName_MermanVillage`（鲛人村场景名常量） |
| `DR.AI.SABaseFishSystem` : `SpecialAttackerFishAISystem` | 鱼 AI 系统 | 所有鱼都有此组件。`FishAIData`(SAFishData)、`IsAggressive`(virtual bool)。**注意**：继承链依赖 `Sirenix.Serialization`，interop 中缺少 `SerializedScriptableObject` 包装类，**不能直接用 `GetComponent<SABaseFishSystem>()`**，需通过 IL2CPP 低级 API 访问（见下方说明） |
| `DR.AI.AwayFromTarget` : `AIAbility` | 逃跑 AI 能力 | 大多数非攻击性鱼有此组件。**但部分攻击性鱼也有**（如虾、海马、鱿鱼），不能仅凭此判断攻击性 |
| `SAFishData` : `SerializedScriptableObject` | 鱼配置数据 | `AggressionType`(FishAggressionType 枚举)、`PeckingOrder`(int)。通过 `SABaseFishSystem.FishAIData` 访问 |
| `FishInteractionBody` 内部枚举 | 鱼交互类型 | `FishInteractionType`: None=0, Carving=1, Pickup=2, Calldrone=3 |
| `DR.CameraSubBoundsCollection` | 相机子区域集合 | `m_BoundsList`（`List<CameraSubBounds>`）。通过 `InGameManager.SubBoundsCollection` 访问 |
| `DR.CameraSubBounds` | 单个相机子区域 | `Bounds`（Unity `Bounds`，含 center/size/min/max） |
| `Common.Contents.MoveScenePanel` | 场景切换菜单面板 | `OnPlayerEnter(bool)`, `ShowList(bool)`, `OnOpen()`, `OnClick()`, `OnCancel()`, `IsOpened`, `IsEntered` |
| `Common.Contents.MoveSceneTrigger` | 场景切换触发器（碰撞体） | `OnPlayerEnter(bool)`, 引用 `m_Panel`(MoveScenePanel) |
| `Common.Contents.MoveSceneElement` | 场景切换菜单选项 | `sceneName`(枚举: Lobby/SuShi/Farm/FishFarm/SushiBranch/Dredge), `OnClick()`, `IsLocked`, `CheckUnlock()` |
| `LobbyMainCanvasManager` : `Singleton<>` | Lobby 场景 UI 管理器 | `MoveScenePanel`(属性), `OnDREvent(DiveTriggerEvent)` |
| `DiveTrigger` | 潜水出发触发器 | `m_Panel`(LobbyStartGamePanelUI), `OnPlayerEnter(bool)` |
| `LobbyStartGamePanelUI` | 潜水出发面板（长按出发） | `Activate(bool)`, `StartGame(StartParameter)`, `StartGameByMirror(string, SceneConnectLocationID)` |
| `MainCanvasManager` : `Singleton<>` | 游戏内 UI 画布管理器 | `pausePopupPanel`(PausePopupMenuPanel), `quickPausePopup`(QuickPausePopup), `IsQuickPause`(bool) |
| `PausePopupMenuPanel` : `BaseUI` | ESC 暂停菜单面板 | `OnPopup()`, `OnClickContinue()`, `OnClickSettings()`, `ShowReturnLobbyPanel()` 。检测是否打开：`gameObject.activeSelf` |
| `BaseUI` : `MonoBehaviour` | 游戏 UI 面板基类 | `uiDepth` 字段 |

> 所有交互类使用 `OnTriggerEnter2D` 检测玩家碰撞，`SuccessInteract` 触发实际拾取。

### 玩家状态锁定系统

游戏通过多层锁定机制控制玩家操作：

| 属性/系统 | 作用 | 过场动画期间 |
|---|---|---|
| `PlayerCharacter.IsScenarioPlaying` | 脚本剧情（cutscene/scenario）正在播放 | ✅ **实际生效** |
| `PlayerCharacter.IsActionLock` | 动作锁定（对话、Boss 演出等） | ❌ 过场中不一定为 true |
| `DRInput.ActionLock_*` | 输入层锁定（Player/UI/Shooting/InGame） | 底层系统 |
| `InputLockBundlePopup` | 统一锁定管理：`LockPlayer()`, `LockAll()` 等 | 底层系统 |
| `TimelineController` | `LockPlayerControl()` / `UnlockPlayerControl()` | Timeline 专用 |
| `ScenarioManager` | `SetInputLock(bool)` | Scenario 专用 |

**Mod 开发建议**：检查玩家是否可操作时，用 `player.IsActionLock || player.IsScenarioPlaying` 双重判断。实测 `IsScenarioPlaying` 在大部分过场中为 true，`IsActionLock` 不一定。

**AutoPickup 中的应用**：
- **为何 skip**：过场/剧情期间自动拾取会破坏游戏体验（如 Boss 演出中意外拾取物品）
- **为何需要 1s 冷却**：过场结束后（`IsScenarioPlaying` 变 false）如果立即拾取，会导致任务脚本 bug（如马粪海胆任务：剧情要求玩家手动拾取特定物品，过场一结束就自动拾取会跳过任务逻辑）

### PickupInstanceItem 物品系统

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

### 游戏语言系统

- `DR.Save.SaveUserOptions.CurrentLanguage` 返回 `DR.Save.Languages` 枚举
- `SaveUserOptions` 是序列化数据类（非 MonoBehaviour、非单例），需要通过 Harmony 补丁捕获实例或值
- `SaveSystemUserOptionManager` 管理 SaveUserOptions 实例，但该类在 interop DLL 中无法被 ilspycmd 定位
- 游戏广泛使用 `SystemLanguage`（Unity 内置枚举）处理字体、日期等
- **⚠️ `get_CurrentLanguage` Harmony Postfix 会产生大量垃圾值**：IL2CPP 中该 getter 会被无关的 save-data 字段读取触发，返回 999、99999、17000 等无意义整数值。**不能**直接缓存最后一次返回值
- Mod 获取游戏语言的推荐方式：Harmony 补丁 `SaveUserOptions.get_CurrentLanguage` 的 Postfix 中，仅当返回 `Chinese(6)` 或 `ChineseTraditional(41)` 时设置 `SeenChinese=true` 标记（可靠正信号），忽略所有其他值

## 首次设置

1. 运行 `bash scripts/setup-bepinex.sh` 安装 BepInEx 6 BE 到游戏目录
2. 手动启动游戏一次，等待 BepInEx 生成 interop DLL（`BepInEx/interop/`，约 177 个 DLL）
3. 创建 `GamePath.user.props` 指向游戏安装路径（见 README）
4. `dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj`
5. 安装 `ilspycmd`: `dotnet tool install -g ilspycmd`

## 架构

```
├── .github/workflows/release.yml  # CI: v* tag → 构建 → GitHub Release → NexusMods
├── .gitattributes                 # Git LFS 追踪 lib/**/*.dll
├── docs/
│   ├── nexusmods-description.bbcode   # NexusMods 页面描述模板
│   ├── ugui-il2cpp-notes.md          # uGUI 开发踩坑记录
│   ├── game-internals.md             # 游戏内部结构探索笔记
│   └── divemap-perf.md               # DiveMap 性能优化笔记
├── lib/
│   ├── bepinex/                   # BepInEx 核心 DLL（4 文件，Git LFS）
│   └── interop/                   # 游戏 interop DLL（10 文件，Git LFS）
├── scripts/
│   ├── setup-bepinex.sh           # 安装 BepInEx 6 BE
│   └── update-lib.sh              # 从游戏目录更新 lib/ 引用 DLL
└── src/DaveDiverExpansion/
    ├── Plugin.cs                  # BepInEx 入口，继承 BasePlugin，初始化 Harmony
    ├── Features/
    │   ├── AutoPickup.cs          # 自动拾取功能（读取 EntityRegistry）
    │   ├── ConfigUI.cs            # uGUI 游戏内配置面板 (F1 切换)
    │   ├── DiveMap.cs             # 潜水地图 HUD (M 键切换，Camera→RenderTexture 方案)
    │   └── QuickSceneSwitch.cs    # 快速场景切换 (F2 键打开 MoveScenePanel)
    └── Helpers/
        ├── EntityRegistry.cs      # 共享实体注册表 + 生命周期 Harmony 补丁
        ├── I18n.cs                # 国际化工具 + 游戏语言缓存 Harmony 补丁
        └── Il2CppHelper.cs        # IL2CPP 反射工具
```

- `Plugin.cs` — 入口点，`Load()` 中按顺序初始化各功能并调用 `_harmony.PatchAll()`
- `Features/` — 每个功能独立为一个文件，包含:
  - 静态类定义 `ConfigEntry` 绑定 + `Init(ConfigFile)` 方法
  - 功能专属的 `[HarmonyPatch]` 类
- `Helpers/` — 共享工具方法和基础设施补丁
  - `EntityRegistry` — 通过 Harmony 生命周期补丁维护 `AllFish`/`AllItems`/`AllChests` 注册表，供 AutoPickup 和 DiveMap 共享读取
    - Items: `OnEnable` 注册 / `OnDisable` 移除（精确生命周期）
    - Chests: `OnEnable` 注册 / `SuccessInteract` 立即移除（玩家开箱时）+ `Purge()` 清理 null（被销毁时）
    - Fish: `Awake` 注册 / `Purge()` 清理 null（无 OnDisable/OnDestroy 钩子）
    - `Purge()` 每 2 秒运行一次（通过 `PlayerCharacter.Update` Postfix），清除已被 Unity 销毁的对象引用

## 构建配置

- `Directory.Build.props` — 入 Git，定义框架 (`net480`)、BepInEx/Unity/Game 引用、构建后自动部署
- `GamePath.user.props` — **不入 Git**，仅本地，定义 `$(GamePath)` 变量
- `.csproj` — 极简，核心配置继承自 `Directory.Build.props`
- 新增 interop 引用时，在 `Directory.Build.props` 的对应 `<ItemGroup>` 中添加 `<Reference>`

### 引用 DLL 解析优先级

`Directory.Build.props` 使用条件属性组解析引用 DLL 路径：
1. **有 GamePath**（本地开发）：从 `$(GamePath)\BepInEx\core\` 和 `$(GamePath)\BepInEx\interop\` 读取
2. **无 GamePath**（CI 构建）：从项目 `lib/bepinex/` 和 `lib/interop/` 读取

### 更新 lib/ 引用 DLL

游戏更新后，interop DLL 可能变化，需要同步到 `lib/`：

```bash
bash scripts/update-lib.sh   # 从游戏目录复制 14 个 DLL 到 lib/
git add lib/                  # Git LFS 追踪
git commit -m "Update reference DLLs for game version X.Y.Z"
```

## CI/CD

### GitHub Actions Release 工作流

- 配置文件：`.github/workflows/release.yml`
- 触发条件：推送 `v*` 标签（如 `v0.1.0`）
- 运行环境：`windows-latest`
- 步骤：checkout (含 LFS) → dotnet build Release → 打包 zip → 创建 GitHub Release → 上传 NexusMods
- 需要 `permissions: contents: write` 权限（已配置）

### 发布新版本

**完整发布流程**（含 GitHub Release + NexusMods 手动上传）：

```bash
# 1. 更新 Plugin.cs 中的 PLUGIN_VERSION
# 2. 更新 docs/nexusmods-description.bbcode 中的 Changelog
# 3. 更新 README.md Features（如有功能变化）
# 4. 提交更改
git commit -m "Bump version to X.Y.Z"
# 5. 打标签并推送
git tag vX.Y.Z
git push origin main --tags
# 6. 用 gh 检查 CI 状态
gh run list --limit 3
gh run watch <run-id> --exit-status
# 7. CI 自动构建并创建 GitHub Release（含 DaveDiverExpansion-vX.Y.Z.zip）
# 8. 手动上传到 NexusMods（见下方流程）
```

**发布时需更新的文件检查清单**：

| 文件 | 内容 | 必须 |
|------|------|------|
| `src/.../Plugin.cs:37` | `PLUGIN_VERSION = "X.Y.Z"` | ✅ |
| `docs/nexusmods-description.bbcode` | Changelog 区域 | ✅ |
| `README.md` | Features 列表（如有功能变化） | 可选 |

### NexusMods 手动上传

- NexusMods 页面：https://www.nexusmods.com/davethediver/mods/20
- Mod ID: `20` | Game domain: `davethediver`
- CI 的 `Nexus-Mods/upload-action` 目前无法使用（Upload API 处于 evaluation 阶段，账号未被授权）

**手动上传步骤**（通过 Playwright 自动化或浏览器手动操作）：

1. 从 GitHub Release 下载 zip：`gh release download vX.Y.Z --pattern "*.zip"`
2. 打开 NexusMods 文件管理页：`https://www.nexusmods.com/davethediver/mods/edit/?step=files&id=20`
3. 填写表单：
   - File name: `DaveDiverExpansion vX.Y.Z`
   - Version: `X.Y.Z`
   - 勾选 "Update mod version"
   - Category: Main Files
   - File description: 简短版本变更摘要（**限 255 字符以内**，超出会被截断显示在文件列表中）
   - 上传 zip 文件
4. 点击 "Save file"

**更新描述步骤**：

1. 打开 Mod details 编辑页：`https://www.nexusmods.com/davethediver/mods/edit/?step=2&id=20`
2. 描述编辑器使用 WYSIBB 富文本编辑器
3. 点击工具栏最右侧的 `[bbcode]` 按钮切换到 BBCode 源码模式
4. 用 `docs/nexusmods-description.bbcode` 的内容替换整个描述
5. 点击 `[bbcode]` 切回 WYSIWYG 模式（让编辑器同步）
6. 点击 "Save"

**Playwright 自动化**（一键发布脚本 `.tmp/pw-nexusmods-release.js`）：

```bash
# 完整 NexusMods 发布（上传文件 + 更新描述 + 归档旧版本）
# 1. 先启动浏览器（后台常驻）
cd "$SKILL_DIR" && node run.js "F:/Projects/dave-diver-expansion/.tmp/pw-launch.js" &
# 2. 等待 CDP 就绪后执行发布脚本
cd "$SKILL_DIR" && node run.js "F:/Projects/dave-diver-expansion/.tmp/pw-nexusmods-release.js"
```

**基础设施**：
- Playwright Skill 目录：`$SKILL_DIR` = `C:\Users\white\.claude\plugins\cache\playwright-skill\playwright-skill\4.1.0\skills\playwright-skill`
- 执行方式：`cd "$SKILL_DIR" && node run.js "<script-path>"`
- Profile 目录：`.tmp/pw-nexusmods-profile`（已保存 NexusMods 登录状态）
- 启动方式：`pw-launch.js` 启动 Chromium + `--remote-debugging-port=9222`，后续脚本通过 `chromium.connectOverCDP(wsUrl)` 连接
- 离开编辑页面会触发 `beforeunload` 对话框，需注册 `page.on('dialog')` handler
- **绝对不要 `taskkill //IM chrome.exe`**

**NexusMods 页面 DOM 选择器**（已验证，避免反复探索）：

| 页面 | 选择器 | 说明 |
|------|--------|------|
| 文件管理页 `?step=files&id=20` | | |
| 文件名输入 | `input[name="name"]` | 50 字符限制 |
| 版本输入 | `input[name="file-version"]` | 50 字符限制 |
| 更新 mod 版本 | `input#update-version` | checkbox |
| 文件描述 | `textarea[name="brief-overview"]` | **255 字符限制** |
| 文件上传 | `input[type="file"]` | .zip/.7z/.rar/.unrar |
| 上传完成检测 | `input[name="file_uuid"]` 有值 | `waitForFunction` 轮询 |
| 保存按钮 | `button#js-save-file` | |
| 文件条目 | `#file-entry-{fileId}` | `<li>` 元素 |
| Manage 下拉 | `#file-entry-{fileId} .drop-down .btn` | hover 展开子菜单 |
| 归档链接 | `#file-entry-{fileId} a.archive-file` | `data-file-id` 属性 |
| 描述编辑页 `?step=2&id=20` | | |
| BBCode 切换 | `.modesw`（最后一个） | textarea 默认隐藏，需先点此 |
| 描述 textarea | `textarea#mod_description` | BBCode 模式下可见 |
| 保存按钮 | `button[type="submit"].bottom-save` | fallback: 任意可见 Save |
| Media 页 `?step=media&id=20` | | |
| 视频标题 | `input[name="video_title"]` | |
| YouTube URL | `input[name="video_url"]` | 仅支持 YouTube |
| 视频描述 | `textarea#video_description` | |
| 添加视频按钮 | `button#upload_video` | "Add this video" |

**已知文件 ID**：
- v0.1.0: `file_id=152` | v0.2.0: `file_id=153` | v0.3.0: `file_id=154`
- 新上传的文件 ID = 上一个 + 1（规律未确认，以实际页面为准）

### Release zip 结构

```
DaveDiverExpansion-v0.3.0.zip
└── BepInEx/plugins/DaveDiverExpansion/
    └── DaveDiverExpansion.dll
```
用户解压到游戏根目录即可。

### Git LFS

14 个引用 DLL（~56MB）通过 Git LFS 存储在 `lib/` 目录下：
- `lib/bepinex/` — 4 个 BepInEx 核心 DLL（0Harmony, BepInEx.Core, BepInEx.Unity.IL2CPP, Il2CppInterop.Runtime）
- `lib/interop/` — 10 个游戏 interop DLL（Assembly-CSharp, UnityEngine 系列等）
- `.gitattributes` 配置 `lib/**/*.dll filter=lfs diff=lfs merge=lfs -text`
- `.gitignore` 中 `*.dll` 后添加 `!lib/**/*.dll` 例外

## IL2CPP 注意事项

- 游戏类型通过 `BepInEx/interop/` 下的 DLL 访问，不是原始 C# 程序集
- interop 方法签名带 `unsafe` 关键字，但在 C# 代码中正常调用即可
- 使用 `Il2CppHelper` 工具类访问私有字段（基于反射）
- Harmony 补丁目标是 interop 包装方法，`typeof(ClassName)` 直接引用 interop 类型
- 不要在代码中使用 `System.Reflection` 访问 IL2CPP 类型的私有成员，使用 Il2CppInterop API
- `Object.FindObjectsOfType<T>()` 可用于扫描场景中的游戏对象
- **当 interop 类型的继承链依赖第三方库（如 Sirenix）导致 CS0012 编译错误时**：
  - 不能直接用 `GetComponent<T>()` 或 `typeof(T)`，会触发引用链
  - 解决方案：通过 `IL2CPP.GetIl2CppClass()` + `IL2CPP.GetIl2CppField()` + `Marshal.ReadIntPtr/ReadInt32` 直接读取原生字段
  - 遍历 `GetComponents<Component>()` 后用 `c.GetIl2CppType().FullName` 匹配目标类型
  - 实际案例：DiveMap 读取 `SABaseFishSystem.FishAIData.AggressionType`（SAFishData 继承 Sirenix.SerializedScriptableObject，该类型在 interop 中缺失）
  - **注意**：`c.GetType().GetProperty("X")` 对 IL2CPP 类型不工作 — `GetComponents<Component>()` 返回的对象 .NET 类型是 `Component`，不是实际派生类型

## 配置系统

- 所有用户可配置项通过 BepInEx `ConfigFile` 系统管理
- 配置文件自动生成在 `<GamePath>/BepInEx/config/com.davediver.expansion.cfg`
- 内置 **uGUI 配置面板**（`Features/ConfigUI.cs`），按 F1 打开
  - 基于 UnityEngine.UI (uGUI)，纯代码构建，零第三方依赖
  - 使用 `ClassInjector.RegisterTypeInIl2Cpp` 注册 MonoBehaviour，全场景热键检测
  - 自动发现所有 `ConfigEntry`，按自定义顺序分组显示
  - Section 显示顺序：`ConfigUI` → `QuickSceneSwitch` → `AutoPickup` → `DiveMap` → `Debug`（硬编码在 `sectionOrder` 数组中，未列出的 section 追加到末尾）
  - 控件类型：`bool` → Toggle，`float`/`int` → Slider，`KeyCode` → "Press any key" 按钮，`enum` → Dropdown，其余 → InputField
  - **KeyCode 按键绑定**：点击按钮进入监听模式（显示"请按键..."），下一次按键被捕获为新值，ESC 取消。通过 `_listeningEntry` 静态字段追踪监听状态，`ProcessKeyListen()` 在 `CheckToggle()` 中优先于所有热键处理
  - 修改立即生效，ConfigFile 自动保存
  - 第三方 ConfigManager 不可用：IMGUI 被 strip，sinai-dev 版有 Unity 6 兼容问题
  - **开发 uGUI 前必读**：[docs/ugui-il2cpp-notes.md](docs/ugui-il2cpp-notes.md)（踩坑记录）

## 潜水地图 (DiveMap)

- `Features/DiveMap.cs` 实现潜水 HUD 小地图和大地图
- **小地图**：右上角，跟随玩家，固定世界空间视野（`BaseMiniMapOrtho=45`，与关卡大小无关），可通过 `MiniMapZoom` 缩放
- **大地图**：按 M 键切换，屏幕中央显示完整关卡
- 技术方案：独立正交 Camera → **正方形** RenderTexture → RawImage
  - Camera 必须 `CopyFrom(mainCam)` 继承 URP 管线设置
  - Camera 始终 `enabled = false`，通过手动 `Camera.Render()` 控制渲染时机
  - **正方形 RenderTexture**：确保小地图始终 1:1，大地图通过 `uvRect` 裁剪为关卡宽高比
  - 关卡边界：先用 `InGameManager.GetBoundary()` 初始化，再合并 `InGameManager.SubBoundsCollection` 中所有子区域获取完整关卡范围（详见 [docs/game-internals.md](docs/game-internals.md) § 关卡边界）
  - ⚠️ `GetBoundary()` 仅返回当前相机子区域，不是完整关卡。冰河区域等多子区域关卡必须合并 `CameraSubBoundsCollection.m_BoundsList`
- **夜间头灯遮罩处理**
  - 玩家有 3 个子对象 `HeadLightOuter_Deep/Night/Glacier`（~10x10 SpriteRenderer，Default 层，sortingLayer=Player）
  - 这些大面积渐变精灵会在地图上形成黑色遮罩
  - `FindObjectsOfType<CharacterHeadLight>()` 在运行时返回 0（组件不在玩家身上）
  - 解决方案：扫描玩家子对象找 `HeadLightOuter*` 前缀的 Renderer，手动 Render 前禁用、Render 后恢复
  - **不能用 culling mask 排除**：这些精灵在 Default 层（0），排除会隐藏整个场景
- **场景排除与 UI 冲突处理**
  - 鲛人村（`MermanVillage` / `MV_*`）：自带 M 键地图，DiveMap 在此场景自动禁用
  - ESC 暂停菜单打开时：隐藏小地图 + 禁用 M 键切换（通过 `MainCanvasManager.pausePopupPanel.gameObject.activeSelf` 检测）
- **性能优化：EntityRegistry + 扫描与重定位分离**
  - 实体来源：从 `EntityRegistry.AllFish`/`AllItems`/`AllChests` 读取（Harmony 生命周期补丁维护，无 `FindObjectsOfType`）
  - 扫描阶段（每 1s）：遍历注册表，过滤 `!activeInHierarchy`（死鱼/已开箱/禁用物品），缓存引用/坐标
  - 重定位阶段（每帧）：从缓存读取坐标，更新 UI 标记位置（鱼的 Transform 引用也检查 activeInHierarchy）
  - 逃生点：`FindObjectsOfType<EscapePodZone/EscapeMirror>` 扫描一次（静态，不移动）
  - 箱子/物品：不移动，缓存 `Vector3` 坐标
  - 鱼：会移动，缓存 `Transform` 引用，每帧读 `position`
- **逃生点系统**
  - 游戏使用 `Interaction.Escape.EscapePodZone`（逃生舱）和 `Interaction.Escape.EscapeMirror`（逃生镜）
  - 标准潜水场景（`A06_01_02`）有 9 个 EscapePodZone，0 个 EscapeMirror
  - 深海区域同样使用 EscapePodZone（无特殊深海逃生类型）
  - UI 标记池预分配 50 个（场景最多见 9 个，留余量）
- **标记颜色**
  - 玩家：白色 | 逃生点：绿色
  - 普通鱼：蓝色 | 攻击性鱼：红色 | 可捕捉生物（虾/海马）：浅绿色
  - 物品：黄色 | 宝箱：橙色 | 氧气宝箱：青色 | 食材罐：紫红色
- **宝箱 prefab 名称分类**
  - `Chest_O2(Clone)` — 浅海氧气宝箱
  - `Loot_ShellFish004(Clone)` — 深海氧气宝箱（也是 O2）
  - `Chest_Item(Clone)` — 普通物品箱 | `Chest_Weapon(Clone)` — 武器箱
  - `Chest_IngredientPot_A/B/C(Clone)` — 食材罐
  - `Chest_Rock(Clone)` — 岩石箱 | `Quest_Drone_*_Box` — 任务箱
  - `OxygenArea` 组件不在宝箱上（所有宝箱 `hasOxygenArea=False`），不能用于检测 O2 箱
- **鱼攻击性检测（FishAggressionType）**
  - 通过 IL2CPP 低级 API 读取 `SABaseFishSystem.FishAIData.AggressionType` 字段值
  - `FishAggressionType` 枚举：`None=0, OnlyRun=1, Attack=2, Custom=3, Neutral=4, OnlyMoveWaypoint=5`
  - 判定逻辑：`Attack(2)` → 攻击性 | `Custom(3) + 无 AwayFromTarget` → 攻击性 | 其余 → 非攻击性
  - `Custom(3) + 有 AwayFromTarget` → 可捕捉生物（虾、海马等，空格直接捕捉）
  - ⚠️ **不能仅用 `AwayFromTarget == null` 判断**：鱿鱼（SpearSquid, Humboldt_Squid）有 AwayFromTarget 但实为攻击性
  - 已验证的鱼种分类（日志实测）：

    | AggressionType | AwayFromTarget | 鱼种举例 | 地图颜色 |
    |---|---|---|---|
    | Attack | 无 | 鲨鱼、海鳗、狮子鱼、梭鱼、水母 | 红 |
    | Attack | 有 | 长枪乌贼、洪堡鱿鱼 | 红 |
    | Custom | 无 | 河豚、鹦鹉螺 | 红 |
    | Custom | 有 | 白对虾、黑虎虾、海马 | 浅绿 |
    | OnlyRun | 有 | 大多数普通鱼 | 蓝 |
    | OnlyMoveWaypoint | 无 | 金枪鱼（路径移动） | 蓝 |

  - **IL2CPP 低级 API 读取方式**（绕过 Sirenix 类型引用限制）：
    ```csharp
    // 缓存类指针和字段偏移（只需初始化一次）
    var classPtr = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "DR.AI", "SABaseFishSystem");
    var fieldPtr = IL2CPP.GetIl2CppField(classPtr, "FishAIData");
    var dataClassPtr = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "SAFishData");
    var aggrFieldPtr = IL2CPP.GetIl2CppField(dataClassPtr, "AggressionType");
    // 读取：遍历 GetComponents<Component>() 找到 SABaseFishSystem（通过 GetIl2CppType().FullName 匹配）
    // 然后用 Marshal.ReadIntPtr / Marshal.ReadInt32 + il2cpp_field_get_offset 读取字段值
    ```
  - 此技术可推广到任何因第三方依赖（如 Sirenix）导致无法直接引用的 interop 类型
- **调试日志**（`Debug` section 的 `DebugLog` ConfigEntry）
  - EntityRegistry: 实体注册/移除事件（Item+/-, Chest+/-, Fish+, Purge 统计）
  - DiveMap scan: 每次扫描的实体计数统计
  - Fish: 每种鱼首次出现时输出 AggressionType、AwayFromTarget、InteractionType
  - 宝箱: 每种宝箱首次出现时输出 name、IsOpen、activeInHierarchy
- Config: Enabled, ToggleKey(M), ShowEscapePods, ShowFish, ShowItems, ShowChests, MapSize, MapOpacity, MiniMapZoom

## 快速场景切换 (QuickSceneSwitch)

- `Features/QuickSceneSwitch.cs` — 按 F2（可配置）直接打开场景切换菜单，无需走到出口触发器
- 原理：`FindObjectOfType<MoveScenePanel>()` 找到当前场景的面板，调用 `OnPlayerEnter(true)` + `ShowList(true)` 打开
- 关闭时调用 `OnCancel()` + `OnPlayerEnter(false)` 清除触发区状态
- **关键踩坑**：`OnPlayerEnter(true)` 模拟玩家进入触发区，如果关闭面板后不调 `OnPlayerEnter(false)`，空格键焦点会残留在面板按钮上，导致空格重新打开面板
- 通过 `_openedByUs` 标记追踪状态，每帧检测面板是否已被原生 ESC 关闭，自动补调 `OnPlayerEnter(false)` 清理
- Config: Enabled, ToggleKey(F2)

### 游戏场景切换系统

Lobby/寿司店/农场等非潜水场景之间的切换通过以下体系实现：

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

- 每个非潜水场景有各自的 `MoveScenePanel` 实例（Lobby 通过 `LobbyMainCanvasManager.Instance.MoveScenePanel` 访问，寿司店通过 `SushiBarMainCanvasManager` 等）
- `MoveSceneTrigger` 是碰撞体触发器，玩家走入时调用 `OnPlayerEnter(true)`，走出时调用 `OnPlayerEnter(false)`
- 潜水出发是另一个系统：`DiveTrigger` → `LobbyStartGamePanelUI`（长按出发，`StartGame(StartParameter)`）

## 国际化 (i18n)

- `Helpers/I18n.cs` 提供轻量翻译系统，仅支持中/英两种语言
- 英文为默认语言（即 key），中文翻译存储在 `ZhCn` 字典中
- 调用方式：`I18n.T("Enabled")` — 中文环境返回 `"启用"`，英文环境返回 `"Enabled"`
- 语言检测优先级：ConfigEntry 手动设置 > `SeenChinese` 标记 > `Application.systemLanguage` 回退
- Harmony Postfix 补丁 `SaveUserOptions.get_CurrentLanguage`：仅在检测到 Chinese/ChineseTraditional 时设置 `SeenChinese=true`（见「游戏语言系统」中关于垃圾值的说明）
- **添加新功能翻译**：在 `I18n.cs` 的 `ZhCn` 字典中添加 `["EnglishKey"] = "中文值"` 条目
- ConfigUI 面板每次打开时重新渲染，语言切换立即生效

## 开发原则

- 每个功能独立为一个文件，包含 `Init(ConfigFile)` 方法 + `[HarmonyPatch]` 类
- 所有用户可配置项通过 BepInEx `ConfigFile` 系统管理（`config.Bind(section, key, default, description)`）
- 使用 `Plugin.Log` 记录日志（`LogInfo`, `LogWarning`, `LogError`）
- 代码注释用英文，文档 CLAUDE.md 用中文，README.md 用英文
- Git commit message 用英文

## 游戏信息

- 游戏目录: 通过 `GamePath.user.props` 配置
- 游戏 EXE: `DaveTheDiver.exe`
- 游戏编译: IL2CPP（`GameAssembly.dll` 101MB）
- 关键 interop DLL: `BepInEx/interop/Assembly-CSharp.dll`
- 参考 mod: [devopsdinosaur/dave-the-diver-mods](https://github.com/devopsdinosaur/dave-the-diver-mods)
  - 使用 net480 + 直接 DLL 引用，已验证可用
  - 包含自动拾取、速度修改等功能的实现参考
