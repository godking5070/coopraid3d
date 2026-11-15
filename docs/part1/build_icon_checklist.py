"""Build the offline icon production checklist from audited game definitions."""
import json, html
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
data = json.loads((HERE / 'icon-production-data.json').read_text(encoding='utf-8-sig'))
esc = lambda s: html.escape(str(s or ''))
colors = dict(zip(['aegis-paladin','berserker','dragon-breaker','sylvan-oracle','seraphic-bard','blood-apothecary','nightblade','tempest-lancer','gunslinger','spirit-ranger','astromancer','mirror-phantom'], ['#d5cca9','#f35d45','#ec933e','#66c995','#f1d374','#cb617b','#9972da','#59bdf2','#9bbfda','#69c7ab','#a785f0','#c6a4ea']))
roles = {'1':'기본 공격 콤보','2':'선택형 두 번째 콤보','3':'보조 기술','4':'카운터 / 대응 선택','Q':'개인 방어 / 긴급 회복','R':'기원 핵심 기술','F':'직업 보조 기술','V':'필살기','SPACE':'직업 이동기'}
parts=[]
count=0
for i, job in enumerate(data['jobs']):
    rows=[]
    for slot in job['slots']:
        if str(slot['key']).upper() in ('X','POTION') or slot['slot']=='Potion': continue
        key=str(slot['key']).upper()
        for skill in slot['skills']:
            if not skill.get('implemented',True): continue
            count+=1
            sid=skill['id']
            combo=skill.get('comboSteps') or []
            combo_text=' → '.join(str(v.get('name',v)) if isinstance(v,dict) else str(v) for v in combo)
            info=skill.get('description','')
            badge='기본 장착' if skill.get('initial') else '선택 / 기원 변형'
            origins=[r['name'] for r in job.get('routes',[]) if r['id'] in skill.get('routeIds',[])]
            if origins: badge='기원: '+' / '.join(origins)
            if key=='V': badge+=' · Stage 3 클리어 후 해금'
            if skill.get('conditionalComboSteps'):
                extra=skill['conditionalComboSteps']
                combo_text+=' · 조건부 추가 타격: '+' / '.join(str(v.get('name',v)) if isinstance(v,dict) else str(v) for v in extra)
            rows.append(f'''<tr data-search="{esc(job['name']+' '+key+' '+skill['name']+' '+sid)}"><td><input type="checkbox" data-id="{esc(sid)}" aria-label="{esc(skill['name'])} 제작 완료"></td><td><kbd>{esc(key)}</kbd><small>{esc(roles.get(key,''))}</small></td><td><b>{esc(skill['name'])}</b><small>{esc(badge)}</small>{f'<p class="combo">콤보: {esc(combo_text)}</p>' if combo_text else ''}<details><summary>기능 · 연결 ID</summary><p>{esc(info)}</p><code>{esc(sid)}</code><p>파일명: <code>{esc(sid)}.png</code></p></details></td></tr>''')
    parts.append(f'''<section class="job" id="{esc(job['id'])}" style="--job:{colors.get(job['id'].removeprefix('job.'),'#c6a4ea')}"><h2>{esc(job['name'])}<span>{len(rows)}종</span></h2><p>{esc(job.get('theme',''))} · {esc(job.get('weapon',''))}</p><table><thead><tr><th>완료</th><th>기본 키 / 용도</th><th>제작할 아이콘</th></tr></thead><tbody>{''.join(rows)}</tbody></table></section>''')

