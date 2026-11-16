"use strict";

// Historical structural-vector generator. The current painted PNGs are derived
// from the separately generated transparent atlas and must not be overwritten.
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");

const WORKSPACE = path.resolve(__dirname, "../..");
const ROOT = path.join(WORKSPACE, "Assets/UI/AstellaPrismV4");
const SOURCE = path.join(ROOT, "Source");
const SPRITES = path.join(ROOT, "Sprites");
const PREVIEW = path.join(__dirname, "preview");
const PAINTED_ATLAS = path.join(__dirname, "raster-recovery", "painted-transparent-atlas.png");
if (fs.existsSync(PAINTED_ATLAS)) {
  throw new Error("Painted Prism sprites are active. This legacy SVG generator is blocked to preserve them. Use raster-recovery/Extract-Frames.ps1 and Extract-Gauges.ps1, then update-painted-manifest.cjs instead.");
}
const { Resvg } = require("@resvg/resvg-js");
const assets = [];
const renderedAssets = new Map();

const geometry = {
  boss: {
    canvas: [1280, 96],
    outer: [[14,48],[44,22],[1236,22],[1266,48],[1236,74],[44,74]],
    inner: [[27,48],[49,29],[1231,29],[1253,48],[1231,67],[49,67]],
    innerBounds: [27,29,1226,38], movingTipWidth: 22,
    frame: "Boss/boss-frame", track: "Boss/boss-track", fill: "Boss/boss-fill",
    endcaps: [
      { asset: "Boss/boss-gem-left", rect: [-16,0,96,96] },
      { asset: "Boss/boss-gem-right", rect: [1200,0,96,96] }
    ]
  },
  cast: {
    canvas: [512,48],
    outer: [[4,24],[17,17],[495,17],[508,24],[495,31],[17,31]],
    inner: [[15,24],[22,20],[490,20],[497,24],[490,28],[22,28]],
    innerBounds: [15,20,482,8], movingTipWidth: 7,
    frame: "Boss/cast-frame", track: "Boss/cast-track", fill: "Boss/cast-fill"
  },
  player: {
    canvas: [1024,96],
    outer: [[12,48],[60,10],[964,10],[1012,48],[964,86],[60,86]],
    inner: [[29,48],[65,18],[959,18],[995,48],[959,78],[65,78]],
    innerBounds: [29,18,966,60], movingTipWidth: 36,
    frame: "Player/gauge-frame", track: "Player/gauge-track",
    fills: { hp: "Player/hp-fill", resource: "Player/resource-fill" },
    endcaps: []
  },
  party: {
    canvas: [320,40],
    outer: [[4,20],[20,6],[300,6],[316,20],[300,34],[20,34]],
    inner: [[14,20],[24,12],[296,12],[306,20],[296,28],[24,28]],
    innerBounds: [14,12,292,16], movingTipWidth: 10,
    frame: "Party/bar-frame", track: "Party/bar-track", fill: "Party/bar-fill"
  },
  slot: {
    canvas: [192,192],
    outer: [[28,8],[164,8],[184,28],[184,164],[164,184],[28,184],[8,164],[8,28]],
    inner: [[31,16],[161,16],[176,31],[176,161],[161,176],[31,176],[16,161],[16,31]],
    innerBounds: [16,16,160,160]
  },
  role: {
    canvas: [96,96],
    outer: [[18,6],[78,6],[90,18],[90,78],[78,90],[18,90],[6,78],[6,18]],
    inner: [[21,13],[75,13],[83,21],[83,75],[75,83],[21,83],[13,75],[13,21]]
  },
  hub: {
    canvas: [384,384],
    outer: [[100,8],[284,8],[376,100],[376,284],[284,376],[100,376],[8,284],[8,100]],
    inner: [[108,25],[276,25],[359,108],[359,276],[276,359],[108,359],[25,276],[25,108]],
    crystalBounds: [102,42,180,302], badgeRect: [128,-24,128,80]
  }
};

function polygon(points) { return "M" + points.map(point => point.join(" ")).join("L") + "Z"; }
function poly(points, fill, extra = "") { return `<path d="${polygon(points)}" fill="${fill}" ${extra}/>`; }
function xml(value) { return String(value).replace(/[&<>"']/g, character => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&apos;"})[character]); }
function sha(buffer) { return crypto.createHash("sha256").update(buffer).digest("hex"); }

