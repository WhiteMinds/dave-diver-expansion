# dave-the-diver-save-codec

Node.js 命令行工具，用于解密/加密 Dave the Diver 的存档文件 (`.sav` ↔ `.json`)。

参考 [DaveSaveEd](https://github.com/FNGarvin/DaveSaveEd) 的 C++/Python 实现，零第三方依赖。

**与上游实现的关键区别**：本版使用 char-level XOR（匹配游戏的 `ObscuredString.EncryptDecrypt` 实现），而非上游的 byte-level XOR。这使得编辑包含非 ASCII 字符（如中文农场动物名）的存档后仍能正确加密。

## 用法

```bash
# 解密 .sav → .json（美化输出）
node decode.mjs GameSave_00_GD.sav

# 加密 .json → .sav
node decode.mjs GameSave_00_GD.json

# 回环测试（.sav → .json → .sav，验证字节完全一致）
node decode.mjs --test GameSave_00_GD.sav

# 批量处理
node decode.mjs file1.sav file2.sav file3.json
```

## 存档文件位置

### Steam
```
%USERPROFILE%\AppData\LocalLow\nexon\DAVE THE DIVER\SteamSData\<SteamID>\
```

### Xbox / Windows Store
存档位于 Xbox 沙盒目录中，文件名为长十六进制字符串。

只有 `*_GD.sav` 文件包含可编辑的 JSON 游戏数据。`*_PZ.sav` 和 `*_UO.sav` 是二进制格式，不受本工具支持。

## 技术细节

详见：
- [SAVEFILE-FORMAT.md](./SAVEFILE-FORMAT.md) — 存档文件格式、加密方案的完整说明
- [PORTING-NOTES.md](./PORTING-NOTES.md) — 从 C++/Python 移植到 Node.js 的关键决策、char vs byte XOR 区别

### 要点速览

- **加密方式**: char-level 循环 XOR，密钥 `"GameData"` (8 chars)，匹配游戏的 `ObscuredString.EncryptDecrypt`
- **大整数安全**: 不使用 `JSON.parse`/`JSON.stringify` 处理数据，避免精度丢失
- **键序保持**: 纯字符串操作，不经过 JS 对象往返

## 环境要求

- Node.js >= 14
- 无第三方依赖

## 许可

沿用 DaveSaveEd 原项目许可 (zlib license)。
