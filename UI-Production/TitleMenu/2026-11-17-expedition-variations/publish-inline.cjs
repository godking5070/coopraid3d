'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const shared = path.join(__dirname, '../2026-11-17-prism-engraved');
const concepts = JSON.parse(fs.readFileSync(path.join(__dirname, 'concepts.json'), 'utf8'));
const expectedIds = ['26-balanced', '27-tall', '28-chiseled', '29-fractured', '30-spring'];
if (concepts.length !== 5 || concepts.some((item, i) => item.id !== expectedIds[i] || item.number !== i + 26))
  throw new Error('Expected the five expedition variants numbered 26 through 30.');
const escapeHTML = value => String(value).replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
const safeJSON = value => JSON.stringify(value).replace(/</g, '\\u003c');

// Keep the file:// product page self-contained while concepts.json remains authoritative.
const indexPath = path.join(__dirname, 'index.html');
const originalIndex = fs.readFileSync(indexPath, 'utf8');
const navigation = concepts.map((item, i) => `<button data-concept="${item.id}" aria-pressed="${i === 0}">${item.number} ${escapeHTML(item.name)}</button>`).join('');
const syncedIndex = originalIndex
  .replace(/    const concepts=\[.*?\];/, '    const concepts=' + safeJSON(concepts) + ';')
  .replace(/(<nav class="concepts"[^>]*>)[\s\S]*?(<\/nav>)/, (_, start, end) => start + navigation + end);
for (const match of syncedIndex.matchAll(/<script>([\s\S]*?)<\/script>/g)) new vm.Script(match[1]);
if (syncedIndex !== originalIndex) fs.writeFileSync(indexPath, syncedIndex, 'utf8');
if (process.argv.includes('--sync-only')) {
  console.log(JSON.stringify({index: indexPath, concepts: concepts.length, synced: syncedIndex !== originalIndex}));
  process.exit(0);
}