function definitions() {
  return `<defs>
    <linearGradient id="metal" x1="0%" y1="0%" x2="0%" y2="100%"><stop stop-color="#eee3df"/><stop offset=".25" stop-color="#cec2ca"/><stop offset=".57" stop-color="#aaa0b1"/><stop offset="1" stop-color="#c7b7b8"/></linearGradient>
    <linearGradient id="metal-hover" x1="0%" y1="0%" x2="0%" y2="100%"><stop stop-color="#f4eae5"/><stop offset=".45" stop-color="#d5c9d6"/><stop offset="1" stop-color="#c9b8c2"/></linearGradient>
    <linearGradient id="metal-pressed" x1="0%" y1="0%" x2="0%" y2="100%"><stop stop-color="#aaa1b7"/><stop offset=".6" stop-color="#8b819b"/><stop offset="1" stop-color="#beb0c1"/></linearGradient>
    <linearGradient id="metal-focused" x1="0%" y1="0%" x2="0%" y2="100%"><stop stop-color="#f0e1d5"/><stop offset=".5" stop-color="#d6c7cb"/><stop offset="1" stop-color="#c5afb7"/></linearGradient>
    <linearGradient id="metal-disabled" x1="0%" y1="0%" x2="0%" y2="100%"><stop stop-color="#9592a5"/><stop offset="1" stop-color="#625e75"/></linearGradient>
    <linearGradient id="glass" x1="0%" y1="0%" x2="80%" y2="100%"><stop stop-color="#242441"/><stop offset=".5" stop-color="#20213c"/><stop offset="1" stop-color="#15182e"/></linearGradient>
    <linearGradient id="track" x1="0%" y1="0%" x2="100%" y2="30%"><stop stop-color="#302a56"/><stop offset=".55" stop-color="#282443"/><stop offset="1" stop-color="#332044"/></linearGradient>
    <linearGradient id="hp" x1="0%" y1="50%" x2="100%" y2="50%"><stop stop-color="#4e9798"/><stop offset=".42" stop-color="#83c6ad"/><stop offset="1" stop-color="#badd9e"/></linearGradient>
    <linearGradient id="critical" x1="0%" y1="50%" x2="100%" y2="50%"><stop stop-color="#8a3b52"/><stop offset=".45" stop-color="#bd586b"/><stop offset="1" stop-color="#e19a9c"/></linearGradient>
    <linearGradient id="resource" x1="0%" y1="0%" x2="100%" y2="65%"><stop stop-color="#8b91df"/><stop offset=".48" stop-color="#a48be3"/><stop offset="1" stop-color="#5c388e"/></linearGradient>
    <linearGradient id="boss" x1="0%" y1="50%" x2="100%" y2="50%"><stop stop-color="#7a42b3"/><stop offset=".3" stop-color="#ac4bd2"/><stop offset=".7" stop-color="#cf498e"/><stop offset="1" stop-color="#e86b98"/></linearGradient>
    <linearGradient id="cast" x1="0%" y1="0%" x2="100%" y2="30%"><stop stop-color="#7479cf"/><stop offset=".6" stop-color="#a79ce6"/><stop offset="1" stop-color="#d1c7ef"/></linearGradient>
    <linearGradient id="surface-light" x1="0%" y1="0%" x2="0%" y2="100%"><stop stop-color="#ffffff" stop-opacity=".34"/><stop offset=".46" stop-color="#ffffff" stop-opacity=".015"/><stop offset="1" stop-color="#101029" stop-opacity=".23"/></linearGradient>
    <linearGradient id="crystal" x1="0%" y1="0%" x2="90%" y2="100%"><stop stop-color="#e5d9ff"/><stop offset=".4" stop-color="#aa7cf0"/><stop offset=".7" stop-color="#7446b8"/><stop offset="1" stop-color="#d4a5e8"/></linearGradient>
    <linearGradient id="crystal-light" x1="0%" y1="0%" x2="15%" y2="100%"><stop stop-color="#f4e7ff"/><stop offset=".45" stop-color="#cfb0ff"/><stop offset="1" stop-color="#f0d2dc"/></linearGradient>
    <radialGradient id="hub-glass" cx="46%" cy="58%" r="74%"><stop stop-color="#5e3d91"/><stop offset=".64" stop-color="#39275e"/><stop offset="1" stop-color="#251c43"/></radialGradient>
    <radialGradient id="prism-glow"><stop stop-color="#d0a4f6" stop-opacity=".3"/><stop offset=".55" stop-color="#a471db" stop-opacity=".1"/><stop offset="1" stop-color="#8050b6" stop-opacity="0"/></radialGradient>
    <radialGradient id="prism-refraction"><stop stop-color="#f0defa" stop-opacity=".24"/><stop offset=".45" stop-color="#d2abf4" stop-opacity=".06"/><stop offset="1" stop-color="#c091ed" stop-opacity="0"/></radialGradient>
    <linearGradient id="facet-top-left" gradientUnits="userSpaceOnUse" x1="138" y1="80" x2="189" y2="204"><stop stop-color="#d8c4f5"/><stop offset="1" stop-color="#9971ca"/></linearGradient>
    <linearGradient id="facet-top-center" gradientUnits="userSpaceOnUse" x1="177" y1="42" x2="201" y2="201"><stop stop-color="#f0e1ff"/><stop offset=".46" stop-color="#d2b5f3"/><stop offset="1" stop-color="#c0a0e5"/></linearGradient>
    <linearGradient id="facet-top-right" gradientUnits="userSpaceOnUse" x1="195" y1="64" x2="275" y2="199"><stop stop-color="#cba5ed"/><stop offset="1" stop-color="#8b60bd"/></linearGradient>
    <linearGradient id="facet-bottom-left" gradientUnits="userSpaceOnUse" x1="136" y1="189" x2="195" y2="344"><stop stop-color="#9c72cc"/><stop offset="1" stop-color="#684194"/></linearGradient>
    <linearGradient id="facet-bottom-center" gradientUnits="userSpaceOnUse" x1="186" y1="192" x2="194" y2="344"><stop stop-color="#bda3e4"/><stop offset=".48" stop-color="#c5a6e7"/><stop offset="1" stop-color="#efd2e8"/></linearGradient>
    <linearGradient id="facet-bottom-right" gradientUnits="userSpaceOnUse" x1="267" y1="191" x2="193" y2="344"><stop stop-color="#8f61c0"/><stop offset="1" stop-color="#bd8bd1"/></linearGradient>
  </defs>`;
}

