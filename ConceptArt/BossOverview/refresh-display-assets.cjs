const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const workspace=path.resolve(__dirname,'../..');
let chromium;
try{({chromium}=require('playwright-core'));}catch(error){
 try{({chromium}=require(path.join(workspace,'.utmp/boss-gallery/browser-check/node_modules/playwright-core')));}catch(fallback){throw new Error('Preview refresh requires playwright-core and Chrome. See MAINTENANCE.md.');}
}
const root=__dirname;
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const catalog=JSON.parse(fs.readFileSync(path.join(root,'catalog.json'),'utf8'));
const sources=catalog.cards.flatMap(c=>c.images.map(im=>({...im,cardTitle:c.title,absolute:path.resolve(root,im.src)})));
if(sources.length!==catalog.image_count)throw Error('Catalog image count mismatch');
if(new Set(sources.map(s=>s.id)).size!==sources.length)throw Error('Duplicate image ids');
// Verify every catalog source before creating any output directories or assets.
for(const s of sources){if(!/^[a-z0-9-]+$/i.test(s.id))throw Error('Unsafe asset id '+s.id);const b=fs.readFileSync(s.absolute);s.actualSha=hash(b);if(s.actualSha!==s.sha256)throw Error('Source/catalog SHA mismatch: '+s.id);s.sourceBytes=b.length;
 if(!s.absolute.startsWith(root+path.sep)){const copySrc='originals/current/'+s.id+'.png';fs.mkdirSync(path.dirname(path.join(root,copySrc)),{recursive:true});fs.writeFileSync(path.join(root,copySrc),b);s.sourceGallerySrc=s.src;s.src=copySrc;}
 s.sourceData='data:image/png;base64,'+b.toString('base64');}
async function convert(input){const im=new Image();im.src=input.url;await im.decode();const width=im.width,height=im.height;if(width!==input.expectedWidth||height!==input.expectedHeight)throw Error('Source/catalog dimensions mismatch');const make=(w,h,quality)=>{const c=document.createElement('canvas');c.width=w;c.height=h;const ctx=c.getContext('2d');ctx.fillStyle='#e8e5ef';ctx.fillRect(0,0,w,h);ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';ctx.drawImage(im,0,0,w,h);return c.toDataURL('image/jpeg',quality);};const thumbWidth=640,thumbHeight=Math.round(height*thumbWidth/width);return{width,height,thumbnail:{width:thumbWidth,height:thumbHeight,url:make(thumbWidth,thumbHeight,.87)},preview:{width,height,url:make(width,height,.94)}};}
(async()=>{const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});try{const page=await browser.newPage(),rows=[];
fs.mkdirSync(path.join(root,'thumbnails'),{recursive:true});fs.mkdirSync(path.join(root,'previews'),{recursive:true});
for(const s of sources){const rendered=await page.evaluate(convert,{url:s.sourceData,expectedWidth:s.width,expectedHeight:s.height});const row={id:s.id,title:s.cardTitle,label:s.label,sourceSrc:s.src,sourceSha256:s.actualSha,sourceBytes:s.sourceBytes,sourceWidth:rendered.width,sourceHeight:rendered.height};
 for(const kind of ['thumbnail','preview']){const r=rendered[kind];if(!r.url.startsWith('data:image/jpeg;base64,'))throw Error('JPEG encoding failed '+s.id);const bytes=Buffer.from(r.url.split(',')[1],'base64');if(bytes[0]!==255||bytes[1]!==216||bytes[2]!==255)throw Error('JPEG signature mismatch '+s.id);const src=`${kind==='thumbnail'?'thumbnails':'previews'}/${s.id}.jpg`;fs.writeFileSync(path.join(root,src),bytes);const saved=fs.readFileSync(path.join(root,src));const decoded=await page.evaluate(async src=>{const im=new Image();im.src=src;await im.decode();return{width:im.width,height:im.height};},'data:image/jpeg;base64,'+saved.toString('base64'));if(decoded.width!==r.width||decoded.height!==r.height)throw Error('Saved JPEG dimensions mismatch '+src);row[kind]={src,sha256:hash(saved),bytes:saved.length,width:r.width,height:r.height,quality:kind==='thumbnail'?.87:.94,mimeType:'image/jpeg'};}
 rows.push(row);
}
for(const s of sources)if(hash(fs.readFileSync(s.absolute))!==s.actualSha)throw Error('Original source changed during conversion '+s.id);
const manifest={version:1,createdAt:new Date().toISOString(),scope:'Current BossOverview catalog 20 images. Display JPEG cache only; original PNGs remain unchanged.',alphaBackground:'#e8e5ef',thumbnailWidth:640,thumbnailQuality:.87,previewQuality:.94,imageCount:rows.length,jpegCount:rows.length*2,verification:{sourceCatalogHashes:'all20 match before conversion',originalHashesAfter:'all20 preserved',savedJpegDecode:'40/40 pass',status:'pass'},images:rows};fs.writeFileSync(path.join(root,'previews/manifest.json'),JSON.stringify(manifest,null,2)+'\n');for(const card of catalog.cards)for(const im of card.images){
 const source=sources.find(s=>s.id===im.id),row=rows.find(r=>r.id===im.id);
 if(source.sourceGallerySrc)im.sourceGallerySrc=source.sourceGallerySrc;
 Object.assign(im,{src:source.src,thumbnailSrc:row.thumbnail.src,displaySrc:row.preview.src,previewSourceSha256:im.sha256});
}
catalog.display_assets={version:1,manifest:'previews/manifest.json',thumbnail_count:rows.length,preview_count:rows.length,originals:'Preserved PNGs; current image/download paths stay within this gallery folder.',cache_validation:'Use display cache only when previewSourceSha256 equals image sha256.'};
const indexFile=path.join(root,'INDEX.html');let html=fs.readFileSync(indexFile,'utf8');
const embedded=html.match(/const catalog = (\{.*?\});\s*const state =/s);if(!embedded)throw Error('Embedded catalog missing');
html=html.replace('const catalog = '+embedded[1]+';','const catalog = '+JSON.stringify(catalog).replaceAll('<','\\u003c')+';');
for(const card of catalog.cards)for(const im of card.images){
 const row=rows.find(r=>r.id===im.id),rx=new RegExp('(<button\\b[^>]*\\bdata-image="'+im.id+'"[^>]*>)([\\s\\S]*?)(<\\/button>)'),match=html.match(rx);
 if(!match)throw Error('Static thumbnail missing '+im.id);
 const img=match[2].match(/<img\b[^>]*>/);if(!img)throw Error('Thumbnail image missing '+im.id);
 let replacement=img[0].replace(/src="[^"]*"/,'src="'+im.thumbnailSrc+'"').replace(/width="[^"]*"/,'width="'+row.thumbnail.width+'"').replace(/height="[^"]*"/,'height="'+row.thumbnail.height+'"').replace(/loading="[^"]*"/,'loading="eager"');
 html=html.replace(match[0],match[1]+match[2].replace(img[0],replacement)+match[3]);
}
fs.writeFileSync(path.join(root,'catalog.json'),JSON.stringify(catalog,null,2)+'\n');fs.writeFileSync(indexFile,html);
console.log(JSON.stringify({status:'pass',sourceCount:rows.length,jpegCount:rows.length*2,originalPngsUnchanged:true,thumbnailBytes:rows.reduce((n,r)=>n+r.thumbnail.bytes,0),previewBytes:rows.reduce((n,r)=>n+r.preview.bytes,0)}));
}finally{await browser.close();}})().catch(e=>{console.error(e.message);process.exitCode=1;});
