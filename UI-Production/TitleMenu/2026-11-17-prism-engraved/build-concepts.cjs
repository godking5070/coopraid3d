const fs=require('node:fs'),path=require('node:path');
const dir=path.join(__dirname,'Concepts');fs.mkdirSync(dir,{recursive:true});
const source=fs.readFileSync(path.join(__dirname,'astella-wordmark.svg'),'utf8');
const group=source.match(/<g fill="#e9e4dd">([\s\S]*?)<\/g>/)[1];
const outlines=group.replace(/-?\d+\.\d+/g,n=>String(Number(Number(n).toFixed(2))));
const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="760" height="220" viewBox="0 0 760 220" role="img" aria-label="ASTELLA"><title>ASTELLA · 프리즘 세공</title><desc>MaruBuri outlined wordmark, shallow silver edge and small manually constructed violet prism. No generated bitmap lettering.</desc><defs><linearGradient id="ivory" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#f0ebe3"/><stop offset=".7" stop-color="#dad5dc"/><stop offset="1" stop-color="#c2bacb"/></linearGradient><linearGradient id="silver" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#e1d6d8"/><stop offset=".5" stop-color="#b2a7bc"/><stop offset="1" stop-color="#706879"/></linearGradient><g id="word">${outlines}</g></defs>
<g fill="none" stroke="url(#silver)"><path d="M101 184l12-7h238m58 0h238l12 7-12 7H409m-58 0H113z" stroke-width="1.5"/><path d="M118 181h229m66 0h229M118 188h229m66 0h229" stroke-width=".65" opacity=".65"/></g>
<g transform="translate(86 57)"><use href="#word" transform="translate(0 .8)" fill="#554d66" stroke="#6e6478" stroke-width="1.3"/><use href="#word" fill="url(#ivory)" stroke="#c7bccb" stroke-width=".55"/></g>
<path d="M380 169l12 15-12 15-12-15z" fill="#2d233f" stroke="#d0c2d9" stroke-width="1.2"/><path d="M380 172v22l-8-10z" fill="#b9a4d5"/><path d="M380 172l8 12h-8z" fill="#d8c9e9"/><path d="M380 184h8l-8 10z" fill="#786097"/>
<path d="M104 184l8-5v10zM656 184l-8-5v10z" fill="#cec2d1"/><path d="M117 181h27m472 0h27" fill="none" stroke="#eee3df" stroke-width=".7"/></svg>`;
fs.writeFileSync(path.join(dir,'01-prism.svg'),svg,'utf8');
const concepts=[
 {id:'01-prism',name:'프리즘 세공',note:'기존 HUD의 금속 베벨과 삼각 끝선을 옮긴 안입니다. 보라색 수정은 작은 연결부에만 사용했습니다.'},
 {id:'02-time',name:'멈춘 시계',note:'시계탑과 멈춘 시간에서 가져온 얇은 눈금·불완전한 원호입니다. 시계 장식은 글자 뒤에 작게 남겼습니다.'},
 {id:'03-sanctuary',name:'성소의 인장',note:'성소의 석판을 연상시키는 각진 금속 구조입니다. 곡선보다 챔퍼와 수직선으로 무게를 잡았습니다.'},
 {id:'04-seal',name:'균열과 봉인',note:'엇갈린 금속선과 작은 프리즘 파편으로 봉인이 갈라지는 느낌을 준 안입니다.'},
 {id:'05-chronicle',name:'원정 기록',note:'원정 기록의 표지처럼 활판 제목과 가는 장식을 조합했습니다. 반짝임보다 선과 간격으로 꾸몄습니다.'}
];fs.writeFileSync(path.join(__dirname,'concepts.json'),JSON.stringify(concepts,null,2)+'\n','utf8');