function svg(canvas, body) {
  return `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="${canvas[0]}" height="${canvas[1]}" viewBox="0 0 ${canvas[0]} ${canvas[1]}">${definitions()}${body}</svg>\n`;
}

let bevelNumber = 0;
function bevelFrame(outer, inner, state = "normal") {
  const frameId = ++bevelNumber;
  const yValues = outer.map(point => point[1]);
  const xValues = outer.map(point => point[0]);
  const centerY = (Math.min(...yValues) + Math.max(...yValues)) / 2;
  const centerX = (Math.min(...xValues) + Math.max(...xValues)) / 2;
  const gradients = [];
  const facets = outer.map((point, index) => {
    const next = (index + 1) % outer.length;
    const ox = (point[0] + outer[next][0]) / 2;
    const oy = (point[1] + outer[next][1]) / 2;
    const ix = (inner[index][0] + inner[next][0]) / 2;
    const iy = (inner[index][1] + inner[next][1]) / 2;
    const horizontal = Math.abs(outer[next][0] - point[0]) > Math.abs(outer[next][1] - point[1]);
    let colors = horizontal ? (oy < centerY ? ["#f0e5df","#d3c8d2","#a79aaf","#63566b"] : ["#877b90","#c2b2be","#d3c4cd","#5e5068"]) : (ox < centerX ? ["#b7adbf","#e2d7dd","#b6a9bb","#5c5068"] : ["#84778e","#c8b9ca","#a797ae","#564a61"]);
    if (state === "disabled") colors = ["#9791a6","#aaa2b4","#80768e","#50475e"];
    if (state === "pressed") colors = ["#8c8199","#b4a5bb","#93849f","#483e55"];
    if (state === "focused") colors = colors.map((color,i) => i === 1 ? "#e0cbd0" : color);
    if (state === "hover") colors = colors.map((color,i) => i === 1 ? "#e5dbe4" : color);
    const id = "metal-face-" + frameId + "-" + index;
    gradients.push(`<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${ox}" y1="${oy}" x2="${ix}" y2="${iy}"><stop stop-color="${colors[0]}"/><stop offset=".23" stop-color="${colors[1]}"/><stop offset=".77" stop-color="${colors[2]}"/><stop offset="1" stop-color="${colors[3]}"/></linearGradient>`);
    return poly([point,outer[next],inner[next],inner[index]],"url(#"+id+")");
  }).join("");
  const baseGradient = state === "normal" ? "metal" : "metal-" + state;
  return `<defs>${gradients.join("")}</defs><path d="${polygon(outer)}${polygon(inner)}" fill="url(#${baseGradient})" fill-rule="evenodd"/>${facets}<path d="${polygon(outer)}" fill="none" stroke="#ddd4de" stroke-opacity=".64" stroke-width=".7"/><path d="${polygon(inner)}" fill="none" stroke="#342a43" stroke-opacity=".78" stroke-width="1.2"/>`;
}

