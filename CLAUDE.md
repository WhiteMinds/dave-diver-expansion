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

# 反编译整个 DLL 到目录（完整项目形式，一个类一个文件）
ilspycmd -p -o /tmp/decompiled "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"
```

### 新功能开发工作流

1. 用 `ilspycmd -l type ... | grep -i keyword` 找到目标类
2. 用 `ilspycmd -t ClassName ...` 查看完整类定义
3. 用 `grep "public unsafe"` 过滤出公开方法签名
4. 确认方法参数类型（`BaseCharacter`、`PlayerCharacter` 等）
5. 编写 `[HarmonyPatch]` + 在 `Plugin.cs` 的 `Load()` 中初始化
6. `dotnet build` → 启动游戏测试 → 查看 `LogOutput.log`

### 已确认的关键游戏类

| 类名 | 作用 | 关键方法 |
|------|------|----------|
| `PlayerCharacter` : `BaseCharacter` | 玩家角色控制器 | `Update()`, `FixedUpdate()`, `Awake()` |
| `FishInteractionBody` | 可交互的鱼 | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)`, `InteractionType` |
| `PickupInstanceItem` | 掉落物品 | `CheckAvailableInteraction(BaseCharacter)`, `SuccessInteract(BaseCharacter)` |
| `InstanceItemChest` | 宝箱 | `SuccessInteract(BaseCharacter)`, `IsOpen` |
| `CrabTrapZone` | 捕蟹笼区域 | `CheckAvailableInteraction(BaseCharacter)`, `SetUpCrabTrap(int)` |
| `InGameManager` : `Singleton<InGameManager>` | 游戏管理器 | `playerCharacter` (获取玩家实例) |
| `Singleton<T>` : `MonoBehaviour` | 单例基类 | `Instance` (静态属性) |

> 所有交互类使用 `OnTriggerEnter2D` 检测玩家碰撞，`SuccessInteract` 触发实际拾取。

## 首次设置

1. 运行 `bash scripts/setup-bepinex.sh` 安装 BepInEx 6 BE 到游戏目录
2. 手动启动游戏一次，等待 BepInEx 生成 interop DLL（`BepInEx/interop/`，约 177 个 DLL）
3. 创建 `GamePath.user.props` 指向游戏安装路径（见 README）
4. `dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj`
5. 安装 `ilspycmd`: `dotnet tool install -g ilspycmd`

## 架构

```
src/DaveDiverExpansion/
├── Plugin.cs          # BepInEx 入口，继承 BasePlugin，初始化 Harmony
├── Features/          # 每个功能一个文件，包含配置定义 + Harmony 补丁
└── Helpers/           # 通用工具（IL2CPP 反射等）
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
- 推荐安装 [BepInExConfigManager](https://github.com/sinai-dev/BepInExConfigManager) 提供游戏内 F5 配置面板
  - 使用 UniverseLib 自建 UI，不依赖 IMGUI（IL2CPP 游戏 IMGUI 经常被 strip）
  - 自动发现所有 `ConfigEntry`，开发者无需额外代码

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
