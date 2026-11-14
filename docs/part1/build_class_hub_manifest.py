"""Copy the supplied runtime layers and flatten their authored manifests for Unity JsonUtility."""
from pathlib import Path
import json, shutil, hashlib

ROOT = Path(__file__).resolve().parents[2]
V6 = ROOT / 'Logs/UIResourceImports/player-hud-v6/astella-player-hud-v6'
V8 = ROOT / 'Logs/UIResourceImports/player-hud-v8/astella-player-hud-v8'
OUT = ROOT / 'Assets/UI/AstellaClassHubs'
MOTION = ROOT / 'Assets/UI/Resources/AstellaUltimateMotion'
jobs = json.loads((V8/'job-huds/manifest.json').read_text(encoding='utf-8-sig'))
ultimate = json.loads((V8/'ultimate/manifest.json').read_text(encoding='utf-8-sig'))

def copy(src, rel):
    dest = OUT / rel
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dest)
    return rel

def job_asset(ref):
    return copy(V8/'job-huds'/ref['png'], 'Job/'+ref['png']) if ref else ''

def state_assets(state):
    values = {}
    for key in ['track','fill','frame','core','full-glow']:
        ref=state['layers'].get(key)
        values['glow' if key=='full-glow' else key] = copy(V8/'ultimate'/ref['png'], 'Ultimate/'+ref['png']) if ref else ''
    values['badge']=copy(V8/'ultimate'/state['keyBadge'], 'Ultimate/'+state['keyBadge'])
    values['symbol']=copy(V8/'ultimate'/state['statusSymbol'], 'Ultimate/'+state['statusSymbol'])
    return values

def motion(slug, anim):
    paths=[]
    for sheet in anim['sheets']:
        dest=MOTION/slug/Path(sheet['path']).name
        dest.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(V8/'ultimate'/sheet['path'],dest)
        paths.append('AstellaUltimateMotion/'+slug+'/'+dest.stem)
    return dict(sheetResourcePaths=paths,frameCount=anim['frameCount'],framesPerSheet=anim['sheets'][0]['count'],
                columns=anim['sheets'][0]['columns'],framesPerSecond=anim['framesPerSecond'])

result=[]
for j in jobs['classes']:
    slug=j['classId']; u=next(c for c in ultimate['classes'] if c['classId']==slug)
    tokens=[]
    if j.get('tokenLayers'):
        for t in j['tokenLayers']:
            tokens.append(dict(active=job_asset(t['active']),empty=job_asset(t['empty']),rect=[0,0,400,240]))
    elif j.get('tokens'):
        for t in j['tokens']:
            tokens.append(dict(active=job_asset(t['active']),empty=job_asset(t['empty']),rect=[0,0,400,240]))
    elif j.get('tokenPositions'):
        w,h=j['tokenCanvas']
        for x,y in j['tokenPositions']:
            tokens.append(dict(active=job_asset(j['assets']['active-token']),empty=job_asset(j['assets']['empty-token']),rect=[x,y,w,h]))
    job=dict(kind=j['kind'],track=job_asset(j['assets']['track']),frame=job_asset(j['assets']['frame']),
             fill=job_asset(j['assets']['fill']) if j['kind']=='continuous' else '',
             fillBounds=j.get('fillBounds') or [0,0,400,240],direction=j.get('fillDirection') or 'bottom-to-top',
             value=j['valueBaseline'],fontSize=j.get('valueFontSize',34),
             showMaximum=j['kind']=='count' and j.get('valueFormat')!='currentOnly',tokens=tokens)
    ult=dict(chargeBounds=u['chargeBounds'],value=u['valueBaseline'],
             charging=state_assets(u['states']['charging']),ready=state_assets(u['states']['ready']),
             unavailable=state_assets(u['states']['unavailable']),
             burst=motion(slug,u['animations']['chargeBurst']),loop=motion(slug,u['animations']['readyLoop']))
    result.append(dict(jobId='job.'+slug,slug=slug,job=job,ultimate=ult))

# Preserve v6's original class sculpting separately; v8's state layers use that same family of original art.
for src in (V6/'ultimate-huds').glob('*/*.png'):
    if src.stem in ['track','fill','frame','core','mask','full-glow']:
        copy(src,'UltimateBaseV6/'+str(src.relative_to(V6/'ultimate-huds')).replace('\\','/'))
for src in (V8/'gauges/sprites').glob('*.png'): copy(src,'Health/'+src.name)
for src in (V8/'skill-buttons/sprites').glob('*.png'): copy(src,'SkillFrames/'+src.name)
copy(V8/'layout-manifest.json','layout-manifest.json')
copy(V8/'job-huds/manifest.json','job-manifest.json')
copy(V6/'ultimate-huds/manifest.json','ultimate-v6-manifest.json')
copy(V8/'ultimate/manifest.json','ultimate-states-manifest.json')
(OUT/'class-hubs-manifest.json').write_text(json.dumps(dict(classes=result),ensure_ascii=False,indent=2),encoding='utf-8')
print('Copied',len(result),'independent class pairs;',len(list(OUT.rglob('*.png'))),'static PNGs;',len(list(MOTION.rglob('*.png'))),'lazy motion sheets.')