function fillArtwork(gauge, kind) {
  const [w,h] = gauge.canvas;
  const inner = polygon(gauge.inner);
  const planeColors = kind === "hp" ? ["#e5f4be", "#397b89"] : kind === "critical" ? ["#f0c2bd", "#76213d"] : ["#ded0ff", "#4c2c7f"];
  return `<defs><clipPath id="fill-clip"><path d="${inner}"/></clipPath></defs><path d="${inner}" fill="url(#${kind})"/><g clip-path="url(#fill-clip)">${Array.from({length:7}, (_,i) => {
    const x = (i / 7) * w;
    return poly([[x-25,h*.18],[x+w*.18,h*.18],[x+w*.05,h*.67],[x-w*.05,h*.55]], planeColors[i%2], `opacity="${i%2 ? .035 : .04}"`);
  }).join("")}<rect width="${w}" height="${h}" fill="url(#surface-light)"/><path d="${inner}" fill="none" stroke="#e9e3ef" stroke-opacity=".25" stroke-width="1.2"/></g>`;
}

function add(id, canvas, body, extra = {}) {
  const svgName = id + ".svg";
  const pngName = id + ".png";
  const source = svg(canvas, body);
  if (/<text\b/i.test(source)) throw new Error("Text baked into production asset: " + id);
  const render = new Resvg(source, {font:{loadSystemFonts:false}, shapeRendering:2}).render();
  const pixels = render.pixels;
  let transparent = 0, translucent = 0, nonzero = 0;
  for (let i=3; i<pixels.length; i+=4) {
    if (pixels[i] === 0) transparent++;
    else { nonzero++; if (pixels[i] !== 255) translucent++; }
  }
  if (!transparent || !nonzero) throw new Error("Asset must have visible art and actual alpha: " + id);
  if (render.width !== canvas[0] || render.height !== canvas[1]) throw new Error("Canvas mismatch: " + id);
  const png = render.asPng();
  const pngPath = path.join(SPRITES, pngName);
  const svgPath = path.join(SOURCE, svgName);
  fs.mkdirSync(path.dirname(pngPath), {recursive:true});
  fs.mkdirSync(path.dirname(svgPath), {recursive:true});
  fs.writeFileSync(svgPath, source);
  fs.writeFileSync(pngPath, png);
  const item = {
    id, png:"Sprites/" + pngName, svg:"Source/" + svgName, canvas,
    pivot:[.5,.5], border:[0,0,0,0], preserveAspect:true,
    rgba:true, textBakedIntoAsset:false, transparentPixels:transparent,
    translucentPixels:translucent, pngSha256:sha(png), svgSha256:sha(source),
    ...extra
  };
  assets.push(item);
  renderedAssets.set(id, {item, png, pixels});
}

function createGauges() {
  for (const [name, gauge] of Object.entries(geometry)) {
    if (!gauge.frame) continue;
    const contract = {innerBounds:gauge.innerBounds, innerPolygon:gauge.inner, movingTipWidth:gauge.movingTipWidth};
    add(gauge.frame, gauge.canvas, bevelFrame(gauge.outer,gauge.inner), {...contract, layer:"fixed transparent frame", transparentInterior:true});
    add(gauge.track, gauge.canvas, poly(gauge.inner,"url(#track)"), {...contract, layer:"full pointed track"});
    if (name === "player") {
      add(gauge.fills.hp, gauge.canvas, fillArtwork(gauge,"hp"), {...contract, layer:"full 100% health fill; runtime mask changes ratio"});
      add(gauge.fills.resource, gauge.canvas, fillArtwork(gauge,"resource"), {...contract, layer:"full 100% job-resource fill; runtime mask changes ratio"});
    } else {
      add(gauge.fill, gauge.canvas, fillArtwork(gauge,name === "party" ? "hp" : name), {...contract, layer:"full 100% fill; runtime mask changes ratio"});
    }
  }
  const diamond = [[48,8],[91,48],[48,88],[5,48]];
  const diamondInner = [[48,17],[80,48],[48,79],[16,48]];
  const gem = bevelFrame(diamond,diamondInner) + poly(diamondInner,"url(#crystal)")
    + poly([[48,17],[48,48],[16,48]],"#c7b2f5",'opacity=".9"')
    + poly([[48,17],[80,48],[48,48]],"#a684e0")
    + poly([[16,48],[48,48],[48,79]],"#7651ad")
    + poly([[48,48],[80,48],[48,79]],"#b89ad9")
    + `<path d="M48 17L48 79M16 48H80" fill="none" stroke="#f3dfff" stroke-width="1.2" stroke-opacity=".6"/>`;
  add("Boss/boss-gem-left",[96,96],gem,{layer:"boss-only detached endcap"});
  add("Boss/boss-gem-right",[96,96],`<g transform="translate(96 0) scale(-1 1)">${gem}</g>`,{layer:"boss-only detached endcap"});
}

