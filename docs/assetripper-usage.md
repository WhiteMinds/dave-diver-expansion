# AssetRipper Usage Guide

AssetRipper 用于从 Unity AssetBundle 中提取游戏资源（TextAsset、纹理、音频等）。
本文档记录 headless 模式的自动化用法，供 AI 辅助开发时使用。

- 源码：`F:\Repos\AssetRipper`
- 本地安装：`F:\Tools\CSharp\AssetRipper_1.3.10\AssetRipper.GUI.Free.exe`
- 版本：1.3.10
- GitHub：https://github.com/AssetRipper/AssetRipper

## 架构

AssetRipper 不是传统 CLI，而是一个 **ASP.NET Core Web 应用**：
1. 启动时加载游戏数据 → 启动 HTTP 服务器
2. 通过 HTTP POST/GET 端点执行加载、导出操作
3. `--headless` 模式下不打开浏览器，适合脚本/AI 自动化

## 命令行参数

```bash
AssetRipper.GUI.Free.exe [options]

Options:
  --port <int>                  # 端口号（默认 0 = 随机端口）
  --log <bool>                  # 是否记录日志（默认 true）
  --log-path <string>           # 日志文件路径（默认自动生成）
  --local-web-file <string[]>   # 用本地文件替换在线资源
  --headless                    # 不自动打开浏览器（自动化必用）
```

## HTTP API 端点

### 核心操作

| 方法 | 端点 | 参数 | 说明 |
|------|------|------|------|
| POST | `/LoadFile` | `Path=<文件路径>` | 加载单个文件 |
| POST | `/LoadFolder` | `Path=<文件夹路径>` | 加载文件夹（如游戏 Data 目录） |
| POST | `/Export/UnityProject` | `Path=<输出路径>` | 导出为完整 Unity 项目（**推荐**） |
| POST | `/Export/PrimaryContent` | `Path=<输出路径>` | 只导出原始资源 |
| POST | `/Reset` | 无 | 清空已加载数据 |

所有 POST 端点使用 `application/x-www-form-urlencoded` 格式。
可选参数 `CreateSubfolder=true` 会自动在输出路径下创建带时间戳的子目录。
请求在操作完成前会阻塞，返回 302 重定向表示成功。

### 浏览资源

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/` | 首页 |
| GET | `/Commands` | 操作面板 |
| GET | `/Settings/Edit` | 编辑导出设置 |
| POST | `/Settings/Update` | 保存设置 |

### 资源查看

| 端点模式 | 说明 |
|----------|------|
| `/Assets/View?path=...` | 查看资源详情 |
| `/Assets/Json?path=...` | 获取资源 JSON |
| `/Assets/Text?path=...` | 获取文本资源内容 |
| `/Assets/Image?path=...` | 获取图片数据 |
| `/Assets/Audio?path=...` | 获取音频数据 |
| `/Bundles/View?path=...` | 查看 Bundle |
| `/Search/View?query=...` | 搜索资源 |

## Headless 自动化工作流（已验证）

### 完整流程

```bash
# 1. 启动 AssetRipper（headless，固定端口）
"F:/Tools/CSharp/AssetRipper_1.3.10/AssetRipper.GUI.Free.exe" --headless --port 8888 &
sleep 5

# 2. 加载游戏数据目录（阻塞，约 1-5 分钟）
curl -X POST http://localhost:8888/LoadFolder \
  -d "Path=F:\SteamLibrary\steamapps\common\Dave the Diver\DaveTheDiver_Data"

# 3. 导出为 Unity 项目（阻塞，约 3-10 分钟）
curl -X POST http://localhost:8888/Export/UnityProject \
  -d "Path=F:\Projects\dave-diver-expansion\ripped\UnityProject"

