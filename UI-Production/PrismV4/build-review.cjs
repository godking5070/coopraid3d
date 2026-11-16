"use strict";
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const kit = path.join(root, "Assets/UI/AstellaPrismV4");
const out = path.join(root, "UI-Concepts/2026-11-16-prism-kit");
const manifest = JSON.parse(fs.readFileSync(path.join(kit, "prism-v4-manifest.json"), "utf8"));
fs.mkdirSync(out, { recursive: true });
for (const asset of manifest.assets) {
  const destination = path.join(out, asset.png);
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(path.join(kit, asset.png), destination);
}
for (const file of ["component-assembly.png", "fill-validation.png", "component-contact-sheet.png"]) fs.copyFileSync(path.join(__dirname, "preview", file), path.join(out, file));
fs.copyFileSync(path.join(kit, "prism-v4-manifest.json"), path.join(out, "manifest.json"));
fs.copyFileSync(path.join(kit, "HANDOFF.md"), path.join(out, "handoff.txt"));
const zip = path.join(root, "UI-Delivery/AstellaPrismV4-main-handoff.zip");
if (fs.existsSync(zip)) fs.copyFileSync(zip, path.join(out, path.basename(zip)));
function escape(value) { return String(value).replace(/[&<>"']/g, x => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[x]); }
const categories = [...new Set(manifest.assets.map(a => a.id.split("/")[0]))];
const labels = { Boss: "보스·시전", Player: "플레이어 게이지", Party: "파티", Hub: "중앙 문장", Skills: "스킬 슬롯", States: "상태·쿨타임" };
const filters = ["All", ...categories].map(c => `<button type="button" data-filter="${escape(c)}" aria-pressed="${c === "All"}">${c === "All" ? "전체" : escape(labels[c] || c)}</button>`).join("");
const tiles = manifest.assets.map(a => `<article data-category="${escape(a.id.split("/")[0])}"><a href="${escape(a.png)}" target="_blank" rel="noopener"><div class="checker"><img src="${escape(a.png)}" alt="${escape(a.id)} 투명 PNG" loading="lazy" width="${a.canvas[0]}" height="${a.canvas[1]}"></div><strong>${escape(a.id)}</strong></a><span>${a.canvas[0]} × ${a.canvas[1]} · RGBA</span></article>`).join("\n");
const html = `<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Astella Prism V4 · 분리 리소스</title>
<style>
:root{color-scheme:dark;font-family:"Malgun Gothic",system-ui,sans-serif;background:#0d1020;color:#e6e4f2}*{box-sizing:border-box}body{margin:0}main{max-width:1600px;margin:auto;padding:28px}header{display:flex;justify-content:space-between;gap:24px;align-items:center}h1{font-size:28px;margin:0 0 12px}h2{font-size:20px;margin:32px 0 14px}p{line-height:1.7;color:#b6b3cb;margin:6px 0}a{color:#d4c3fa;text-underline-offset:4px}.actions{display:flex;gap:18px;flex-wrap:wrap}figure{margin:20px 0}.assembly{display:block;width:100%;height:auto;border-radius:8px}.note{font-size:13px}.filters{display:flex;flex-wrap:wrap;gap:10px;margin:18px 0}button{font:inherit;padding:10px 14px;background:#191c30;color:#d6d2e8;border:1px solid #4c465f;border-radius:6px;cursor:pointer}button[aria-pressed=true]{background:#443768;color:#fff;border-color:#bfa8eb}button:focus-visible,a:focus-visible{outline:3px solid #c4adef;outline-offset:4px}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:16px}article{min-width:0;padding:12px;background:#171a2b;border:1px solid #38334b;border-radius:8px}article a{text-decoration:none}article strong{display:block;margin-top:12px;overflow-wrap:anywhere;font-size:13px}article span{font-size:12px;color:#a9a3bd}.checker{height:136px;display:flex;align-items:center;justify-content:center;background-color:#202332;background-image:linear-gradient(45deg,#343448 25%,transparent 25%),linear-gradient(-45deg,#343448 25%,transparent 25%),linear-gradient(45deg,transparent 75%,#343448 75%),linear-gradient(-45deg,transparent 75%,#343448 75%);background-size:20px 20px;background-position:0 0,0 10px,10px -10px,-10px 0}.checker img{max-width:100%;max-height:128px;width:auto;height:auto}article[hidden]{display:none}footer{margin:30px 0 10px;font-size:13px;color:#a9a3bd}@media(max-width:1000px){.grid{grid-template-columns:repeat(3,minmax(0,1fr))}header{align-items:flex-start;flex-direction:column}}@media(max-width:650px){main{padding:18px}.grid{grid-template-columns:repeat(2,minmax(0,1fr))}h1{font-size:23px}.checker{height:110px}.checker img{max-height:102px}}
</style><script src="review.js" defer></script></head><body><main>
<header><div><h1>Prism V4 · 분리형 UI 리소스</h1><p>투명 PNG ${manifest.assets.length}종 · 편집용 SVG · 아이콘 제작 제외</p></div><nav class="actions" aria-label="전달 자료"><a href="AstellaPrismV4-main-handoff.zip">메인 전달 패키지 받기</a><a href="handoff.txt" target="_blank" rel="noopener">적용 안내</a><a href="manifest.json" target="_blank" rel="noopener">부품 목록</a></nav></header>
<figure><img class="assembly" src="component-assembly.png" alt="아이콘 칸을 비우고 분리 부품을 조립한 Prism HUD 미리보기" width="1672" height="941"><figcaption class="note">이름·숫자·단축키는 조립 미리보기의 별도 텍스트입니다. PNG 부품에는 포함되지 않습니다.</figcaption></figure>
<h2>게이지 채움 확인</h2><img class="assembly" src="fill-validation.png" alt="0%, 50%, 100%의 게이지 채움 비교. 가득 차면 양쪽 삼각 끝까지 채워집니다.">
<h2>개별 투명 부품</h2><p class="note">체크 무늬는 투명도를 보여주는 배경입니다. 부품을 누르면 PNG 원본을 엽니다.</p><nav class="filters" aria-label="부품 종류">${filters}</nav><div class="grid">${tiles}</div>
<footer>기존 게임 아이콘을 스킬·파티의 빈 칸에 연결해 사용합니다. 메인 Unity에서의 실제 전투 연결과 Play 검증은 별도 적용 단계입니다.</footer>
</main></body></html>`;
fs.writeFileSync(path.join(out, "index.html"), html);
fs.writeFileSync(path.join(out, "review.js"), `"use strict";\nconst buttons=[...document.querySelectorAll('[data-filter]')];const tiles=[...document.querySelectorAll('[data-category]')];buttons.forEach(button=>button.addEventListener('click',()=>{buttons.forEach(peer=>peer.setAttribute('aria-pressed',String(peer===button)));tiles.forEach(tile=>{tile.hidden=button.dataset.filter!=='All'&&tile.dataset.category!==button.dataset.filter;});}));\n`);
console.log("Built review gallery for " + manifest.assets.length + " sprites: " + out);