function createRoleSlots() {
  const role = geometry.role;
  add("Party/role-frame",role.canvas,bevelFrame(role.outer,role.inner),{innerPolygon:role.inner,layer:"fixed role-slot frame"});
  add("Party/role-background",role.canvas,poly(role.inner,"url(#glass)"),{innerPolygon:role.inner,layer:"empty role-slot backplate; artwork excluded"});
  add("Party/role-inner-mask",role.canvas,poly(role.inner,"#ffffff"),{innerPolygon:role.inner,layer:"white alpha role-slot mask; artwork excluded"});
}

function createHub() {
  const hub = geometry.hub;
  add("Hub/hub-frame",hub.canvas,bevelFrame(hub.outer,hub.inner),{innerPolygon:hub.inner,layer:"fixed octagon frame",transparentInterior:true});
  const clip = `<defs><clipPath id="hub-clip"><path d="${polygon(hub.inner)}"/></clipPath></defs>`;
  const backdrop = poly(hub.inner,"url(#hub-glass)") + `<g clip-path="url(#hub-clip)">${poly([[25,108],[192,26],[192,192],[25,276]],"#7553a7",'opacity=".1"')}${poly([[192,26],[359,108],[192,192]],"#a17bc3",'opacity=".07"')}${poly([[192,192],[359,276],[276,359],[108,359]],"#6d3d9a",'opacity=".15"')}<ellipse cx="192" cy="217" rx="135" ry="153" fill="url(#prism-glow)"/></g>`;
  add("Hub/hub-background",hub.canvas,clip+backdrop,{innerPolygon:hub.inner,layer:"full octagon glass backplate"});
  const crystal = `<defs><clipPath id="prism-clip"><path d="M192 42L282 192L192 344L102 192Z"/></clipPath></defs>`
    + poly([[192,42],[282,192],[192,344],[102,192]],"url(#crystal)")
    + poly([[192,42],[168,192],[102,192]],"url(#facet-top-left)")
    + poly([[192,42],[216,192],[168,192]],"url(#facet-top-center)")
    + poly([[192,42],[282,192],[216,192]],"url(#facet-top-right)")
    + poly([[102,192],[168,192],[192,344]],"url(#facet-bottom-left)")
    + poly([[168,192],[216,192],[192,344]],"url(#facet-bottom-center)")
    + poly([[216,192],[282,192],[192,344]],"url(#facet-bottom-right)")
    + `<g clip-path="url(#prism-clip)"><ellipse cx="192" cy="185" rx="64" ry="127" fill="url(#prism-refraction)"/></g><path d="M192 42L282 192L192 344L102 192Z" fill="none" stroke="#dac3ed" stroke-width="1.2" stroke-opacity=".72"/><path d="M102 192H282M192 42L168 192L192 344M192 42L216 192L192 344" fill="none" stroke="#e9d4f4" stroke-width=".8" stroke-opacity=".3"/>`;
  add("Hub/hub-crystal",hub.canvas,crystal,{crystalBounds:hub.crystalBounds,majorFacetCount:6,layer:"separate faceted amethyst"});
  const badgeOuter = [[24,8],[104,8],[120,24],[120,56],[104,72],[24,72],[8,56],[8,24]];
  const badgeInner = [[27,15],[101,15],[113,27],[113,53],[101,65],[27,65],[15,53],[15,27]];
  add("Hub/ultimate-badge",[128,80],poly(badgeInner,"url(#glass)")+bevelFrame(badgeOuter,badgeInner),{layer:"blank V badge; text supplied by runtime",textRect:[23,16,82,48]});
}

