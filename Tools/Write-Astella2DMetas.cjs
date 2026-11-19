"use strict";
// Same new-assets-only policy as UI-Production/PrismV4/write-metas.cjs.
// node Tools/Write-Astella2DMetas.cjs --write; an existing .meta is never replaced.
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const root = path.resolve(__dirname, "..");
const assets = path.join(root, "Assets");
const pack = path.join(assets, "Astella2D");
function walk(folder) {
  return fs.readdirSync(folder, { withFileTypes: true }).flatMap(entry => {
    if (entry.name.startsWith(".")) return [];
    const file = path.join(folder, entry.name);
    if (entry.isSymbolicLink()) throw new Error("Refusing symbolic link: " + file);
    return entry.isDirectory() ? [file, ...walk(file)] : [file];
  });
}
function guid(file) {
  return crypto.createHash("sha256").update("Astella2D-new-assets-20261120-v1:" + path.relative(root, file).split(path.sep).join("/")).digest("hex").slice(0, 32);
}
function metadata(file) {
  const head = `fileFormatVersion: 2\nguid: ${guid(file)}\n`;
  const tail = "  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
  if (fs.statSync(file).isDirectory()) return head + "folderAsset: yes\nDefaultImporter:\n" + tail;
  if (path.extname(file) === ".cs") return head + "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
  if (path.extname(file) !== ".png") return head + "TextScriptImporter:\n" + tail;
  return head + `TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 1
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  spriteMode: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
  userData:
  assetBundleName:
  assetBundleVariant:
`;
}
const known = new Set();
for (const file of walk(assets).filter(file => file.endsWith(".meta"))) {
  const id = fs.readFileSync(file, "utf8").match(/^guid: ([a-fA-F0-9]{32})\r?$/m)?.[1].toLowerCase();
  if (!id) continue;
  if (known.has(id)) throw new Error("Existing GUID collision; no files changed: " + file);
  known.add(id);
}
const pending = [];
for (const file of [pack, ...walk(pack)].filter(file => !file.endsWith(".meta"))) {
  const target = file + ".meta";
  if (fs.existsSync(target)) continue;
  const id = guid(file);
  if (known.has(id)) throw new Error("New GUID collision; no files changed: " + file);
  known.add(id); pending.push([target, metadata(file)]);
}
if (process.argv.includes("--write")) for (const [target, text] of pending) fs.writeFileSync(target, text, { flag: "wx" });
console.log(`${process.argv.includes("--write") ? "Created" : "Would create"} ${pending.length} new Astella2D metadata files; all existing GUIDs preserved.`);
