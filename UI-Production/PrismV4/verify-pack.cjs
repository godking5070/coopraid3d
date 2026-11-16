"use strict";

const fs = require("node:fs");
const path = require("node:path");
const zlib = require("node:zlib");
const crypto = require("node:crypto");
const root = path.resolve(__dirname, "../..");
const pack = path.join(root, "Assets/UI/AstellaPrismV4");
const manifest = JSON.parse(fs.readFileSync(path.join(pack, "Source/art-manifest.json"), "utf8"));
const failures = [];
const results = [];
const cache = new Map();
function check(condition, message) { if (!condition) failures.push(message); }
function hash(data) { return crypto.createHash("sha256").update(data).digest("hex"); }
const deliveryManifestPath = path.join(pack, "prism-v4-manifest.json");
if (fs.existsSync(deliveryManifestPath)) {
  const delivery = JSON.parse(fs.readFileSync(deliveryManifestPath, "utf8"));
  check(delivery.assets.length === manifest.assets.length, "Delivery/source manifest asset counts differ.");
  for (const asset of manifest.assets) {
    const published = delivery.assets.find(item => item.id === asset.id);
    check(published && published.pngSha256 === asset.pngSha256 && published.svgSha256 === asset.svgSha256,
      "Delivery manifest is stale: " + asset.id + "; run finalize-manifest.cjs after the last artwork build.");
  }
  check(JSON.stringify(delivery.gauges) === JSON.stringify(manifest.gauges), "Delivery gauge geometry is stale.");
}
function paeth(a, b, c) {
  const p = a + b - c, x = Math.abs(p - a), y = Math.abs(p - b), z = Math.abs(p - c);
  return x <= y && x <= z ? a : y <= z ? b : c;
}
function png(file) {
  const data = fs.readFileSync(file);
  if (data.subarray(0, 8).toString("hex") !== "89504e470d0a1a0a") throw new Error("Invalid PNG: " + file);
  let width, height, bits, type, interlace;
  const idat = [];
  for (let offset = 8; offset < data.length;) {
    const length = data.readUInt32BE(offset), kind = data.toString("ascii", offset + 4, offset + 8);
    const bytes = data.subarray(offset + 8, offset + 8 + length);
    if (kind === "IHDR") { width = bytes.readUInt32BE(0); height = bytes.readUInt32BE(4); bits = bytes[8]; type = bytes[9]; interlace = bytes[12]; }
    if (kind === "IDAT") idat.push(bytes);
    offset += length + 12;
    if (kind === "IEND") break;
  }
  if (bits !== 8 || type !== 6 || interlace !== 0) throw new Error("Expected non-interlaced RGBA8: " + file);
  const raw = zlib.inflateSync(Buffer.concat(idat)), stride = width * 4, pixels = Buffer.alloc(width * height * 4);
  if (raw.length !== (stride + 1) * height) throw new Error("Invalid PNG scanline size: " + file);
  for (let y = 0; y < height; y++) {
    const mode = raw[y * (stride + 1)], row = y * stride;
    if (mode > 4) throw new Error("Invalid PNG filter");
    for (let x = 0; x < stride; x++) {
      const a = x >= 4 ? pixels[row + x - 4] : 0;
      const b = y > 0 ? pixels[row - stride + x] : 0;
      const c = y > 0 && x >= 4 ? pixels[row - stride + x - 4] : 0;
      const predictor = mode === 0 ? 0 : mode === 1 ? a : mode === 2 ? b : mode === 3 ? Math.floor((a + b) / 2) : paeth(a, b, c);
      pixels[row + x] = (raw[y * (stride + 1) + 1 + x] + predictor) & 255;
    }
  }
  let transparent = 0, visible = 0;
  for (let i = 3; i < pixels.length; i += 4) { if (pixels[i] === 0) transparent++; else visible++; }
  return { width, height, pixels, transparent, visible, sha256: hash(data) };
}

