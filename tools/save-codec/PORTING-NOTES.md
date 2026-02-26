# 实现笔记：参考 C++/Python 实现

本文档记录参考 DaveSaveEd C++ 版和 `encdec.py` Python 版实现 Node.js 版本过程中的关键决策和差异。

## 参考实现

| 实现 | 文件 | 特点 |
|------|------|------|
| C++ (主程序) | `SaveGameManager.cpp`, `encdec.h/cpp` | 完整 GUI 编辑器，使用 `nlohmann::ordered_json` 保持键序 |
| Python (参考) | `encdec.py` | 更简洁的命令行实现，逻辑等价 |
| **Node.js (本版)** | `decode.mjs` | 参考 Python 版结构，使用 char-level XOR（匹配游戏实现） |

## 与上游实现的关键区别：Char-level vs Byte-level XOR

### 上游的 byte-level XOR（C++/Python 共同使用）

```python
# Python 版
for i in range(len(data)):
    output[i] = data[i] ^ key[(key_index + i) % 8]
```

上游在 **raw bytes** 级别做 XOR，key cycling 按 byte 递增。

### 游戏的实际实现：char-level XOR

通过逆向游戏的 `ObscuredString.EncryptDecrypt`（CodeStage AntiCheat 库，IsilDump 确认）发现，游戏实际上是：

1. `File.ReadAllText(path)` — UTF-8 解码文件字节为 C# `string`
2. 逐 **char** (UTF-16 code unit) 与 key 的 char 做 XOR
3. 写入文件时 UTF-8 编码回字节

```csharp
// 游戏的实际实现（ISIL 逆向确认）
static string EncryptDecrypt(string value, string key) {
    char[] result = new char[value.Length];
    for (int i = 0; i < value.Length; i++) {
        result[i] = (char)(value[i] ^ key[i % key.Length]);
    }
    return new string(result);
}
```

### 为什么 byte-level 在大多数情况下能工作

对于纯 ASCII 的存档数据，1 byte = 1 char，byte-level 和 char-level XOR 的结果完全一致。大多数欧美用户的存档不包含多字节 UTF-8 字符，所以上游实现可以正常工作。

### 什么时候会出问题

当存档包含非 ASCII 字符时（如中文农场动物名 `"白毛鸡"`），加密后的 char 用 UTF-8 编码可能占 2-3 bytes。此时：

- **char count ≠ byte count**
- byte-level XOR 的 key phase 与 char-level 不同
- **编辑存档后文件长度改变** → 问题区域的 byte offset 变化 → key phase 错位加剧
- 结果：解密出的 JSON 在问题字段处包含非法内容，Newtonsoft.Json 报错

实测：原始存档 UTF-8 解码后有 0 个 U+FFFD 替换字符（完全合法 UTF-8），但 byte-level XOR 编辑后的文件有 11 个替换字符。

### Node.js 版的解决方案

直接在 JavaScript string（UTF-16）级别做 XOR，完美镜像游戏实现：

```javascript
function charXor(str, key = "GameData") {
  const result = new Array(str.length);
  for (let i = 0; i < str.length; i++) {
    result[i] = String.fromCharCode(str.charCodeAt(i) ^ key.charCodeAt(i % key.length));
  }
  return result.join("");
}
```

这消除了所有 FarmAnimal Name 字段的问题，不再需要 BYPASSED_HEX 绕过机制。

## 已移除的 BYPASSED_HEX 机制

上游实现（C++/Python/旧版 Node.js）使用 BYPASSED_HEX 机制来处理 FarmAnimal Name 字段中的非 UTF-8 字节：遇到该字段时保存原始密文 hex，编码时原样写回。

这个机制有一个根本缺陷：当编辑导致文件长度变化时，原样写回的密文在新的 byte offset 处会被游戏用不同的 key phase 解密，产生乱码。

**采用 char-level XOR 后，BYPASSED_HEX 机制完全不需要了**，因为在 char 级别操作时所有字符（包括中文）都是正常的 Unicode chars，XOR 后仍是合法的 Unicode。

## 与 Python 版的对应关系

| Python | Node.js | 说明 |
|--------|---------|------|
| `bytearray` XOR 循环 | `charXor(str, key)` 字符串操作 | 核心区别：char-level vs byte-level |
| `json.dumps(obj, separators=(',',':'))` | `compactifyRaw(text)` | 不能用 `JSON.stringify`，见下文 |
| `json.dumps(obj, indent=4)` | `prettyPrintRaw(text)` | 不能用 `JSON.stringify`，见下文 |

## 不能使用 JSON.parse / JSON.stringify 的原因

### 问题 1：大整数精度丢失

```
原始值:   639076164000175857
JSON.parse 后: 639076164000175900  (损失精度)
```

JavaScript `Number` 是 IEEE 754 双精度浮点，安全整数范围仅到 2^53 - 1 = 9007199254740991。存档中的时间戳等字段远超此范围。

### 问题 2：数字键名排序

`JSON.stringify` 对纯数字键名按数值升序排列（V8 行为），而存档依赖原始插入顺序。

### 解决方案

使用纯字符串操作代替 JSON 解析：

- **`prettyPrintRaw(compact)`** — 扫描字符串状态机，在字符串外的 `{`, `[`, `}`, `]`, `,`, `:` 处插入换行/缩进
- **`compactifyRaw(jsonText)`** — 扫描字符串状态机，删除字符串外的所有空白字符
- 解码时仅调用 `JSON.parse` 做格式校验，写入文件时使用原始字符串

## 依赖

零第三方依赖。仅使用 Node.js 内置模块：
- `node:fs` — 文件读写
- `node:path` — 路径处理
- `node:readline` — 覆盖确认提示

要求 Node.js >= 14（ESM 支持）。