# 4. 清理
curl -X POST http://localhost:8888/Reset
```

### 导出模式对比

| 模式 | 端点 | 输出内容 | 适用场景 |
|------|------|----------|----------|
| **Unity Project** | `/Export/UnityProject` | 完整 Unity 项目结构 | **推荐**：包含所有翻译文本的 YAML |
| Primary Content | `/Export/PrimaryContent` | 原始资源文件 | 仅提取 GameDataSheet JSON（不含翻译） |

> **重要发现**：游戏翻译文本存储在 BSON 格式的 TextAsset 中（bundle `64d2459d...`），
> Primary Content 导出只输出 `.bytes` 二进制，无法直接读取。
> 必须使用 **Unity Project 导出**，AssetRipper 会将 BSON 解析为可读的 YAML `.asset` 文件。

## Dave the Diver 导出结构

### 导出目录

```
ripped/UnityProject/ExportedProject/Assets/
├── AssetBundleResources/GameDataSheet/     # 游戏数据 JSON（物品、鱼类等）
├── MonoBehaviour/                           # 翻译文本 YAML（关键！）
│   ├── ItemNameText.asset                  # 物品名翻译
│   ├── FishText.asset                      # 鱼类相关翻译
│   ├── ItemDescText.asset                  # 物品描述翻译
│   ├── RecipeText.asset                    # 菜谱翻译
│   ├── UIText.asset                        # UI 翻译
│   ├── MissionText.asset                   # 任务翻译
│   ├── NPCText.asset                       # NPC 翻译
│   └── ...                                 # 其他翻译类型
├── Contents/                               # Prefab、材质、动画等
└── Sprite/                                 # 精灵图
```

### 翻译文件格式（YAML）

`MonoBehaviour/*.asset` 中的翻译条目格式：

```yaml
- name: Green_Sea_Urchin_Name       # Text Key（与 ItemTextID 对应）
  english: Green Sea Urchin
  korean: 말똥성게
  japanese: バフンウニ
  chinese: 马粪海胆
  chinesetraditional: 馬糞海膽
  french: Oursin vert
  italian: Riccio di mare verde
  germany: Grüner Seeigel
  spanish: Erizo de mar verde
  portuguese: Ouriço-do-Mar Verde
  russian: Зеленый морской еж
```

### 游戏数据 JSON

`AssetBundleResources/GameDataSheet/` 下的文件在两种导出模式下都可用：

| 文件 | 内容 | 关键字段 |
|------|------|----------|
| `DR_GameData_Item.json` | 物品数据 | `ItemTextID`, `TID`, `SpawnObject`, `ItemSellPrice` |
| `DR_GameData_Fish.json` | 鱼类数据 | `FishName`, `NameTextID`, `FishType` |
| `DR_GameData_Equipment.json` | 装备数据 | |
| `DR_GameData_SushiBar.json` | 寿司店/菜谱 | `NameTextID` |
| `DR_GameData_FishFarm.json` | 鱼塘数据 | `ItemTextID` |
| `DR_GameData_Mission.json` | 任务数据 | |
| `DR_GameData_NPC.json` | NPC 数据 | |
| `DR_GameData_Farm.json` | 农场数据 | |
| `DR_GameData_Research.json` | 研究数据 | |
| `DR_GameData_PreText.json` | 预加载系统文本（仅 8 条） | |

### 查找物品翻译的工作流

```bash
# 1. 在 GameDataSheet 中找到物品的 ItemTextID
grep "SeaUrchin" ripped/UnityProject/ExportedProject/Assets/AssetBundleResources/GameDataSheet/DR_GameData_Item.json
# → "ItemTextID": "Green_Sea_Urchin_Name"

# 2. 用 ItemTextID 在翻译文件中查找
grep -A 12 "Green_Sea_Urchin_Name" ripped/UnityProject/ExportedProject/Assets/MonoBehaviour/ItemNameText.asset
# → 获得所有语言的翻译

# 3. 直接搜索中文名
grep -r "马粪海胆" ripped/UnityProject/ExportedProject/Assets/MonoBehaviour/
```

### 翻译数据来源（GameDataText 结构）

游戏使用 `DR.GameDataText` 类加载翻译，包含以下子表：

| 子表 | 导出文件 | 内容 |
|------|----------|------|
| `ItemNameText` | `ItemNameText.asset` | 物品名称 |
| `ItemDescText` | `ItemDescText.asset` | 物品描述 |
| `FishText` | `FishText.asset` | 鱼类文本 |
| `RecipeText` | `RecipeText.asset` | 菜谱文本 |
| `UIText` | `UIText.asset` | UI 文本 |
| `DialogText` | `DialogText.asset` | 对话文本 |
| `MissionText` | `MissionText.asset` | 任务文本 |
| `NPCText` | `NPCText.asset` | NPC 文本 |
| `SceneText` | `SceneText.asset` | 场景文本 |
| `TutorialText` | `TutorialText.asset` | 教程文本 |
| `SNSText` / `SNSFeedText` | `SNSText.asset` | 社交媒体文本 |
| `StaffText` / `StaffSkillText` | `StaffText.asset` | 员工文本 |
| `CalendarText` | `CalendarText.asset` | 日历文本 |
| 更多... | | |

## 注意事项

- 加载游戏目录约 1-5 分钟，Unity Project 导出约 3-10 分钟
- HTTP 请求在操作完成前会阻塞（curl 需要设置长超时：`--max-time 600`）
- 返回 302 = 成功（重定向回首页）
- Unity Project 导出可能 > 10GB，确保磁盘空间充足
- `ripped/` 目录已添加到 `.gitignore`
- AssetRipper 支持 Unity 3.5.0 到 6000.2.X，已验证支持本游戏的 Unity 6000.0.52f1
