#!/usr/bin/env node
// Dave the Diver save file encoder/decoder
// Referenced https://github.com/FNGarvin/DaveSaveEd (C++/Python)
//
// Copyright (c) 2025 FNGarvin (184324400+FNGarvin@users.noreply.github.com)
// All rights reserved.
//
// This software is provided 'as-is', without any express or implied
// warranty. In no event will the authors be held liable for any damages
// arising from the use of this software.
//
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it
// freely, subject to the following restrictions:
//
// 1. The origin of this software must not be misrepresented; you must not
//    claim that you wrote the original software. If you use this software
//    in a product, an acknowledgment in the product documentation would be
//    appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be
//    misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.

import { readFileSync, writeFileSync, existsSync, unlinkSync } from "node:fs";
import { basename, extname } from "node:path";
import { createInterface } from "node:readline";

// ─── Constants ───────────────────────────────────────────────────────────────

// The game uses ObscuredString.EncryptDecrypt which operates on C# chars
// (UTF-16 code units), not raw bytes. The XOR key is derived from
// SaveDataType.ToString() which yields "GameData".
const XOR_KEY = "GameData";

// ─── Helpers ─────────────────────────────────────────────────────────────────

/**
 * Char-level XOR — mirrors ObscuredString.EncryptDecrypt exactly.
 *
 * The game reads the file as a UTF-8 string (File.ReadAllText), then XORs
 * each char's code point with the corresponding key char's code point.
 * Key cycling is per-char, NOT per-byte.
 */
function charXor(str, key = XOR_KEY) {
  const len = str.length;
  const keyLen = key.length;
  const result = new Array(len);
  for (let i = 0; i < len; i++) {
    result[i] = String.fromCharCode(str.charCodeAt(i) ^ key.charCodeAt(i % keyLen));
  }
  return result.join("");
}

// ─── Pretty-print without parsing ────────────────────────────────────────────

/**
 * Naive JSON pretty-printer that operates on the raw string.
 * Avoids JSON.parse so large integers and key order are preserved exactly.
 */
function prettyPrintRaw(compact) {
  const parts = [];
  let indent = 0;
  const TAB = "    ";
  let inString = false;
  let i = 0;

  while (i < compact.length) {
    const ch = compact[i];

    if (inString) {
      if (ch === "\\") {
        parts.push(compact[i] + compact[i + 1]);
        i += 2;
        continue;
      }
      if (ch === '"') inString = false;
      parts.push(ch);
      i++;
      continue;
    }

    switch (ch) {
      case '"':
        inString = true;
        parts.push(ch);
        break;
      case "{":
      case "[":
        indent++;
        parts.push(ch + "\n" + TAB.repeat(indent));
        break;
      case "}":
      case "]":
        indent--;
        parts.push("\n" + TAB.repeat(indent) + ch);
        break;
      case ",":
        parts.push(",\n" + TAB.repeat(indent));
        break;
      case ":":
        parts.push(": ");
        break;
      default:
        parts.push(ch);
        break;
    }
    i++;
  }
  return parts.join("");
}

// ─── Decode (.sav → .json) ──────────────────────────────────────────────────

function decodeSavToJson(savPath, prettyPrint = true) {
  console.log(`[INFO] Decoding '${savPath}'...`);
  const fileBytes = readFileSync(savPath);

  // Step 1: UTF-8 decode (mirrors File.ReadAllText in C#)
  const encrypted = fileBytes.toString("utf8");

  // Step 2: Char-level XOR decrypt (mirrors ObscuredString.EncryptDecrypt)
  const decrypted = charXor(encrypted);

  // Validate that the decrypted data is parseable JSON (but do NOT use the
  // parsed result — JSON.parse mangles large integers and may re-order keys).
  try {
    JSON.parse(decrypted);
  } catch (e) {
    console.error(`[ERROR] Decrypted data is not valid JSON: ${e.message}`);
    const errorPath = savPath.replace(/\.[^.]+$/, ".failed_decode.txt");
    writeFileSync(errorPath, decrypted, "utf8");
    console.error(`  Saved decrypted text to '${errorPath}' for inspection.`);
    return;
  }

  const outPath = savPath.replace(/\.[^.]+$/, ".json");
  if (prettyPrint) {
    writeFileSync(outPath, prettyPrintRaw(decrypted), "utf-8");
  } else {
    writeFileSync(outPath, decrypted, "utf-8");
  }
  console.log(`Successfully decoded and saved to '${outPath}'.`);
  return outPath;
}

// ─── Encode (.json → .sav) ──────────────────────────────────────────────────

/**
 * Strip JSON whitespace without parsing — preserves large ints and key order.
 * Removes whitespace outside of string values.
 */