function createSlots() {
  const slot = geometry.slot;
  const inner = slot.inner;
  add("Skills/slot-background",slot.canvas,poly(inner,"url(#glass)")+poly([[16,133],[150,16],[176,16],[176,55],[55,176],[16,176]],"#7970b0",'opacity=".08"'),{layer:"slot backplate"});
  for (const state of ["normal","hover","pressed","focused","disabled"]) {
    add("Skills/slot-frame-"+state,slot.canvas,bevelFrame(slot.outer,inner,state),{layer:"one active state frame",state,innerPolygon:inner,transparentInterior:true});
  }
  add("Skills/slot-inner-mask",slot.canvas,poly(inner,"#ffffff"),{layer:"white alpha icon mask",innerPolygon:inner});
  for (const [id,w] of [["key-badge",72],["key-badge-wide",112]]) {
    const outer = [[11,2],[w-11,2],[w-3,10],[w-3,38],[w-11,46],[11,46],[3,38],[3,10]];
    const innerBadge = [[13,7],[w-13,7],[w-8,12],[w-8,36],[w-13,41],[13,41],[8,36],[8,12]];
    add("Skills/"+id,[w,48],poly(innerBadge,"url(#glass)")+bevelFrame(outer,innerBadge),{layer:"blank runtime key badge",textRect:[12,8,w-24,32]});
  }
  add("States/cooldown-mask",slot.canvas,poly(inner,"#ffffff"),{layer:"alpha mask for radial cooldown; runtime supplies ratio"});
  add("States/cooldown-overlay",slot.canvas,poly(inner,"#090b20",'opacity=".76"'),{layer:"radial-fill cooldown shade; no baked timer"});
  add("States/disabled-overlay",slot.canvas,poly(inner,"#42435f",'opacity=".58"'),{layer:"disabled desaturation/tint overlay"});
  add("States/hover-overlay",slot.canvas,poly(inner,"#e4d7f1",'opacity=".1"'),{layer:"subtle hover glass light"});
}

function createDynamicLayers() {
  for (const [name,id] of [["boss","Boss/boss-inner-mask"],["cast","Boss/cast-inner-mask"],["player","Player/gauge-inner-mask"],["party","Party/bar-inner-mask"],["hub","Hub/hub-inner-mask"]]) {
    const shape = geometry[name];
    add(id,shape.canvas,poly(shape.inner,"#ffffff"),{innerPolygon:shape.inner,layer:"white alpha inner mask"});
    shape.innerMask = id;
  }
  add("Hub/ready-glow",geometry.hub.canvas,`<defs><clipPath id="ready-clip"><path d="${polygon(geometry.hub.inner)}"/></clipPath></defs><g clip-path="url(#ready-clip)"><ellipse cx="192" cy="200" rx="164" ry="179" fill="url(#prism-glow)"/></g>`,{layer:"optional readiness light inside hub only"});
  add("Hub/disabled-overlay",geometry.hub.canvas,poly(geometry.hub.inner,"#252338",'opacity=".62"'),{innerPolygon:geometry.hub.inner,layer:"optional unavailable hub tint"});
  add("Party/bar-fill-critical",geometry.party.canvas,fillArtwork(geometry.party,"critical"),{innerBounds:geometry.party.innerBounds,innerPolygon:geometry.party.inner,movingTipWidth:geometry.party.movingTipWidth,layer:"critical-health alternative full fill"});
  add("Party/selected-overlay",geometry.party.canvas,poly(geometry.party.inner,"#ddd0ed",'opacity=".16"')+`<path d="${polygon(geometry.party.inner)}" fill="none" stroke="#dfcce5" stroke-width="1.2" stroke-opacity=".34"/>`,{innerPolygon:geometry.party.inner,layer:"optional selected party-bar light"});
  add("Party/dead-overlay",geometry.party.canvas,poly(geometry.party.inner,"#1a192b",'opacity=".72"'),{innerPolygon:geometry.party.inner,layer:"optional defeated party-bar tint"});
}

function assetImage(id,x,y,w,h,extra="") {
  const asset = renderedAssets.get(id);
  if (!asset) throw new Error("Unknown assembly asset: "+id);
  return `<image x="${x}" y="${y}" width="${w}" height="${h}" xlink:href="data:image/png;base64,${asset.png.toString("base64")}" ${extra}/>`;
}

// Runtime reference algorithm: intersect the original full pointed polygon
// with a moving right tip. At 1 use the complete authored polygon; at 0 hide it.
function ratioMask(gauge,ratio) {
  const [left,top,width,height] = gauge.innerBounds;
  const end = left + width*ratio;
  const tip = Math.min(gauge.movingTipWidth,width*ratio);
  if (ratio<=0) return "";
  if (ratio>=1) return polygon(gauge.inner);
  return polygon([[left,top],[end-tip,top],[end,top+height/2],[end-tip,top+height],[left,top+height]]);
}