for (const asset of manifest.assets) {
  check(!/\/Icons\/|role-(?:shield|star|diamond|flower)|skill-\d/i.test(asset.png), "Unexpected excluded icon: " + asset.png);
  const file = path.join(pack, asset.png), source = path.join(pack, asset.svg);
  check(fs.existsSync(file) && fs.existsSync(source), "Missing sprite/source: " + asset.id);
  if (!fs.existsSync(file) || !fs.existsSync(source)) continue;
  const image = png(file), svg = fs.readFileSync(source);
  cache.set(asset.id, image);
  check(image.width === asset.canvas[0] && image.height === asset.canvas[1], "Canvas mismatch: " + asset.id);
  check(image.transparent > 0 && image.visible > 0, "Missing alpha or artwork: " + asset.id);
  check(!/<text\b|<foreignObject\b/i.test(svg.toString("utf8")), "Baked labels: " + asset.id);
  check(image.sha256 === asset.pngSha256 && hash(svg) === asset.svgSha256, "Outdated manifest digest: " + asset.id);
  if (/frame/.test(asset.id)) {
    const center = (Math.floor(image.height / 2) * image.width + Math.floor(image.width / 2)) * 4 + 3;
    check(image.pixels[center] === 0, "Frame center must be transparent: " + asset.id);
  }
  results.push({ id: asset.id, canvas: asset.canvas, transparentPixels: image.transparent, visiblePixels: image.visible, pngSha256: image.sha256 });
}

const gaugeResults = [];
for (const [name, gauge] of Object.entries(manifest.gauges)) {
  const track = cache.get(gauge.track);
  const ids = gauge.fill ? [gauge.fill] : Object.values(gauge.fills);
  if (!track) { failures.push("Missing gauge track: " + name); continue; }
  for (const id of ids) {
    const fill = cache.get(id);
    if (!fill) { failures.push("Missing gauge fill: " + id); continue; }
    let holes = 0;
    for (let i = 3; i < track.pixels.length; i += 4) {
      if (track.pixels[i] >= 254 && fill.pixels[i] < 254) holes++;
    }
    check(holes === 0, "Full gauge leaves " + holes + " track pixels uncovered: " + id);
    const [left, top, width, height] = gauge.innerBounds;
    const y = Math.round(top + height / 2);
    const leftPixel = (y * fill.width + Math.ceil(left + 2)) * 4 + 3;
    const rightPixel = (y * fill.width + Math.floor(left + width - 3)) * 4 + 3;
    check(fill.pixels[leftPixel] > 240 && fill.pixels[rightPixel] > 240, "Full gauge misses a triangle tip: " + id);
    const ratios = [0, .5, 1].map(ratio => {
      let visible = 0;
      const front = left + width * ratio, tip = Math.min(gauge.movingTipWidth || 0, width * ratio);
      if (ratio > 0) for (let yy = 0; yy < fill.height; yy++) {
        const cross = Math.min(1, Math.abs(yy + .5 - (top + height / 2)) / (height / 2));
        const edge = ratio >= 1 ? fill.width : front - tip * cross;
        for (let xx = 0; xx < fill.width; xx++) if (xx + .5 <= edge && fill.pixels[(yy * fill.width + xx) * 4 + 3] > 0) visible++;
      }
      return { ratio, visiblePixels: visible };
    });
    check(ratios[0].visiblePixels === 0 && ratios[1].visiblePixels > 0 && ratios[1].visiblePixels < ratios[2].visiblePixels && ratios[2].visiblePixels === fill.visible, "Gauge ratio reveal mismatch: " + id);
    gaugeResults.push({ id, fullCoverageHoles: holes, ratios });
  }
}

const report = { passed: failures.length === 0, iconArtworkIncluded: false, spriteCount: results.length, assets: results, gauges: gaugeResults, failures, limits: ["Current PNG alpha/dimensions/digests and retained legacy SVG reference checks; this offline verifier does not exercise Unity Play or live game bindings."] };
const destination = path.join(__dirname, "verification.json");
fs.writeFileSync(destination, JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify({ passed: report.passed, spriteCount: report.spriteCount, gaugeChecks: report.gauges.length, failures, report: destination }));
if (!report.passed) process.exitCode = 1;
