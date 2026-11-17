'use strict';
// Build an offline art book with embedded previews. Original PNG bytes are preserved.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

const root = path.resolve(__dirname, '../..');
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, 'sources.json'), 'utf8'));
const previewDir = path.join(__dirname, 'previews');
const sourceText = fs.readFileSync(path.join(__dirname, 'originals/00_캐릭터 컨셉.txt'), 'utf8').replace(/^\uFEFF/, '');
const sections = [...sourceText.matchAll(/^(\d{2})\s+([^\r\n]+)\r?\n([^\r\n]+)/gm)];
const specs = [
  ['GUARDIAN KNIGHT', '기사', '검 · 방패', '은발', '기사단 문장'],
  ['LANCE KNIGHT', '기사', '중창 · 대형 방패', '금발 단발', '경계 자세'],
  ['CRUSADE HAMMER KNIGHT', '기사', '워해머', '흰색 중갑', '남색'],
  ['TIME MAGE', '마법·연주', '회중시계 · 시계판', '연보라색', '시간'],
  ['BARD', '마법·연주', '꽃 장식 하프', '분홍 단발', '연주'],
  ['ALCHEMIST', '마법·연주', '플라스크 · 약병', '올리브색', '고글'],
  ['DUAL BLADE ASSASSIN', '쌍검·창', '쌍검', '나비 장식', '검정 · 자주'],
  ['DRAGON LANCER', '쌍검·창', '창', '용뿔 장식', '남보라색'],
  ['FOREST ARCHER', '활·총', '활 · 화살', '깃털 모자', '초록 망토'],
  ['MACHINIST', '활·총', '장식 총 · 마도 장치', '고글', '붉은 제복'],
  ['NEBULA MAGE', '마법·연주', '천구의 · 별의 궤도', '보라색', '금빛'],
  ['CAUSALITY MAGE', '마법·연주', '마도 장갑 · 시간 고리', '은보라색', '금빛 고리']
];
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
for (const entry of manifest.files) {
  const bytes = fs.readFileSync(path.join(__dirname, entry.file));
  if (hash(bytes) !== entry.sha256) throw Error('Original hash mismatch: ' + entry.file);
}
if (sections.length !== 12) throw Error('Expected twelve source descriptions.');

// Cache only; these JPEGs are never substituted for the archived originals.
fs.mkdirSync(previewDir, { recursive: true });
const cacheKey = hash(fs.readFileSync(path.join(__dirname, 'sources.json')));
const cacheKeyPath = path.join(previewDir, 'source-hashes.txt');
const cachedKey = fs.existsSync(cacheKeyPath) ? fs.readFileSync(cacheKeyPath, 'utf8').trim() : '';
const missingPreview = cacheKey !== cachedKey || sections.some(section => !fs.existsSync(path.join(previewDir, section[1] + '.jpg')));
if (missingPreview) {
  const script = `
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    $conceptConfig = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd())) | ConvertFrom-Json
    Add-Type -AssemblyName System.Drawing
    $conceptCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
    $conceptParams = New-Object System.Drawing.Imaging.EncoderParameters(1)
    $conceptParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, 92L)
    try {
      Get-ChildItem -LiteralPath $conceptConfig.source -Filter '*.png' | ForEach-Object {
        $conceptImage = [System.Drawing.Image]::FromFile($_.FullName)
        try { $conceptImage.Save((Join-Path $conceptConfig.output ($_.Name.Substring(0,2) + '.jpg')), $conceptCodec, $conceptParams) }
        finally { $conceptImage.Dispose() }
      }
    } finally { $conceptParams.Dispose() }
  `;
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], {
    input: Buffer.from(JSON.stringify({ source: path.join(__dirname, 'originals/일러스트'), output: previewDir }), 'utf8').toString('base64'), encoding: 'utf8', windowsHide: true
  });
  if (result.status !== 0) throw Error(result.stderr || 'JPEG preview generation failed.');
  fs.writeFileSync(cacheKeyPath, cacheKey + '\n');
}
const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[char]));
const encodePath = value => value.split('/').map(encodeURIComponent).join('/');
const items = sections.map((section, index) => {
  const [, number, name, description] = section;
  const entry = manifest.files.find(file => file.file.startsWith('originals/일러스트/' + number + '_'));
  if (!entry) throw Error('Original not found for ' + name);
  const bytes = fs.readFileSync(path.join(__dirname, entry.file));
  if (bytes.readUInt32BE(16) !== 1024 || bytes.readUInt32BE(20) !== 1536) throw Error('Unexpected PNG dimensions: ' + name);
  const [english, group, weapon, ...tags] = specs[index];
  return { number, name, english, group, weapon, tags, description, width:1024, height:1536,
    original: encodePath('ConceptArt/CharacterOverview/' + entry.file), sha256:entry.sha256 };
});
const cards = items.map((item, index) => {
  const image = 'data:image/jpeg;base64,' + fs.readFileSync(path.join(previewDir, item.number + '.jpg')).toString('base64');
  return `<article class="concept" data-index="${index}" data-group="${escape(item.group)}">
    <a class="art-link" data-view="${index}" href="${item.original}" target="_blank" rel="noopener" aria-label="${escape(item.name)} 컨셉 확대">
      <img src="${image}" alt="${escape(item.name + ' — ' + item.description)}" width="1024" height="1536" decoding="async" loading="${index < 6 ? 'eager' : 'lazy'}">
      <span class="art-number" aria-hidden="true">${item.number}</span><span class="art-open" aria-hidden="true">크게 보기 ↗</span>
    </a>
    <div class="concept-info"><p class="english">${item.english}</p><div class="name-line"><h3>${escape(item.name)}</h3><span class="group-label">${escape(item.group)}</span></div>
      <p class="weapon">${escape(item.weapon)}</p><p class="description">${escape(item.description)}</p>
    </div>
  </article>`;
}).join('\n');
const template = fs.readFileSync(path.join(__dirname, 'gallery.template.html'), 'utf8');
const html = template.replace('<!-- CONCEPT_CARDS -->', cards).replace('/* CONCEPT_DATA */', JSON.stringify(items).replace(/</g, '\\u003c'));
const output = path.join(root, '캐릭터_컨셉아트_모음.html');
fs.writeFileSync(output, html);
console.log(JSON.stringify({ output, characters:items.length, originalsVerified:manifest.files.length, embeddedImages:items.length, bytes:Buffer.byteLength(html), sourceCommit:manifest.sourceCommit }, null, 2));
