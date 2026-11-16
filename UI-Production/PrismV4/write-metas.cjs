"use strict";
// After every pack asset is final: node UI-Production/PrismV4/write-metas.cjs --write
// Default is a read-only plan. New path-derived GUIDs; never replaces an existing .meta.
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const workspace = path.resolve(__dirname, "../..");
const assets = path.join(workspace, "Assets");
const pack = path.join(assets, "UI/AstellaPrismV4");
const write = process.argv.includes("--write");

function walk(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    // Unity ignores hidden files/directories; never create metadata for scoped .gitattributes, etc.
    if (entry.name.startsWith(".")) return [];
    const target = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error("Refusing symbolic link: " + target);
    return entry.isDirectory() ? [target, ...walk(target)] : [target];
  });
}
function guid(file, suffix = "") {
  const relative = path.relative(workspace, file).split(path.sep).join("/");
  return crypto.createHash("sha256").update("AstellaPrismV4-new-assets-20261116-v1:" + relative + suffix).digest("hex").slice(0, 32);
}
function metadata(file) {
  const header = `fileFormatVersion: 2\nguid: ${guid(file)}\n`;
  const tail = "  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
  if (fs.statSync(file).isDirectory()) return header + "folderAsset: yes\nDefaultImporter:\n" + tail;
  const extension = path.extname(file).toLowerCase();
  if (extension === ".cs") return header + "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
  if (extension !== ".png") return header + ([".md", ".txt", ".json"].includes(extension) ? "TextScriptImporter:\n" : "DefaultImporter:\n") + tail;
  return header + `TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 8
  textureShape: 1
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
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: ${guid(file, ":sprite")}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  userData:
  assetBundleName:
  assetBundleVariant:
`;
}

if (!fs.statSync(pack).isDirectory()) throw new Error("Pack missing: " + pack);
const known = new Map();
for (const file of walk(assets).filter(file => file.endsWith(".meta"))) {
  const match = fs.readFileSync(file, "utf8").match(/^guid: ([a-fA-F0-9]{32})\r?$/m);
  if (!match) continue;
  const id = match[1].toLowerCase();
  if (known.has(id)) throw new Error("Existing GUID collision: " + known.get(id) + " and " + file);
  known.set(id, file);
}
const pending = [];
for (const file of [pack, ...walk(pack)].filter(file => !file.endsWith(".meta")).sort()) {
  const meta = file + ".meta";
  if (fs.existsSync(meta)) {
    if (!/^guid: [a-fA-F0-9]{32}\r?$/m.test(fs.readFileSync(meta, "utf8"))) throw new Error("Invalid existing metadata, left untouched: " + meta);
    continue;
  }
  const id = guid(file);
  if (known.has(id)) throw new Error("New GUID collides with " + known.get(id));
  known.set(id, meta);
  pending.push([meta, metadata(file)]);
}
if (write) for (const [file, content] of pending) fs.writeFileSync(file, content, { encoding: "utf8", flag: "wx" });
console.log(`${write ? "Created" : "Would create"} ${pending.length} new .meta files; existing files and GUIDs preserved.`);
