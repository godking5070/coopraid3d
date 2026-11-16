"use strict";
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const root = path.resolve(__dirname, "../..");
const pack = path.join(root, "Assets/UI/AstellaPrismV4");
const art = JSON.parse(fs.readFileSync(path.join(pack, "Source/art-manifest.json"), "utf8"));
const approvedPath = art.approvedReference || "UI-Concepts/2026-11-16-prism-v4/02-continuous-fill.png";
const approvedAbsolute = path.join(root, approvedPath);
const manifestPath = path.join(pack, "prism-v4-manifest.json");
const previous = fs.existsSync(manifestPath) ? JSON.parse(fs.readFileSync(manifestPath, "utf8")) : null;
const approvedDigest = fs.existsSync(approvedAbsolute)
  ? crypto.createHash("sha256").update(fs.readFileSync(approvedAbsolute)).digest("hex")
  : previous && previous.approvedReference ? previous.approvedReference.sha256 : null;
const manifest = {
  schemaVersion: 1,
  packageId: "astella-prism-v4",
  packageVersion: art.provenance && art.provenance.paintedAtlas ? "1.0.1" : "1.0.0",
  assetRoot: "Assets/UI/AstellaPrismV4",
  purpose: "Separate text-free Prism HUD resources for transfer to the main project.",
  approvedReference: { path: approvedPath, sha256: approvedDigest, canvas: [1672, 941] },
  provenance: art.provenance || { method: art.reconstruction, generator: art.generator, renderer: art.renderer, recoveredLayers: false },
  iconArtwork: { included: false, policy: "Reuse existing skill, class and role icons; this package only supplies empty artwork Images, frames and masks." },
  text: { bakedIntoSprites: false, runtime: ["player health", "boss name and percent", "cast time", "party names", "job resource values", "key bindings", "cooldown values"] },
  import: { type: "Sprite", spriteMode: "Single", mesh: "FullRect", alphaIsTransparency: true, pivot: [.5, .5], pixelsPerUnit: 100, filter: "Bilinear", wrap: "Clamp", mipmaps: false, compression: "Uncompressed", slice: "Do not resize pointed or octagonal art with arbitrary nine-slicing." },
  coordinateSystem: art.coordinateSystem,
  gauges: art.gauges,
  hub: art.hub,
  skills: art.skills,
  legacyVectorSources: art.legacyVectorSources || null,
  layerOrder: {
    gauge: ["full track", "full-width fill clipped by live ratio", "frame", "boss gems if applicable", "runtime text"],
    slot: ["background", "existing game icon clipped by inner mask", "cooldown overlay", "state frame", "blank key badge", "runtime labels"],
    center: ["background", "central crystal", "state overlay", "frame", "blank ultimate badge", "runtime key"]
  },
  bindings: {
    playerHealth: ["AstellaOrnateHUD.playerHpFill", "AstellaOrnateHUD.playerHpText"],
    boss: ["AstellaOrnateHUD.bossHpFill", "bossTitleText", "bossPercentText", "castFill", "castNameText", "castTimeText"],
    rightJobResource: { owner: "AstellaClassJobHUD", exclude: ".ultimate", source: "View.fillMask / fillArtwork / valueText / labelText" },
    centralUltimate: { owner: "AstellaClassUltimateHUD", source: "ChargeRatio / CurrentState / views[]", readiness: "AstellaPlayerHUDV3.IsReady" },
    party: "AstellaPartyHUDV4.source and rows[]; retain existing callbacks and icon registry.",
    skills: { owner: "AstellaOrnateHUD.slots[]", semanticOrder: ["Gcd1", "Gcd2", "OffGcd1", "Counter", "Job1", "Movement", "Identity", "OffGcd2"], labels: "LocalInputBindings.Label", artwork: "Reuse RaidUIPresentation.Settings.Icon(skill.id)" }
  },
  tools: { import: "Tools > Astella > Prism V4 > Import Sprites", examples: "Tools > Astella > Prism V4 > Build Example Prefabs", automaticMainApplication: false },
  assets: art.assets,
  validation: { report: "UI-Production/PrismV4/verification.json", scope: "Independent resource-kit verification, not the current main scene's integration/Play status; consult the separate main-project evidence.", unityPlayVerified: false, liveBindingsApplied: false }
};
fs.writeFileSync(path.join(pack, "prism-v4-manifest.json"), JSON.stringify(manifest, null, 2) + "\n");
console.log("Finalized " + manifest.assets.length + " separate sprites; icon artwork excluded.");
