# DaveDiverExpansion - AI 开发指南

## 项目概述

Dave the Diver 游戏 Mod，基于 BepInEx 6 Bleeding Edge + HarmonyX。
游戏使用 **IL2CPP** 编译（非 Mono），所有游戏类型通过 BepInEx 生成的 interop DLL 访问。

## 技术栈

- C# / .NET Framework 4.8 (`net480`)
- BepInEx 6 Bleeding Edge (IL2CPP), version 6.0.0-be.753
- HarmonyX (运行时方法补丁) + Il2CppInterop (IL2CPP 类型桥接)
- Unity 6000.0.52f1 (Unity 6), URP 渲染管线

## 关键命令

```bash
# 构建（自动部署 DLL 到游戏 BepInEx/plugins/）
dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj

# 查看 BepInEx 日志
cat "<GamePath>/BepInEx/LogOutput.log"

# 反编译游戏类（查看方法签名、字段）
ilspycmd -t ClassName "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"

# 整体反编译到 decompiled/（推荐，一次性，6700+ 文件，方便 Grep 搜索）
ilspycmd -p -o decompiled "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"

# 游戏更新后，同步引用 DLL 到 lib/（供 CI 构建使用）
bash scripts/update-lib.sh
```

## 架构

```
├── .github/workflows/release.yml  # CI: v* tag → 构建 → GitHub Release
├── docs/                          # 详细文档（见下方索引）
├── lib/                           # 引用 DLL（Git LFS）
├── scripts/                       # setup-bepinex.sh, update-lib.sh
└── src/DaveDiverExpansion/
    ├── Plugin.cs                  # BepInEx 入口，Harmony init
    ├── Features/
    │   ├── AutoPickup.cs          # 自动拾取（读取 EntityRegistry）
    │   ├── ConfigUI.cs            # uGUI 配置面板 (F1)
    │   ├── DiveMap.cs             # 潜水地图 HUD (M 键)
    │   └── QuickSceneSwitch.cs    # 快速场景切换 (F2)
    └── Helpers/
        ├── EntityRegistry.cs      # 共享实体注册表 + 生命周期补丁
        ├── I18n.cs                # 国际化 + 语言缓存补丁
        └── Il2CppHelper.cs        # IL2CPP 反射工具
```

- `Plugin.cs` — 入口点，`Load()` 中初始化各功能并调用 `_harmony.PatchAll()`
- `Features/` — 每个功能独立为一个文件，含 `Init(ConfigFile)` + `[HarmonyPatch]` 类
- `Helpers/EntityRegistry` — Harmony 生命周期补丁维护 `AllFish`/`AllItems`/`AllChests` HashSet，供 AutoPickup 和 DiveMap 共享读取。每 2s 通过 `Purge()` 清理已销毁对象

## 文档索引

开发时按需查阅，不必全部加载：

| 文档 | 内容 | 何时查阅 |
|------|------|----------|
| [docs/game-classes.md](docs/game-classes.md) | 游戏类参考表、物品/鱼/宝箱分类、玩家状态锁定、语言系统、场景切换系统 | 开发新 Harmony 补丁、操作游戏实体时 |
| [docs/game-internals.md](docs/game-internals.md) | 反编译技巧、单例模式、场景层级、逆向工具、Burst/Job 限制、暂停菜单系统 | 探索未知游戏类、排查反编译问题时 |
| [docs/ugui-il2cpp-notes.md](docs/ugui-il2cpp-notes.md) | uGUI + IL2CPP 踩坑记录（布局、Dropdown 模板、ClassInjector） | 修改/新增 ConfigUI 面板 UI 时 |
| [docs/divemap-perf.md](docs/divemap-perf.md) | DiveMap 性能优化数据（CPU/GPU profiling） | 优化 DiveMap 性能时 |
| [docs/release-workflow.md](docs/release-workflow.md) | CI/CD、发布流程、NexusMods 上传、Playwright 自动化、DOM 选择器 | 发布新版本时 |
| [docs/assetripper-usage.md](docs/assetripper-usage.md) | AssetRipper headless 用法、游戏翻译数据提取 | 需要提取游戏资源/翻译时 |

## 构建配置

- `Directory.Build.props` — 入 Git，定义框架、引用、构建后自动部署
- `GamePath.user.props` — **不入 Git**，定义 `$(GamePath)` 变量
- 引用 DLL 解析：有 GamePath → 游戏目录；无 GamePath（CI）→ `lib/` 目录
- 新增 interop 引用：在 `Directory.Build.props` 的 `<ItemGroup>` 中添加 `<Reference>`

## IL2CPP 注意事项

- 游戏类型通过 `BepInEx/interop/` DLL 访问，Harmony 补丁目标是 interop 包装方法
- 使用 `Il2CppHelper` 工具类访问私有字段，**不要用 `System.Reflection`**
- `Object.FindObjectsOfType<T>()` 可用于扫描场景游戏对象
- **`Singleton<T>.Instance` 会自动创建实例** — 安全检测用 `Singleton<T>._instance`
- **Sirenix 依赖问题**：部分类型（如 `SABaseFishSystem`）不能直接 `GetComponent<T>()`，需通过 `IL2CPP.GetIl2CppClass()` + `Marshal.ReadIntPtr` 低级 API 访问（详见 [docs/game-classes.md](docs/game-classes.md) § 鱼攻击性检测）

## 配置系统

- 所有配置通过 BepInEx `ConfigFile` 管理，自动生成 `.cfg` 文件
- 内置 uGUI 配置面板（F1 打开），自动发现所有 `ConfigEntry`
- Section 顺序：`ConfigUI` → `QuickSceneSwitch` → `AutoPickup` → `DiveMap` → `Debug`
- 控件类型：`bool` → Toggle，`float`/`int` → Slider，`KeyCode` → "Press any key" 按钮，`enum` → Dropdown
- uGUI 开发踩坑记录：[docs/ugui-il2cpp-notes.md](docs/ugui-il2cpp-notes.md)

## 国际化 (i18n)

- `I18n.T("Enabled")` — 中文返回 `"启用"`，英文返回 `"Enabled"`
- 添加翻译：在 `I18n.cs` 的 `ZhCn` 字典添加 `["EnglishKey"] = "中文值"`
- 语言检测：ConfigEntry 手动设置 > `SeenChinese` 标记 > `Application.systemLanguage`

## 开发原则

- 每个功能独立为一个文件，含 `Init(ConfigFile)` + `[HarmonyPatch]` 类
- 所有配置项通过 `config.Bind(section, key, default, description)` 管理
- 使用 `Plugin.Log` 记录日志（`LogInfo`, `LogWarning`, `LogError`）
- 代码注释用英文，CLAUDE.md 用中文，README.md 用英文，Git commit 用英文

## 新功能开发工作流

1. 确保 `decompiled/` 目录存在（整体反编译）
2. 用 Grep 在 `decompiled/` 中搜索关键类名/方法名
3. 查阅 [docs/game-classes.md](docs/game-classes.md) 确认类型和方法签名
4. 编写 `[HarmonyPatch]` + 在 `Plugin.cs` 的 `Load()` 中初始化
5. `dotnet build` → 启动游戏测试 → 查看 `LogOutput.log`
