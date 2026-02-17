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
| `PlayerCharacter` : `BaseCharacter` | 玩家角色控制器 | `Update()`, `FixedUpdate()`, `Awake()` |
| `FishInteractionBody` | 可交互的鱼 | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `InteractionType` |
| `PickupInstanceItem` | 掉落物品基类（所有可拾取物品） | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `isNeedSwapSetID`, `usePreset`, `GetItemID()` |
| `InstanceItemChest` | 宝箱 | `SuccessInteract(BaseCharacter)`, `IsOpen` |
| `CrabTrapZone` | 捕蟹笼区域 | `CheckAvailableInteraction(BaseCharacter)`, `SetUpCrabTrap(int)` |
| `InGameManager` : `Singleton<InGameManager>` | 游戏管理器 | `playerCharacter` (获取玩家实例) |
| `Singleton<T>` : `MonoBehaviour` | 单例基类 | `Instance` (静态属性) |
| `SingletonNoMono<T>` : `Il2CppSystem.Object` | 非 MonoBehaviour 单例基类 | `Instance` (静态属性) |
| `DR.Save.SaveUserOptions` : `SaveDataBase` | 用户设置（语言、音量、按键等） | `CurrentLanguage`, `CheckLanguage(SystemLanguage)` |
| `DR.Save.Languages` | 游戏语言枚举 | `Chinese=6`, `ChineseTraditional=41`, `English=10` |
| `DataManager` : `Singleton<DataManager>` | 数据管理器 | `GetText(ref string textID)`（静态+实例两种） |
| `FontManager` | 字体管理器 | `GetFont(SystemLanguage)`, `GetFontAsset(SystemLanguage)` |
| `OrthographicCameraManager` : `Singleton<>` | 主相机管理器 | `m_Camera`（主 Camera）。注意：`m_BottomLeftPivot`/`m_TopRightPivot` 在 `CalculateCamerabox()` 前为 (0,0)，不可靠 |
| `Interaction.Escape.EscapePodZone` | 逃生舱区域 | `transform.position`（不移动） |
| `Interaction.Escape.EscapeMirror` | 逃生镜区域 | `transform.position`（不移动） |
| `OxygenArea` | 氧气补充区域 | `minHP`, `chargeTime`, `isCharging`（注意：不是氧气宝箱） |
| `SABaseFishSystem` | 鱼 AI 系统基类 | 所有鱼都有此组件，存储 AI 数据 |
| `DR.AI.AwayFromTarget` : `AIAbility` | 逃跑 AI 能力 | 非攻击性鱼有此组件；攻击性鱼没有 |
| `SAFishData` : `ScriptableObject` | 鱼配置数据 | `AggressionType`（`FishAggressionType` 枚举）。是 ScriptableObject，不在 GameObject 上 |

> 所有交互类使用 `OnTriggerEnter2D` 检测玩家碰撞，`SuccessInteract` 触发实际拾取。

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
- Mod 获取游戏语言的推荐方式：Harmony 补丁 `SaveUserOptions.get_CurrentLanguage` 的 Postfix 缓存返回值

## 首次设置

1. 运行 `bash scripts/setup-bepinex.sh` 安装 BepInEx 6 BE 到游戏目录
2. 手动启动游戏一次，等待 BepInEx 生成 interop DLL（`BepInEx/interop/`，约 177 个 DLL）
3. 创建 `GamePath.user.props` 指向游戏安装路径（见 README）
4. `dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj`
5. 安装 `ilspycmd`: `dotnet tool install -g ilspycmd`

## 架构

```
src/DaveDiverExpansion/
├── Plugin.cs                # BepInEx 入口，继承 BasePlugin，初始化 Harmony
├── Features/
│   ├── AutoPickup.cs        # 自动拾取功能 + Harmony 补丁
│   ├── ConfigUI.cs          # uGUI 游戏内配置面板 (F1 切换)
│   └── DiveMap.cs           # 潜水地图 HUD (M 键切换，Camera→RenderTexture 方案)
└── Helpers/
    ├── I18n.cs              # 国际化工具 + 游戏语言缓存 Harmony 补丁
    └── Il2CppHelper.cs      # IL2CPP 反射工具
```

- `Plugin.cs` — 入口点，`Load()` 中按顺序初始化各功能并调用 `_harmony.PatchAll()`
- `Features/` — 每个功能独立为一个文件，包含:
  - 静态类定义 `ConfigEntry` 绑定 + `Init(ConfigFile)` 方法
  - 同文件内的 `[HarmonyPatch]` 类
