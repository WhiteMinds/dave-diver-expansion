# Dave the Diver 存档文件格式

本文档描述 Dave the Diver (Steam/Xbox) 存档文件的加密格式、文件命名规则，以及解密/加密的完整算法。

## 存档目录结构

### Steam

```
%USERPROFILE%\AppData\LocalLow\nexon\DAVE THE DIVER\SteamSData\<SteamID>\
```

### Xbox / Windows Store

Xbox 版使用长十六进制文件名，不遵循下面的命名规则。

### 文件命名规则 (Steam)

```
[prefix_]GameSave_<slot>_<type>.sav
```

| 组成部分 | 值 | 含义 |
|----------|------|------|
| **prefix** | （无） | 当前自动存档（游戏正在使用的最新版本） |
| | `e_` | 上一次自动存档备份（稍旧） |
| | `m_` | 手动存档 |
| **slot** | `00`, `01`, `02` | 存档槽位编号（游戏提供 3 个存档位） |
| **type** | `GD` | **G**ame **D**ata — 主存档，XOR 加密的 JSON |
| | `PZ` | 二进制数据（地图/场景状态），非 JSON，不可用本工具编辑 |
| | `UO` | 小型二进制数据（仅部分槽位有），不可用本工具编辑 |

**只有 `_GD.sav` 文件是 XOR 加密的 JSON**，是本工具的处理对象。

## XOR 加密方案

### 密钥

固定字符串 `"GameData"` (8 字符)，源自 `SaveDataType.GameData.ToString()`。

### 加密/解密实现

游戏使用 **CodeStage.AntiCheat.ObscuredTypes.ObscuredString.EncryptDecrypt**。通过逆向 IsilDump（Cpp2IL 反编译）确认，该方法的核心逻辑等价于：

```csharp
static string EncryptDecrypt(string value, string key) {
    char[] result = new char[value.Length];
    for (int i = 0; i < value.Length; i++) {
        result[i] = (char)(value[i] ^ key[i % key.Length]);
    }
    return new string(result);
}
```

**关键点：XOR 操作在 C# `char` (UTF-16 code unit) 级别，key cycling 是 per-char (`i % keyLen`)，不是 per-byte。**

### 完整数据流

#### 解密（读取存档）

```
File.ReadAllText(path)          // 读文件字节，UTF-8 解码为 C# string
  → string encrypted             // 每个 char 是一个 UTF-16 code unit
ObscuredString.EncryptDecrypt(encrypted, "GameData")
  → string decrypted             // 逐 char XOR 解密，得到 JSON 明文
JsonConvert.DeserializeObject<SaveData>(decrypted)
  → SaveData                     // Newtonsoft.Json 反序列化
```

#### 加密（写入存档）

```
JsonConvert.SerializeObject(saveData)
  → string json                  // JSON 明文
ObscuredString.EncryptDecrypt(json, "GameData")
  → string encrypted             // 逐 char XOR 加密
StreamWriter / File.WriteAllText
  → 写入文件                     // UTF-8 编码为字节
```

### 为什么不能用 byte-level XOR

上游 DaveSaveEd（C++/Python 版本）使用 byte-level XOR，对绝大多数存档可以正确工作。但当存档包含非 ASCII 内容（如中文农场动物名 `"白毛鸡"`）时：

1. 游戏加密时，中文明文 char → XOR → 非 ASCII char → UTF-8 编码为多字节序列
2. 解密时，`File.ReadAllText` UTF-8 解码回正确的 chars → XOR 还原中文
3. 但 byte-level XOR 的 key cycling 是 per-byte，当 UTF-8 多字节字符出现时，**char 数 ≠ byte 数**，导致 key phase 错位

对于**未修改**的原始存档，byte-level 和 char-level 结果恰好一致（因为 byte offset 与 char offset 的 key phase 差异被文件生成时的一致性保证了）。但**编辑后**如果文件长度改变，byte-level XOR 在多字节 char 区域的 key phase 会与游戏的 char-level XOR 不同，导致解密失败。

### 解密结果

解密后得到紧凑格式的 JSON 字符串（无空白），包含游戏全部存档数据。典型大小 300–400 KB，包含 80+ 个顶层键如 `Version`、`PlayerInfo`、`Ingredients`、`Chapter` 等。

## Node.js 实现注意事项

### 大整数精度

存档中包含超过 `Number.MAX_SAFE_INTEGER` (2^53 - 1) 的整数，如：

```json
"LastUpdateTime": 639076164000175857
```

**绝对不能** 使用 `JSON.parse()` → `JSON.stringify()` 来处理数据，否则大整数会丢失精度（例如 `639076164000175857` 变成 `639076164000175900`）。

解决方案：
- 解码时：先解密得到原始 JSON 字符串，只调用 `JSON.parse()` 做校验，但写入文件时使用原始字符串
- 美化输出时：用基于字符扫描的 pretty-printer，不经过 `JSON.parse`
- 编码时：用基于字符扫描的 compactifier 去除空白，不经过 `JSON.parse`

### 键序保持

游戏存档依赖 JSON 键的插入顺序。C++ 版使用 `nlohmann::ordered_json` (基于 `fifo_map`) 来保持键序。JavaScript 的 `JSON.stringify` 在大多数情况下保持插入顺序，但对纯数字键名会按数值排序。因此同样需要避免经过 `JSON.parse`/`JSON.stringify` 往返。

## 存档编辑实践指南

### 正确做法

1. 用 `decode.mjs` 解码 `.sav` → pretty-printed `.json`
2. 用**纯文本操作**（`String.indexOf` / `String.replace` 等）修改 JSON 文本
3. 用 `decode.mjs` 重新编码 `.json` → `.sav`
4. 用 `decode.mjs --test` 做回环测试验证编码正确

### 已验证可行的修改类型

- 修改数值（如任务 `nowCounts: [5]` → `[0]`）✅
- 删除/清空对象（如 `"figures": {}` 替换原有内容）✅
- 删除 JSON 条目（如从 `Looting`、`AchieveKeyToCount` 中移除条目）✅
- 同时修改多处关联数据（figures + Looting + AchieveKeyToCount + mission counts）✅

### 注意事项

- 游戏没有 checksum/hash 校验——存档错误纯粹是 JSON 反序列化失败
- 不需要同步修改 PZ 文件
- 尾逗号处理：删除 JSON 条目后注意处理前后逗号，避免产生非法 JSON
