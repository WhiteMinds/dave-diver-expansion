# 发布工作流

## CI/CD

### GitHub Actions Release 工作流

- 配置文件：`.github/workflows/release.yml`
- 触发条件：推送 `v*` 标签（如 `v0.1.0`）
- 运行环境：`windows-latest`
- 步骤：checkout (含 LFS) → dotnet build Release → 打包 zip → 创建 GitHub Release → 上传 NexusMods
- 需要 `permissions: contents: write` 权限（已配置）

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

## 发布新版本

**完整发布流程**（含 GitHub Release + NexusMods 上传）：

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
# 8. NexusMods 上传（见下方）
```

**发布时需更新的文件检查清单**：

| 文件 | 内容 | 必须 |
|------|------|------|
| `src/.../Plugin.cs:37` | `PLUGIN_VERSION = "X.Y.Z"` | ✅ |
| `docs/nexusmods-description.bbcode` | Changelog 区域 | ✅ |
| `README.md` | Features 列表（如有功能变化） | 可选 |

## NexusMods 上传

- NexusMods 页面：https://www.nexusmods.com/davethediver/mods/20
- Mod ID: `20` | Game domain: `davethediver`
- CI 的 `Nexus-Mods/upload-action` 目前无法使用（Upload API 处于 evaluation 阶段，账号未被授权）

### 手动上传步骤

1. 从 GitHub Release 下载 zip：`gh release download vX.Y.Z --pattern "*.zip"`
2. 打开 NexusMods 文件管理页：`https://www.nexusmods.com/davethediver/mods/edit/?step=files&id=20`
3. 填写表单：
   - File name: `DaveDiverExpansion vX.Y.Z`
   - Version: `X.Y.Z`
   - 勾选 "Update mod version"
   - Category: Main Files
   - File description: 简短版本变更摘要（**限 255 字符以内**）
   - 上传 zip 文件
4. 点击 "Save file"

### 更新描述步骤

1. 打开 Mod details 编辑页：`https://www.nexusmods.com/davethediver/mods/edit/?step=2&id=20`
2. 点击工具栏最右侧的 `[bbcode]` 按钮切换到 BBCode 源码模式
3. 用 `docs/nexusmods-description.bbcode` 的内容替换整个描述
4. 点击 `[bbcode]` 切回 WYSIWYG 模式（让编辑器同步）
5. 点击 "Save"

### Playwright 自动化

一键发布脚本 `.tmp/pw-nexusmods-release.js`：

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

### NexusMods 页面 DOM 选择器

已验证，避免反复探索：

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