- `Helpers/` — 共享工具方法

## 构建配置

- `Directory.Build.props` — 入 Git，定义框架 (`net480`)、BepInEx/Unity/Game 引用、构建后自动部署
- `GamePath.user.props` — **不入 Git**，仅本地，定义 `$(GamePath)` 变量
- `.csproj` — 极简，核心配置继承自 `Directory.Build.props`
- 新增 interop 引用时，在 `Directory.Build.props` 的对应 `<ItemGroup>` 中添加 `<Reference>`

## IL2CPP 注意事项

- 游戏类型通过 `BepInEx/interop/` 下的 DLL 访问，不是原始 C# 程序集
- interop 方法签名带 `unsafe` 关键字，但在 C# 代码中正常调用即可
- 使用 `Il2CppHelper` 工具类访问私有字段（基于反射）
- Harmony 补丁目标是 interop 包装方法，`typeof(ClassName)` 直接引用 interop 类型
- 不要在代码中使用 `System.Reflection` 访问 IL2CPP 类型的私有成员，使用 Il2CppInterop API
- `Object.FindObjectsOfType<T>()` 可用于扫描场景中的游戏对象

## 配置系统

- 所有用户可配置项通过 BepInEx `ConfigFile` 系统管理
- 配置文件自动生成在 `<GamePath>/BepInEx/config/com.davediver.expansion.cfg`
- 内置 **uGUI 配置面板**（`Features/ConfigUI.cs`），按 F1 打开
  - 基于 UnityEngine.UI (uGUI)，纯代码构建，零第三方依赖
  - 使用 `ClassInjector.RegisterTypeInIl2Cpp` 注册 MonoBehaviour，全场景热键检测
  - 自动发现所有 `ConfigEntry`，按 section 分组
  - 控件类型：`bool` → Toggle，`float`/`int` → Slider，`enum` → Dropdown，其余 → InputField
  - 修改立即生效，ConfigFile 自动保存
  - 第三方 ConfigManager 不可用：IMGUI 被 strip，sinai-dev 版有 Unity 6 兼容问题
  - **开发 uGUI 前必读**：[docs/ugui-il2cpp-notes.md](docs/ugui-il2cpp-notes.md)（踩坑记录）

## 潜水地图 (DiveMap)

- `Features/DiveMap.cs` 实现潜水 HUD 小地图和大地图
- **小地图**：右上角，跟随玩家，可配置缩放级别（`MiniMapZoom`）
- **大地图**：按 M 键切换，屏幕中央显示完整关卡
- 技术方案：独立正交 Camera → RenderTexture → RawImage
  - Camera 必须 `CopyFrom(mainCam)` 继承 URP 管线设置
  - 关卡边界从 `InGameManager.GetBoundary()` 获取（备选 `CurrentCameraBounds`）
- **性能优化：扫描与重定位分离**
  - 扫描阶段（`FindObjectsOfType`，每 0.5s）：发现新实体，缓存引用/坐标
  - 重定位阶段（每帧）：从缓存读取坐标，更新 UI 标记位置
  - 逃生点：不移动，进场景只扫描一次
  - 箱子/物品：不移动，缓存 `Vector3` 坐标
  - 鱼：会移动，缓存 `Transform` 引用，每帧读 `position`
- **标记颜色**
  - 玩家：白色 | 逃生点：绿色 | 鱼：蓝色 | 攻击性鱼：红色
  - 物品：黄色 | 宝箱：橙色 | 氧气宝箱（`Chest_O2`）：青色
- **攻击性鱼检测**：`fish.GetComponent<DR.AI.AwayFromTarget>() == null`
  - 普通鱼有 `AwayFromTarget` 组件（遇敌逃跑），攻击性鱼没有
  - 注意 `AwayFromTarget` 在命名空间 `DR.AI` 下

## 国际化 (i18n)

- `Helpers/I18n.cs` 提供轻量翻译系统，仅支持中/英两种语言
- 英文为默认语言（即 key），中文翻译存储在 `ZhCn` 字典中
- 调用方式：`I18n.T("Enabled")` — 中文环境返回 `"启用"`，英文环境返回 `"Enabled"`
- 语言检测优先级：ConfigEntry 手动设置 > Harmony 缓存的游戏语言 > OS 系统语言
- Harmony Postfix 补丁 `SaveUserOptions.get_CurrentLanguage` 自动缓存游戏语言
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