function compactifyRaw(jsonText) {
  const parts = [];
  let inString = false;
  let i = 0;
  while (i < jsonText.length) {
    const ch = jsonText[i];
    if (inString) {
      if (ch === "\\") {
        parts.push(jsonText[i], jsonText[i + 1]);
        i += 2;
        continue;
      }
      if (ch === '"') inString = false;
      parts.push(ch);
      i++;
      continue;
    }
    if (ch === '"') {
      inString = true;
      parts.push(ch);
    } else if (ch === " " || ch === "\n" || ch === "\r" || ch === "\t") {
      // skip whitespace outside strings
    } else {
      parts.push(ch);
    }
    i++;
  }
  return parts.join("");
}

function encodeJsonToSav(jsonPath, isTest = false) {
  if (!isTest) console.log(`[INFO] Encoding '${jsonPath}'...`);
  const textData = readFileSync(jsonPath, "utf-8");

  let compact;
  if (isTest) {
    // During round-trip test the file is already compact
    compact = textData;
  } else {
    // Compactify without JSON.parse to preserve large integers and key order
    compact = compactifyRaw(textData);
  }

  // Step 1: Char-level XOR encrypt (mirrors ObscuredString.EncryptDecrypt)
  const encrypted = charXor(compact);

  // Step 2: UTF-8 encode to bytes (mirrors File.WriteAllText / StreamWriter)
  const outputBytes = Buffer.from(encrypted, "utf8");

  const baseName = jsonPath.replace(/\.[^.]+$/, "");
  const outPath = isTest ? baseName + ".resave" : baseName + ".sav";

  if (!isTest && existsSync(outPath)) {
    // Synchronous prompt – mirrors Python's input()
    const rl = createInterface({
      input: process.stdin,
      output: process.stdout,
    });
    return new Promise((resolve) => {
      rl.question(`'${outPath}' already exists. Overwrite? (y/N): `, (ans) => {
        rl.close();
        if (ans.toLowerCase() !== "y") {
          resolve();
          return;
        }
        writeFileSync(outPath, outputBytes);
        console.log(`Successfully encoded to '${outPath}'.`);
        resolve(outPath);
      });
    });
  }

  writeFileSync(outPath, outputBytes);
  if (!isTest) console.log(`Successfully encoded to '${outPath}'.`);
  return outPath;
}

// ─── Round-trip Test ─────────────────────────────────────────────────────────

function testRoundtrip(savPath) {
  console.log(`--- Running round-trip test on '${savPath}' ---`);
  const baseName = savPath.replace(/\.[^.]+$/, "");
  const jsonPath = baseName + ".json";
  const resavePath = baseName + ".resave";
  let areIdentical = false;

  try {
    console.log("\nStep 1: Decoding .sav to raw .json...");
    const result = decodeSavToJson(savPath, /* prettyPrint */ false);
    if (!result || !existsSync(jsonPath)) {
      console.log("\nFAILURE: Decode step failed to produce a .json file.");
      return;
    }

    console.log("\nStep 2: Re-encoding raw .json to .resave...");
    encodeJsonToSav(jsonPath, /* isTest */ true);

    console.log(
      "\nStep 3: Comparing original .sav with the new .resave file..."
    );
    const original = readFileSync(savPath);
    const resaved = readFileSync(resavePath);

    areIdentical =
      original.length === resaved.length && original.equals(resaved);

    if (areIdentical) {
      console.log(
        "\nSUCCESS: The files are identical. The process is perfectly reversible."
      );
    } else {
      console.log(
        "\nFAILURE: The files are NOT identical. The process is not reversible."
      );
    }
  } finally {
    if (areIdentical) {
      console.log("\nStep 4: Cleaning up intermediate files...");
      if (existsSync(jsonPath)) unlinkSync(jsonPath);
      if (existsSync(resavePath)) unlinkSync(resavePath);
    }
    console.log("--- Test complete ---");
  }
}

// ─── CLI ─────────────────────────────────────────────────────────────────────

function displayUsage() {
  const name = basename(process.argv[1]);
  console.log(`Usage: node ${name} <file1.json | file1.sav> [file2 ...]`);
  console.log(`   or: node ${name} --test <file.sav>`);
  console.log(
    "\nConvert save files for Dave the Diver between .sav and .json"
  );
}

async function main() {
  const args = process.argv.slice(2);

  if (args.length === 0) {
    displayUsage();
    process.exit(1);
  }

  if (args[0] === "--test") {
    if (args.length !== 2 || !args[1].toLowerCase().endsWith(".sav")) {
      console.error(
        "\nError: --test flag requires a single .sav file as an argument."
      );
      displayUsage();
      process.exit(1);
    }
    testRoundtrip(args[1]);
    return;
  }

  for (const filepath of args) {
    if (!existsSync(filepath)) {
      console.error(`Warning: File not found: '${filepath}'. Skipping.`);
      continue;
    }
    const ext = extname(filepath).toLowerCase();
    if (ext === ".json") {
      await encodeJsonToSav(filepath);
    } else if (ext === ".sav") {
      decodeSavToJson(filepath);
    } else {
      console.error(
        `Warning: Unsupported file extension '${ext}' for '${filepath}'. Skipping.`
      );
    }
  }
}

main();