const dataImage = (file, mime) => 'data:image/' + mime + ';base64,' + fs.readFileSync(file).toString('base64');
const background = dataImage(path.join(__dirname, 'inline-background.jpg'), 'jpeg');
const common = dataImage(path.join(shared, 'menu-overlay-1920.png'), 'png');
const reference = dataImage(path.join(__dirname, 'Reference/23-expedition.webp'), 'webp');
const items = concepts.map(item => ({...item, image: dataImage(path.join(__dirname, 'Artifacts', item.id + '.webp'), 'webp')}));
const html = `<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ASTELLA 원정의 명각 변형 26~30</title><style>
*{box-sizing:border-box}body{margin:0;background:var(--background,#fff);color:var(--foreground,#171717);font-family:var(--font-sans,system-ui,sans-serif)}main{padding:14px}header{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}h1{font-size:18px;margin:0}.modes,nav{display:flex;gap:6px;flex-wrap:wrap}nav{margin:13px 0 10px}button{font:inherit;font-size:13px;padding:8px 11px;border:1px solid var(--border,#ccc);border-radius:6px;background:var(--secondary,#eee);color:var(--secondary-foreground,#222);cursor:pointer}button[aria-pressed=true]{background:var(--primary,#483c5a);color:var(--primary-foreground,#fff)}button:hover{background:var(--accent,#ddd);color:var(--accent-foreground,#171717)}button:focus-visible{outline:3px solid var(--ring,#b09acb);outline-offset:2px}button:disabled{opacity:.45;cursor:default}
.reference{display:flex;align-items:center;gap:14px;margin:12px 0;padding:8px 12px;border:1px solid var(--border,#ccc);border-radius:6px;font-size:12px;color:var(--muted-foreground,#666)}.reference img{display:block;width:180px;height:60px;object-fit:contain;background:#101323;border-radius:3px}.overview{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.art-card{padding:0;overflow:hidden;text-align:left;background:var(--card,#fafafa);color:var(--card-foreground,#171717)}.art-card:last-child{grid-column:1/-1;width:calc(50% - 6px);justify-self:center}.logo-pad{height:175px;display:flex;align-items:center;justify-content:center;padding:20px;background:#101323}.logo-pad img{width:100%;height:100%;object-fit:contain}.card-label{display:block;font-size:14px;padding:10px 12px}
.scene{position:relative;width:100%;aspect-ratio:16/9;overflow:hidden;background:#14203a;border-radius:7px}.background,.common,.shade{position:absolute;inset:0;width:100%;height:100%}.background{object-fit:cover}.shade{background:linear-gradient(90deg,rgba(7,10,26,.34),rgba(11,15,32,.18) 28%,transparent 51%),linear-gradient(0deg,rgba(6,10,23,.18),transparent 24%)}.common{pointer-events:none}.brand{position:absolute;left:9.45%;top:8.8%;width:33%;height:17.95556%;object-fit:contain;filter:drop-shadow(0 2px 2px rgba(3,7,22,.5))}.scene.zoom .brand{left:6%;top:12%;width:88%;height:70%}.scene.zoom .shade{background:rgba(6,8,24,.72)}.scene.zoom .common{opacity:.05}p{font-size:13px;line-height:1.5;margin:10px 0 0;color:var(--muted-foreground,#666)}footer{font-size:12px;color:var(--muted-foreground,#666);margin-top:7px}[hidden]{display:none!important}@media(max-width:620px){.overview{grid-template-columns:1fr}.art-card:last-child{grid-column:auto;width:100%}.logo-pad{height:160px}.reference img{width:140px;height:47px}}
</style><main><header><h1>ASTELLA · 원정의 명각 변형 5종</h1><div class="modes"><button id="all" aria-pressed="true">변형 5종 한눈에</button><button id="zoom" aria-pressed="false" disabled>로고 확대 보기</button></div></header><aside class="reference" id="reference"><img id="reference-art" src="${reference}" alt="기준 · 원정의 명각"><span>기준 · 원정의 명각<br>선택한 23번 원본</span></aside><nav id="tabs" aria-label="변형 시안 선택"></nav><div class="overview" id="overview"></div><div class="scene" id="scene" hidden><img class="background" src="${background}" alt="기존 아스텔라 숲 배경"><div class="shade"></div><img class="common" src="${common}" alt="기존 한글과 메뉴"><img class="brand" id="brand" alt="ASTELLA"></div><p id="caption"></p><footer>원정의 명각 변형 26~30 · 같은 숲과 메뉴에서 비교 · 디자인 시안</footer></main><script>
const items=${safeJSON(items)},tabs=document.getElementById('tabs'),brand=document.getElementById('brand'),overview=document.getElementById('overview'),scene=document.getElementById('scene'),all=document.getElementById('all'),zoom=document.getElementById('zoom'),caption=document.getElementById('caption');
function resetZoom(){zoom.setAttribute('aria-pressed','false');zoom.textContent='로고 확대 보기';scene.classList.remove('zoom')}
function select(index,show=true){const item=items[index];brand.src=item.image;brand.alt='ASTELLA · '+item.name;[...tabs.children].forEach((button,i)=>button.setAttribute('aria-pressed',String(show&&index===i)));caption.textContent=item.number+'. '+item.name+' — '+item.note;if(show){overview.hidden=true;scene.hidden=false;all.setAttribute('aria-pressed','false');zoom.disabled=false}}
items.forEach((item,index)=>{const tab=document.createElement('button');tab.textContent=item.number+' '+item.name;tab.onclick=()=>select(index);tabs.append(tab);const card=document.createElement('button');card.className='art-card';const pad=document.createElement('span');pad.className='logo-pad';const image=document.createElement('img');image.alt='ASTELLA · '+item.name;image.src=item.image;pad.append(image);const label=document.createElement('span');label.className='card-label';label.textContent=item.number+' · '+item.name;card.append(pad,label);card.onclick=()=>select(index);overview.append(card)});
all.onclick=()=>{overview.hidden=false;scene.hidden=true;all.setAttribute('aria-pressed','true');[...tabs.children].forEach(button=>button.setAttribute('aria-pressed','false'));resetZoom();zoom.disabled=true;caption.textContent='카드를 누르면 같은 타이틀 화면에서 비교할 수 있습니다.'};
zoom.onclick=()=>{const next=zoom.getAttribute('aria-pressed')!=='true';zoom.setAttribute('aria-pressed',String(next));zoom.textContent=next?'전체 화면 보기':'로고 확대 보기';scene.classList.toggle('zoom',next)};select(0,false);caption.textContent='카드를 누르면 같은 타이틀 화면에서 비교할 수 있습니다.';
</script></html>`;
new vm.Script(html.match(/<script>([\s\S]*?)<\/script>/)[1]);
const bytes = Buffer.byteLength(html);
if (bytes >= 512 * 1024 || html.includes('\ufffd')) throw new Error('Invalid native preview: ' + bytes + ' bytes.');
if (!process.env.ORCA_CHAT_SCRATCH_DIR) throw new Error('ORCA_CHAT_SCRATCH_DIR is required to publish the native preview.');
const file = path.join(process.env.ORCA_CHAT_SCRATCH_DIR, 'astella-expedition-variations-26-30-20261117.html');
fs.writeFileSync(file, html, 'utf8');
console.log(JSON.stringify({file, bytes, concepts: items.length, references: 1}));