let clipNumber = 0;
function composedGauge(gauge,x,y,w,h,ratio,fill) {
  const id = "assembly-clip-"+(++clipNumber);
  const [nativeW,nativeH] = gauge.canvas;
  const innerMask = "assembly-inner-"+clipNumber;
  const definitions = `<defs><clipPath id="${id}"><path d="${ratioMask(gauge,ratio)}"/></clipPath><clipPath id="${innerMask}"><path d="${polygon(gauge.inner)}"/></clipPath></defs>`;
  const content = assetImage(gauge.track,0,0,nativeW,nativeH)
    + (ratio>0 ? `<g clip-path="url(#${innerMask})"><g clip-path="url(#${id})">${assetImage(fill || gauge.fill,0,0,nativeW,nativeH)}</g></g>` : "")
    + assetImage(gauge.frame,0,0,nativeW,nativeH);
  return `<svg x="${x}" y="${y}" width="${w}" height="${h}" viewBox="0 0 ${nativeW} ${nativeH}">${definitions}${content}</svg>`;
}

function previewText(x,y,text,size=22,anchor="start",color="#e6dfe9") {
  return `<text x="${x}" y="${y}" fill="${color}" font-family="Segoe UI,Arial,sans-serif" font-size="${size}" text-anchor="${anchor}">${xml(text)}</text>`;
}

function savePreview(name,canvas,body) {
  const source = `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="${canvas[0]}" height="${canvas[1]}" viewBox="0 0 ${canvas[0]} ${canvas[1]}">${body}</svg>\n`;
  fs.mkdirSync(PREVIEW,{recursive:true});
  fs.writeFileSync(path.join(PREVIEW,name+".svg"),source);
  fs.writeFileSync(path.join(PREVIEW,name+".png"),new Resvg(source,{font:{loadSystemFonts:true,defaultFontFamily:"Segoe UI"}}).render().asPng());
}

function createPreviews() {
  clipNumber=0;
  let body = `<rect width="1672" height="941" fill="#0d1021"/>`;
  body += composedGauge(geometry.boss,372,70,960,72,1);
  body += assetImage("Boss/boss-gem-left",360,70,72,72)+assetImage("Boss/boss-gem-right",1272,70,72,72);
  body += previewText(852,63,"PRISM WARDEN",23,"middle")+previewText(852,115,"100%",24,"middle");
  body += composedGauge(geometry.cast,575,133,512,48,.6);
  ["AEGIS","LYRA","NOX","IRIS"].forEach((name,i) => {
    const y=82+i*74;
    body += assetImage("Party/role-background",44,y,68,68)+assetImage("Party/role-frame",44,y,68,68);
    body += previewText(126,y+24,name,20)+composedGauge(geometry.party,120,y+27,174,22,.88);
  });
  body += composedGauge(geometry.player,64,618,620,58,1,geometry.player.fills.hp);
  body += composedGauge(geometry.player,986,618,620,58,.6,geometry.player.fills.resource);
  body += previewText(153,655,"HP",23)+previewText(378,655,"48,639",30,"middle")+previewText(1296,655,"60 / 100",30,"middle");
  body += assetImage("Hub/hub-background",700,610,272,272)+assetImage("Hub/hub-crystal",700,610,272,272)+assetImage("Hub/hub-frame",700,610,272,272);
  body += assetImage("Hub/ultimate-badge",790.7,593,90.7,56.7)+previewText(836,632,"V",30,"middle");
  [82,236,390,544,1000,1154,1308,1462].forEach((x,i) => {
    body += assetImage("Skills/slot-background",x,710,136,136)+assetImage("Skills/slot-frame-normal",x,710,136,136);
    const key=["1","2","3","4","Q","Space","R","F"][i];
    const badgeW=key==="Space"?79.3:51;
    body += assetImage(key==="Space"?"Skills/key-badge-wide":"Skills/key-badge",x+(136-badgeW)/2,695,badgeW,34);
    body += previewText(x+68,720,key,key==="Space"?18:22,"middle");
  });
  savePreview("component-assembly",[1672,941],body);

  let fills = `<rect width="1600" height="460" fill="#101326"/>${previewText(32,40,"POINTED GAUGE CHECK / separate full-width fill, track and frame",24)}`;
  [0,.5,1].forEach((ratio,i) => {
    const x=32+i*524;
    fills += previewText(x+248,88,String(ratio*100)+"%",23,"middle");
    fills += composedGauge(geometry.player,x,110,496,46.5,ratio,geometry.player.fills.hp);
    fills += composedGauge(geometry.player,x,200,496,46.5,ratio,geometry.player.fills.resource);
    fills += composedGauge(geometry.boss,x,290,496,37.2,ratio);
    fills += previewText(x,385,"Frame stays fixed; texture never squeezed",17);
  });
  fills += previewText(32,430,"100% reaches both triangular tips. 0% keeps the full pointed track. Text exists in this preview only.",18);
  savePreview("fill-validation",[1600,460],fills);

  const columns=6, tile=230, rows=Math.ceil(assets.length/columns);
  let sheet=`<rect width="${columns*tile}" height="${rows*180+50}" fill="#101326"/>${previewText(18,30,"PRISM V4 / TEXT-FREE COMPONENTS",20)}`;
  assets.forEach((asset,index) => {
    const x=(index%columns)*tile+12, y=Math.floor(index/columns)*180+50;
    const scale=Math.min(204/asset.canvas[0],126/asset.canvas[1]);
    sheet += assetImage(asset.id,x+(204-asset.canvas[0]*scale)/2,y,asset.canvas[0]*scale,asset.canvas[1]*scale);
    sheet += previewText(x,y+150,asset.id,12);
  });
  savePreview("component-contact-sheet",[columns*tile,rows*180+50],sheet);
}

