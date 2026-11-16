"use strict";
const fs=require("node:fs");
const path=require("node:path");
const {Resvg}=require("@resvg/resvg-js");
const project=path.resolve(__dirname,"../../..");
const kit=path.join(project,"Assets/UI/AstellaPrismV4");
const out=path.resolve(__dirname,"../preview");
const manifest=JSON.parse(fs.readFileSync(path.join(kit,"prism-v4-manifest.json"),"utf8"));
const artwork=new Map(manifest.assets.map(asset=>[asset.id,{...asset,png:fs.readFileSync(path.join(kit,asset.png))}]));
let clipId=0;
function xml(value){return String(value).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&apos;"})[c]);}
function polygon(points){return "M"+points.map(point=>point.join(" ")).join("L")+"Z";}
function image(id,x,y,width,height){
 const item=artwork.get(id);if(!item)throw new Error("Unknown production PNG: "+id);
 return `<image x="${x}" y="${y}" width="${width}" height="${height}" xlink:href="data:image/png;base64,${item.png.toString("base64")}"/>`;
}
function text(x,y,value,size=22,anchor="start"){
 return `<text x="${x}" y="${y}" fill="#e6dfe9" font-family="Segoe UI,Arial,sans-serif" font-size="${size}" text-anchor="${anchor}">${xml(value)}</text>`;
}
function gauge(shape,x,y,width,height,ratio,fill){
 const [w,h]=shape.canvas;
 let content=image(shape.track,0,0,w,h);
 if(ratio>0){
  const source=image(fill||shape.fill,0,0,w,h);
  if(ratio>=1)content+=source;
  else{
   const [left,top,innerW,innerH]=shape.innerBounds,front=left+innerW*ratio,tip=Math.min(shape.movingTipWidth,innerW*ratio),id="painted-clip-"+(++clipId);
   const boundary=[[0,0],[front-tip,0],[front-tip,top],[front,top+innerH/2],[front-tip,top+innerH],[front-tip,h],[0,h]];
   content+=`<defs><clipPath id="${id}"><path d="${polygon(boundary)}"/></clipPath></defs><g clip-path="url(#${id})">${source}</g>`;
  }
 }
 content+=image(shape.frame,0,0,w,h);
 return `<svg x="${x}" y="${y}" width="${width}" height="${height}" viewBox="0 0 ${w} ${h}">${content}</svg>`;
}
function save(name,width,height,body){
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">${body}</svg>\n`;
 fs.mkdirSync(out,{recursive:true});
 fs.writeFileSync(path.join(out,name+".svg"),svg);
 fs.writeFileSync(path.join(out,name+".png"),new Resvg(svg,{font:{loadSystemFonts:true,defaultFontFamily:"Segoe UI"}}).render().asPng());
}

let body='<rect width="1672" height="941" fill="#0d1021"/>';
body+=gauge(manifest.gauges.boss,372,70,960,72,1);
body+=image("Boss/boss-gem-left",360,70,72,72)+image("Boss/boss-gem-right",1272,70,72,72);
body+=text(852,63,"PRISM WARDEN",23,"middle")+text(852,115,"100%",24,"middle");
body+=gauge(manifest.gauges.cast,575,133,512,48,.6);
["AEGIS","LYRA","NOX","IRIS"].forEach((name,i)=>{
 const y=82+i*74;
 body+=image("Party/role-background",44,y,68,68)+image("Party/role-frame",44,y,68,68);
 body+=text(126,y+24,name,20)+gauge(manifest.gauges.party,120,y+27,174,22,.88);
});
body+=gauge(manifest.gauges.player,64,618,620,58,1,manifest.gauges.player.fills.hp);
body+=gauge(manifest.gauges.player,986,618,620,58,.6,manifest.gauges.player.fills.resource);
body+=text(153,655,"HP",23)+text(378,655,"48,639",30,"middle")+text(1296,655,"60 / 100",30,"middle");
body+=image("Hub/hub-background",700,610,272,272)+image("Hub/hub-crystal",700,610,272,272)+image("Hub/hub-frame",700,610,272,272);
body+=image("Hub/ultimate-badge",790.7,593,90.7,56.7)+text(836,632,"V",30,"middle");
[82,236,390,544,1000,1154,1308,1462].forEach((x,i)=>{
 body+=image("Skills/slot-background",x,710,136,136)+image("Skills/slot-frame-normal",x,710,136,136);
 const key=["1","2","3","4","Q","Space","R","F"][i],badgeW=key==="Space"?79.3:51;
 body+=image(key==="Space"?"Skills/key-badge-wide":"Skills/key-badge",x+(136-badgeW)/2,695,badgeW,34);
 body+=text(x+68,720,key,key==="Space"?18:22,"middle");
});
save("component-assembly",1672,941,body);

let fills='<rect width="1600" height="460" fill="#101326"/>'+text(32,40,"PAINTED PNG / fixed full-width fill, track and frame",24);
[0,.5,1].forEach((ratio,i)=>{
 const x=32+i*524;
 fills+=text(x+248,88,String(ratio*100)+"%",23,"middle");
 fills+=gauge(manifest.gauges.player,x,110,496,46.5,ratio,manifest.gauges.player.fills.hp);
 fills+=gauge(manifest.gauges.player,x,200,496,46.5,ratio,manifest.gauges.player.fills.resource);
 fills+=gauge(manifest.gauges.boss,x,290,496,37.2,ratio);
 fills+=text(x,385,"Frame fixed; painted texture never squeezed",17);
});
fills+=text(32,430,"100% reaches both triangular tips. Text is composited for this preview only.",18);
save("fill-validation",1600,460,fills);

const columns=6,tile=230,rows=Math.ceil(manifest.assets.length/columns);
let sheet=`<rect width="${columns*tile}" height="${rows*180+50}" fill="#101326"/>`+text(18,30,"PAINTED PRISM V4 / CURRENT TEXT-FREE PNG COMPONENTS",20);
manifest.assets.forEach((asset,i)=>{
 const x=(i%columns)*tile+12,y=Math.floor(i/columns)*180+50,scale=Math.min(204/asset.canvas[0],126/asset.canvas[1]);
 sheet+=image(asset.id,x+(204-asset.canvas[0]*scale)/2,y,asset.canvas[0]*scale,asset.canvas[1]*scale);
 sheet+=text(x,y+150,asset.id,12);
});
save("component-contact-sheet",columns*tile,rows*180+50,sheet);
console.log("Built 3 painted previews from current production PNGs; no legacy SVG artwork rendered.");
