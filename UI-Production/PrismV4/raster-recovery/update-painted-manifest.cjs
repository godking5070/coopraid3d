"use strict";
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const zlib = require("node:zlib");
const project = path.resolve(__dirname, "../../..");
const pack = path.join(project, "Assets/UI/AstellaPrismV4");
const manifestPath = path.join(pack, "Source/art-manifest.json");
const art = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
const readJson = file => JSON.parse(fs.readFileSync(path.join(__dirname,file), "utf8").replace(/^\uFEFF/, ""));
const frameReport = readJson("frames-extraction.json");
const gaugeReport = readJson("gauge-raster-report.json");
const hash = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const atlas = path.join(__dirname, "painted-transparent-atlas.png");
const frameIds = new Set(frameReport.updatedSprites.map(file => file.replace(/\.png$/, "")));
const gaugeIds = new Set(gaugeReport.assets.map(asset => asset.path.replace(/^Assets\/UI\/AstellaPrismV4\/Sprites\//, "").replace(/\.png$/, "")));

function paeth(a,b,c) {
  const p=a+b-c, x=Math.abs(p-a), y=Math.abs(p-b), z=Math.abs(p-c);
  return x<=y&&x<=z?a:y<=z?b:c;
}
function alphaStatistics(bytes) {
  const width=bytes.readUInt32BE(16), height=bytes.readUInt32BE(20);
  if(bytes[24]!==8||bytes[25]!==6) throw new Error("Current production PNG must be RGBA8");
  const data=[];
  for(let offset=8;offset<bytes.length;) {
    const length=bytes.readUInt32BE(offset), type=bytes.toString("ascii",offset+4,offset+8);
    if(type==="IDAT") data.push(bytes.subarray(offset+8,offset+8+length));
    offset+=length+12;if(type==="IEND")break;
  }
  const raw=zlib.inflateSync(Buffer.concat(data)), stride=width*4, pixels=Buffer.alloc(stride*height);
  if(raw.length!==(stride+1)*height) throw new Error("Unexpected PNG scanline length");
  for(let y=0;y<height;y++) {
    const mode=raw[y*(stride+1)], row=y*stride;
    for(let x=0;x<stride;x++) {
      const a=x>=4?pixels[row+x-4]:0,b=y>0?pixels[row-stride+x]:0,c=y>0&&x>=4?pixels[row-stride+x-4]:0;
      const predictor=mode===0?0:mode===1?a:mode===2?b:mode===3?Math.floor((a+b)/2):mode===4?paeth(a,b,c):NaN;
      if(!Number.isFinite(predictor))throw new Error("Unknown PNG filter");
      pixels[row+x]=(raw[y*(stride+1)+1+x]+predictor)&255;
    }
  }
  let transparent=0,translucent=0;
  for(let i=3;i<pixels.length;i+=4)if(pixels[i]===0)transparent++;else if(pixels[i]!==255)translucent++;
  return {canvas:[width,height],transparentPixels:transparent,translucentPixels:translucent};
}

art.version=2;
art.name="Astella Prism V4 / painted material correction";
art.reconstruction="Painted PNG components mechanically separated from a newly generated transparent image_gen atlas based on the approved raster reference; not recovered original PSD/SVG layers.";
art.approvedReference="UI-Production/PrismV4/approved-reference.png";
art.designHistoryReference="UI-Concepts/2026-11-16-prism-v4/02-continuous-fill.png";
art.generator="UI-Production/PrismV4/raster-recovery/Extract-Frames.ps1 + Extract-Gauges.ps1";
art.renderer="System.Drawing deterministic crop, masked mapping and state tint";
art.legacyVectorSources={
  path:"Assets/UI/AstellaPrismV4/Source/**/*.svg",
  status:"Historical structural/coordinate references, not the editable source of current painted RGB artwork.",
  generator:"UI-Production/PrismV4/generate-prism.cjs",
  overwriteProtection:"Legacy generator refuses to run while the painted transparent atlas exists."
};
art.provenance={
  method:art.reconstruction, recoveredLayers:false, originalPsdRecovered:false,
  paintedAtlas:{path:"UI-Production/PrismV4/raster-recovery/painted-transparent-atlas.png",sha256:hash(fs.readFileSync(atlas)),generationMode:"built-in image_gen edit",transparentBackground:true},
  prompt:{path:"UI-Production/PrismV4/raster-recovery/prompt.txt",sha256:hash(fs.readFileSync(path.join(__dirname,"prompt.txt")))},
  extractors:["UI-Production/PrismV4/raster-recovery/Extract-Frames.ps1","UI-Production/PrismV4/raster-recovery/Extract-Gauges.ps1"],
  legacySvg:art.legacyVectorSources,
  notes:["No new skill or role icon artwork; no baked text.","Badge-occluded rim segments continue adjacent painted rim pixels; occluded backplate extends clean side-panel pixels.","Gauge masks and full fills retain their canonical geometry and continuous pointed endpoints."]
};
art.hub.inner=frameReport.hubInnerPolygon;
art.hub.innerPolygon=frameReport.hubInnerPolygon;
art.hub.innerBounds=[35,37,312,313];
art.skills.inner=frameReport.slotInnerPolygon;
art.skills.innerBounds=[20,20,152,152];

for(const asset of art.assets) {
  const png=fs.readFileSync(path.join(pack,asset.png));
  const svg=fs.readFileSync(path.join(pack,asset.svg));
  const stats=alphaStatistics(png);
  if(JSON.stringify(stats.canvas)!==JSON.stringify(asset.canvas))throw new Error("Canvas contract changed: "+asset.id);
  asset.pngSha256=hash(png);asset.svgSha256=hash(svg);
  asset.transparentPixels=stats.transparentPixels;asset.translucentPixels=stats.translucentPixels;
  const painted=frameIds.has(asset.id)||gaugeIds.has(asset.id);
  asset.svgRole=painted?"legacy structural reference; does not reproduce the current PNG":"retained deterministic structural state/mask source";
  asset.pngProvenance=painted?{
    method:/inner-mask$/.test(asset.id)?"geometric alpha mask for current painted extraction":"mechanical extraction or deterministic appearance state derived from painted atlas",
    atlas:art.provenance.paintedAtlas.path,
    extractor:frameIds.has(asset.id)?art.provenance.extractors[0]:art.provenance.extractors[1]
  }:{method:"retained deterministic structural state overlay/mask"};
  if(asset.id.startsWith("Skills/slot-")) {asset.innerPolygon=frameReport.slotInnerPolygon;asset.innerBounds=[20,20,152,152];}
  if(asset.id.startsWith("Party/role-")){asset.innerPolygon=frameReport.roleInnerPolygon;asset.innerBounds=[14,14,68,68];}
  if(["Hub/hub-frame","Hub/hub-background","Hub/hub-inner-mask"].includes(asset.id)){asset.innerPolygon=frameReport.hubInnerPolygon;asset.innerBounds=[35,37,312,313];}
}
art.provenance.paintedExtractionAssetCount=art.assets.filter(asset=>asset.pngProvenance.atlas).length;
art.provenance.retainedStateAssetCount=art.assets.length-art.provenance.paintedExtractionAssetCount;
art.note="Current PNGs are authoritative. Legacy SVG files preserve historical structural references only. Preview text is composited separately. New skill/role icon artwork is excluded.";
fs.writeFileSync(manifestPath,JSON.stringify(art,null,2)+"\n");
console.log(JSON.stringify({assets:art.assets.length,paintedExtraction:art.provenance.paintedExtractionAssetCount,retainedStates:art.provenance.retainedStateAssetCount,manifest:manifestPath}));