page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>ASTELLA 스킬 아이콘 제작 목록</title>
<style>
:root{color-scheme:dark;font-family:Segoe UI,Malgun Gothic,sans-serif;background:#11131d;color:#e8e5de;line-height:1.65}body{max-width:1100px;margin:48px auto;padding:0 24px}h1{font-size:34px;color:#edcf91;margin-bottom:8px}h2{color:var(--job);border-left:4px solid var(--job);padding-left:14px;font-size:24px}h2 span{float:right;font-size:15px;color:#a9abb8}h3{color:#edcf91}p{margin:6px 0 16px}a{color:#dfc48d}.lead{color:#babdcb}.card,.job{background:#1b1e2c;border:1px solid #35394a;border-radius:12px;padding:24px;margin:24px 0}.tools{position:sticky;top:0;background:#11131df5;padding:16px 0;z-index:2;display:flex;gap:12px;flex-wrap:wrap;align-items:center}input[type=search],select,button{font:inherit;background:#25293b;color:#eee;border:1px solid #545a71;padding:9px 12px;border-radius:6px}input[type=search]{flex:1;min-width:240px}input[type=checkbox]{width:22px;height:22px;accent-color:#c3a060}table{width:100%;border-collapse:collapse;text-align:left}th,td{padding:14px 10px;border-bottom:1px solid #343748;vertical-align:top}th:first-child{width:45px}th:nth-child(2){width:160px}small{display:block;color:#a8abba;font-size:12px}kbd{display:inline-block;min-width:28px;text-align:center;border:1px solid #9d875a;border-radius:5px;padding:2px 8px;color:#f3d89f;font:600 16px Segoe UI}.combo{font-size:13px;color:#c9c3af}details{font-size:13px;margin-top:8px}summary{cursor:pointer;color:#aab0c5}code{overflow-wrap:anywhere;color:#b1c2e5}tr:has(input:checked) b{text-decoration:line-through;color:#9298a7}.note{color:#c7bb9e;font-size:14px}[hidden]{display:none!important}nav{display:flex;flex-wrap:wrap;gap:8px}nav a{padding:4px 10px;background:#242838;border-radius:5px;font-size:14px}.swatch{display:inline-block;width:10px;height:10px;background:var(--job);border-radius:50%;margin-right:7px}@media print{.tools,button,nav{display:none}body{margin:0;max-width:none}.job{break-inside:avoid}details{display:block}details>*{display:block}*{color:#111!important;background:white!important}}
</style>
<h1>ASTELLA · 스킬 아이콘 제작 목록</h1><p class="lead">현재 12개 정규 직업의 9키 정의 기준 · 2026-11-06 작성 · 브라우저에서 열어 언제든 확인할 수 있는 오프라인 문서</p>
<div class="card"><h3>무엇부터 만들까?</h3><p><kbd>1</kbd> 기본 공격 · <kbd>2</kbd> 두 번째 콤보 · <kbd>3</kbd> 보조 기술 · <kbd>4</kbd> 카운터 · <kbd>Q</kbd> 방어/긴급 회복 · <kbd>R</kbd> 기원 핵심 · <kbd>F</kbd> 직업 보조 · <kbd>V</kbd> 필살기 · <kbd>Space</kbd> 이동기</p><p>첫 시안은 직업 하나를 골라 실제 사용할 빌드의 <b>9종</b>을 먼저 제작합니다. 이후 아래의 선택형 스킬과 세 기원의 R을 확장합니다. 잠겨 있는 R·V도 최종 제작 대상입니다. 현재 정규 12직업에서는 E·Z를 별도 제작하지 않습니다.</p><p class="note">숫자 4는 카운터, Q는 직업 스킬입니다. 기존 테스트 직업 화면의 E·Z·R 배치와 구분하세요. 단축키는 변경할 수 있으므로 이미지에는 키나 글자를 그리지 않습니다.</p></div>
<div class="card"><h3>공용 아이콘 — 직업 스킬과 별도 제작</h3><table><tr><th>완료</th><th>키</th><th>필요한 그림 / 배치</th></tr><tr><td><input type="checkbox" data-id="common.potion"></td><td><kbd>X</kbd></td><td><b>회복 포션</b> — 붉은 액체의 작은 유리병. 스킬보다 작은 소모품 칸. 파일명 <code>common.potion.png</code></td></tr><tr><td><input type="checkbox" data-id="common.revival"></td><td><kbd>5</kbd></td><td><b>공유 부활 코인</b> — 금빛 원형 코인과 이리스 문양. 파티 목록 아래. 수량·단축키는 UI로 표시. 파일명 <code>common.revival.png</code></td></tr></table></div>
<div class="card"><h3>그림 제작 규칙</h3><p>원본 512×512 PNG, 투명 배경 권장. 금색 테두리·단축키·쿨타임 숫자·잠금 표시는 넣지 않습니다. 작은 HUD에서도 구분되는 큰 중심 실루엣과 명암을 사용하고, 같은 직업은 주색·광원·재질·모티브를 통일합니다. 같은 색 안에서 칼날·방패·투사체·문양 등 형태로 스킬을 구분합니다.</p><p>아래 한 행은 한 장의 아이콘입니다. 같은 스킬이 여러 빌드에 쓰이면 재사용합니다. 1·2번의 세 콤보 단계는 우선 대표 아이콘 한 장을 공유하고, 단계마다 그림이 바뀌는 연출을 결정한 뒤 추가 제작합니다. 패시브·핵심/보조 특성·강화 상태·쿨타임별 그림은 이번 필수 수량에서 제외합니다.</p><p class="note">현재 HUD는 직업별 팔레트 셰이더를 사용합니다. 완성 일러스트 연결 시 원본 색을 사용할지 해당 직업 팔레트를 유지할지도 함께 확인합니다. 제작한 PNG는 <code>Assets/UI/AstellaOrnate/Art/Skills/직업슬러그/</code> 아래에 두고, 실제 스킬 ID로 아이콘을 연결합니다.</p></div>
<nav>__NAV__</nav><div class="tools"><input id="search" type="search" placeholder="직업, 키, 스킬명, ID 검색"><select id="filter"><option value="all">전체</option><option value="todo">미완료만</option><option value="done">완료만</option></select><span id="progress"></span><button onclick="window.print()">인쇄 / PDF</button></div>
__JOBS__
<p class="note">총 __COUNT__종의 직업별 장착 스킬 + 공용 2종. 목록은 현재 코드의 스냅샷이며 이후 스킬 변경 시 재추출이 필요합니다. 제작 체크는 이 브라우저에 저장됩니다. 다른 기기·브라우저와 자동 동기화되지 않습니다. 출처: CharacterFrameworkData.cs, LocalSettingsData.cs, AegisPaladinData.BuildNineKey, NineKeyTankData.Build, NineKeyHealerData.Build, NineKeyDamageData.Build.</p>
<script>
const boxes=[...document.querySelectorAll('[data-id]')], storage='astella-icon-production-v1';let saved={};try{saved=JSON.parse(localStorage.getItem(storage)||'{}')}catch{}for(const box of boxes){box.checked=!!saved[box.dataset.id];box.addEventListener('change',()=>{saved[box.dataset.id]=box.checked;try{localStorage.setItem(storage,JSON.stringify(saved))}catch{}update()})}function update(){const q=document.querySelector('#search').value.trim().toLocaleLowerCase(),f=document.querySelector('#filter').value;for(const row of document.querySelectorAll('tr[data-search]')){const done=row.querySelector('input').checked;row.hidden=!row.dataset.search.toLocaleLowerCase().includes(q)||(f==='todo'&&done)||(f==='done'&&!done)}for(const section of document.querySelectorAll('.job'))section.hidden=![...section.querySelectorAll('tbody tr')].some(r=>!r.hidden);document.querySelector('#progress').textContent=boxes.filter(b=>b.checked).length+' / '+boxes.length+' 완료'}document.querySelector('#search').addEventListener('input',update);document.querySelector('#filter').addEventListener('change',update);update();
</script></html>'''
nav=''.join(f'<a href="#{esc(j["id"])}">{esc(j["name"])}</a>' for j in data['jobs'])
overview=[]
for job in data['jobs']:
    cells=[]
    for key in ['1','2','3','4','Q','R','F','V','SPACE']:
        slot=next(s for s in job['slots'] if s['key'].upper()==key)
        skills=[s for s in slot['skills'] if s.get('implemented',True)]
        skill=next((s for s in skills if s['id']==slot['defaultSkillId']),skills[0])
        label=' / '.join(s['name'] for s in skills) if key=='R' else skill['name']
        cells.append('<td>'+esc(label)+'</td>')
    overview.append('<tr><th>'+esc(job['name'])+'</th>'+''.join(cells)+'</tr>')
overview='<div class="card"><h3>직업별 첫 제작 세트</h3><p>기본 장착 기준입니다. R은 선택할 기원의 그림 한 장부터 시작하세요. 교체 가능한 전체 목록은 아래에 있습니다.</p><div style="overflow-x:auto"><table style="min-width:1500px;font-size:13px"><tr><th>직업</th>'+''.join('<th>'+k+'</th>' for k in ['1','2','3','4','Q','R (기원 3종)','F','V','Space'])+'</tr>'+''.join(overview)+'</table></div></div>'
page=page.replace('__NAV__',overview+nav).replace('__JOBS__',''.join(parts)).replace('__COUNT__',str(count))
target=ROOT / '스킬아이콘_제작목록.html'
target.write_text(page,encoding='utf-8')
print(f'{target}: {count} job icons + 2 common icons')