createGauges();
createRoleSlots();
createHub();
createSlots();
createDynamicLayers();
createPreviews();

const manifest = {
  version:1,
  name:"Astella Prism V4 / approved restrained-detail reconstruction",
  reconstruction:"New deterministic vector reconstruction; not recovered original SVG art.",
  approvedReference:"UI-Concepts/2026-11-16-prism-v4/02-continuous-fill.png",
  generator:"UI-Production/PrismV4/generate-prism.cjs",
  renderer:"@resvg/resvg-js@2.6.2",
  coordinateSystem:{origin:"top-left",units:"native pixels",pivot:[.5,.5],borderOrder:["left","bottom","right","top"]},
  productionRules:{textBakedIntoAssets:false,alpha:"straight RGBA",background:"transparent",scale:"preserve original aspect ratio",gems:"boss endcaps only",moonMotifs:false,iconArtwork:"excluded by user; empty role and skill slots only"},
  gauges:Object.fromEntries(["boss","cast","player","party"].map(name=>[name,{...geometry[name],innerPolygon:geometry[name].inner,fillRatio:"current / maximum",fillMode:"clip the fixed full-width fill to the full inner polygon and the moving pointed end",atZero:"hide fill; keep full track",atFull:"use complete inner polygon including both pointed tips",frameAndText:"fixed layers, outside fill clipping"}])),
  hub:{...geometry.hub,background:"Hub/hub-background",crystal:"Hub/hub-crystal",frame:"Hub/hub-frame",blankBadge:"Hub/ultimate-badge"},
  skills:{...geometry.slot,layerOrder:["Skills/slot-background","optional existing artwork supplied by main project; new icons excluded","States/cooldown-overlay (runtime radial fill)","one Skills/slot-frame-*","blank Skills/key-badge or key-badge-wide","runtime key / cooldown text"]},
  states:{hubReady:"Hub/ready-glow",hubDisabled:"Hub/disabled-overlay",partyCriticalFill:"Party/bar-fill-critical",partySelected:"Party/selected-overlay",partyDead:"Party/dead-overlay"},
  assets,
  previews:["UI-Production/PrismV4/preview/component-assembly.png","UI-Production/PrismV4/preview/fill-validation.png","UI-Production/PrismV4/preview/component-contact-sheet.png"],
  note:"Preview labels are assembled separately and are not baked into any production sprite or editable asset SVG. New skill/role icon artwork is excluded by user request. Central amethyst is retained as structural HUD decoration."
};
fs.mkdirSync(SOURCE,{recursive:true});
fs.writeFileSync(path.join(SOURCE,"art-manifest.json"),JSON.stringify(manifest,null,2)+"\n");
console.log(`Generated ${assets.length} text-free SVG/PNG pairs and 3 offline SVG/PNG previews.`);
console.log("Manifest: "+path.relative(WORKSPACE,path.join(SOURCE,"art-manifest.json")));
