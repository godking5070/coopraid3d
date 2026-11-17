#!/usr/bin/env node
'use strict';
// Node 26 built-ins only. This opens a dedicated hidden headless Edge instance.
const fs = require('node:fs/promises');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { spawn } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const option = name => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3);
const evidence = path.resolve(option('evidence') || 'C:/Astella/title-art-five-evidence-20261117');
const html = path.resolve(option('html') || path.join(__dirname, 'index.html'));
const stamp = new Date().toISOString().replace(/[:.]/g, '-') + '-' + randomBytes(4).toString('hex');
const profile = path.join(evidence, 'edge-profile-' + stamp);
const report = {
  purpose: 'Isolated browser design preview; no Unity or real game actions are certified.',
  startedUtc: new Date().toISOString(), html, evidence, profile, edge: null,
  expectedLabels: ['이어하기', '새 원정 시작', '파티 참가', '저장 관리', '설정', '종료'],
  bitmapSpellingBoundary: 'ASTELLA is checked through image alt text only; bitmap letterforms need visual review.',
  checks: [], cases: [], pageErrors: [], resourceFailures: [], errors: [], passed: false
};

function check(name, passed, detail, item) {
  const result = { name, passed: Boolean(passed), detail };
  (item ? item.checks : report.checks).push(result);
  return result.passed;
}

class CDP {
  constructor(url) { this.socket = new WebSocket(url); this.next = 0; this.pending = new Map(); this.listeners = []; }
  async open() {
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('CDP WebSocket connection timed out.')), 10000);
      this.socket.addEventListener('open', () => { clearTimeout(timer); resolve(); }, { once: true });
      this.socket.addEventListener('error', () => { clearTimeout(timer); reject(new Error('CDP WebSocket failed.')); }, { once: true });
    });
    this.socket.addEventListener('message', event => {
      const message = JSON.parse(String(event.data));
      if (message.id) {
        const pending = this.pending.get(message.id); if (!pending) return;
        this.pending.delete(message.id); clearTimeout(pending.timer);
        if (message.error) pending.reject(new Error(`${pending.method}: ${message.error.message}`));
        else pending.resolve(message.result);
      } else for (const listener of this.listeners) listener(message);
    });
    this.socket.addEventListener('close', () => {
      for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(new Error('CDP connection closed.')); }
      this.pending.clear();
    });
  }
  call(method, params = {}, sessionId, timeout = 30000) {
    return new Promise((resolve, reject) => {
      const id = ++this.next;
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error(`${method} timed out.`)); }, timeout);
      this.pending.set(id, { resolve, reject, timer, method });
      this.socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
  }
  async evaluate(expression, sessionId) {
    const result = await this.call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId);
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
    return result.result.value;
  }
}

const snapshotScript = `(() => {
  const clean = value => String(value || '').replace(/\\s+/g, ' ').trim();
  const rect = node => { if (!node) return null; const r = node.getBoundingClientRect();
    return { x:r.x, y:r.y, width:r.width, height:r.height, right:r.right, bottom:r.bottom }; };
  const actions = [...document.querySelectorAll('.menu-action')];
  const art = document.querySelector('.brand-art'), ko = document.querySelector('.brand-ko');
  const saved = document.getElementById('saved-example'), cont = document.getElementById('continue');
  return {
    url: location.href, width:innerWidth, height:innerHeight,
    scrollWidth:document.documentElement.scrollWidth, scrollHeight:document.documentElement.scrollHeight,
    labels:actions.map(button => clean(button.querySelector('.label')?.textContent || button.textContent)),
    activeIndex:actions.indexOf(document.activeElement),
    selected:actions.map((button,index) => ({index,selected:button.classList.contains('is-selected'),aria:button.getAttribute('aria-pressed')})),
    continueDisabled:cont?.disabled, continueIndex:actions.indexOf(cont), savedChecked:saved?.checked,
    savedIsCheckbox:saved?.type === 'checkbox', status:clean(document.getElementById('preview-status')?.textContent),
    brandKo:clean(ko?.textContent), art:art ? {src:art.currentSrc,alt:art.alt,complete:art.complete,
      naturalWidth:art.naturalWidth,naturalHeight:art.naturalHeight} : null,
    fonts:[...document.fonts].map(face => ({family:face.family,status:face.status})),
    maruReady:document.fonts.check('29px MaruBuri'), pretendardReady:document.fonts.check('14px Pretendard'),
    labelFont:actions[0] ? getComputedStyle(actions[0]).fontFamily : '',
    koFont:ko ? getComputedStyle(ko).fontFamily : '',
    bounds:[['.brand-art',art],['.brand-ko',ko],['.main-menu',document.querySelector('.main-menu')],
      ...actions.map((node,index) => ['.menu-action['+index+']',node])].map(([selector,node]) => ({selector,rect:rect(node)})),
    reviewChromeCount:document.querySelectorAll('.review-bar,.review-note,.concepts').length,
    reviewChromeHidden:[...document.querySelectorAll('.review-bar,.review-note,.concepts')].every(node => getComputedStyle(node).display === 'none')
  };
})()`;

async function waitForAssets(cdp, session) {
  return cdp.evaluate(`(async () => {
    const until = Date.now() + 18000;
    while (document.readyState !== 'complete' || !document.querySelector('.brand-art')) {
      if (Date.now() > until) throw new Error('Document/brand-art did not become ready.');
      await new Promise(resolve => setTimeout(resolve, 40));
    }
    await Promise.race([Promise.all([document.fonts.ready, ...[...document.images].map(image => image.decode())]),
      new Promise((_,reject) => setTimeout(() => reject(new Error('Font/image decoding timed out.')), 18000))]);
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    return {images:[...document.images].map(image => ({src:image.currentSrc,width:image.naturalWidth,height:image.naturalHeight}))};
  })()`, session);
}

async function key(cdp, session, name, code) {
  const params = { key: name, code: name, windowsVirtualKeyCode: code, nativeVirtualKeyCode: code };
  await cdp.call('Input.dispatchKeyEvent', { type: 'keyDown', ...params }, session);
  await cdp.call('Input.dispatchKeyEvent', { type: 'keyUp', ...params }, session);
  return cdp.evaluate(snapshotScript, session);
}

async function captureCase(cdp, width, height, concept) {
  const item = { width, height, concept, checks: [], screenshot: path.join(__dirname, `preview-${concept}-${width}.png`) };
  report.cases.push(item);
  let target;
  try {
    target = (await cdp.call('Target.createTarget', { url: 'about:blank' })).targetId;
    const session = (await cdp.call('Target.attachToTarget', { targetId: target, flatten: true })).sessionId;
    await cdp.call('Page.enable', {}, session); await cdp.call('Runtime.enable', {}, session);
    await cdp.call('Network.enable', {}, session);
    cdp.listeners.push(message => {
      if (message.sessionId !== session) return;
      if (message.method === 'Runtime.exceptionThrown') report.pageErrors.push({width,details:message.params.exceptionDetails});
      if (message.method === 'Network.loadingFailed') report.resourceFailures.push({width,...message.params});
    });
    await cdp.call('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false,
      screenWidth: width, screenHeight: height }, session);
    const url = pathToFileURL(html); url.searchParams.set('capture', '1');url.searchParams.set('concept',concept);
    await cdp.call('Page.navigate', { url: url.href }, session);
    item.assets = await waitForAssets(cdp, session);
    const state = await cdp.evaluate(snapshotScript, session); item.initial = state;
    check('exact viewport', state.width === width && state.height === height, [state.width,state.height], item);
    check('six exact Korean menu labels', JSON.stringify(state.labels) === JSON.stringify(report.expectedLabels), state.labels, item);
    check('exact Korean title', state.brandKo === '아스텔라', state.brandKo, item);
    check('logo image loaded with ASTELLA alt', state.art?.complete && state.art.naturalWidth > 0 && state.art.naturalHeight > 0 && state.art.alt === 'ASTELLA', state.art, item);
    check('requested concept is loaded',state.art?.src.endsWith('/Artifacts/'+concept+'.png'),state.art?.src,item);
    check('MaruBuri and Pretendard faces loaded', ['MaruBuri','Pretendard'].every(family => state.fonts.some(face => face.family.replace(/['"]/g,'') === family && face.status === 'loaded')) && state.maruReady && state.pretendardReady, state.fonts, item);
    check('Korean menu/title use MaruBuri', state.labelFont.includes('MaruBuri') && state.koFont.includes('MaruBuri'), [state.labelFont,state.koFont], item);
    check('continue disabled by default', state.continueDisabled === true && state.savedChecked === false && state.savedIsCheckbox, state.continueDisabled, item);
    check('capture mode hides all review regions', state.reviewChromeCount === 3 && state.reviewChromeHidden, {count:state.reviewChromeCount,hidden:state.reviewChromeHidden}, item);
    check('no viewport overflow', state.scrollWidth <= width + 1 && state.scrollHeight <= height + 1, [state.scrollWidth,state.scrollHeight], item);
    check('logo/title/menu fully inside viewport', state.bounds.every(({rect:r}) => r && r.width > 0 && r.height > 0 && r.x >= -1 && r.y >= -1 && r.right <= width + 1 && r.bottom <= height + 1), state.bounds, item);
    const logo = state.bounds.find(bound => bound.selector === '.brand-art').rect;
    const menu = state.bounds.find(bound => bound.selector === '.main-menu').rect;
    check('logo does not overlap menu', logo && menu && (logo.bottom <= menu.y || logo.right <= menu.x || menu.right <= logo.x), {logo,menu}, item);
    const capture = await cdp.call('Page.captureScreenshot', { format: 'png', fromSurface: true,
      captureBeyondViewport: false, clip: { x:0,y:0,width,height,scale:1 } }, session);
    const bytes = Buffer.from(capture.data, 'base64');
    check('PNG has exact dimensions', bytes.readUInt32BE(16) === width && bytes.readUInt32BE(20) === height, {width:bytes.readUInt32BE(16),height:bytes.readUInt32BE(20)}, item);
    await fs.writeFile(item.screenshot, bytes);
    if(concept==='06-prism'&&width===1920){
      const switched=await cdp.evaluate(`(async()=>{
        const buttons=[...document.querySelectorAll('[data-concept]')],art=document.querySelector('.brand-art');
        buttons[4].click();await art.decode();
        const result={query:new URL(location.href).searchParams.get('concept'),src:art.currentSrc,selected:buttons.map(button=>button.getAttribute('aria-pressed'))};
        buttons[0].click();await art.decode();return result;
      })()`,session);
      check('review tabs update URL, PNG and aria selection',switched.query==='10-covenant'&&switched.src.endsWith('/Artifacts/10-covenant.png')&&switched.selected[4]==='true'&&switched.selected.filter(value=>value==='true').length===1,switched,item);
      await cdp.evaluate(`(()=>{const nodes=[document.body,document.querySelector('.scene'),document.querySelector('.brand-art'),document.querySelector('.landscape'),document.querySelector('.readability')];window.__previewStyles=nodes.map(node=>[node,node.style.cssText]);nodes[0].style.background='transparent';nodes[1].style.background='transparent';nodes.slice(2).forEach(node=>node.style.visibility='hidden')})()`,session);
      await cdp.call('Emulation.setDefaultBackgroundColorOverride',{color:{r:0,g:0,b:0,a:0}},session);
      const overlay=await cdp.call('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false,clip:{x:0,y:0,width,height,scale:1}},session);
      item.menuOverlay=path.join(__dirname,'menu-overlay-1920.png');await fs.writeFile(item.menuOverlay,Buffer.from(overlay.data,'base64'));
      await cdp.evaluate(`window.__previewStyles.forEach(([node,css])=>node.style.cssText=css);delete window.__previewStyles`,session);
      await cdp.call('Emulation.setDefaultBackgroundColorOverride',{},session);
    }
    const focus = index => cdp.evaluate(`document.querySelectorAll('.menu-action')[${index}].focus()`, session);
    await focus(5);
    for (const [name,code,expected] of [['Home',36,1],['End',35,5],['ArrowDown',40,1],['ArrowUp',38,5]]) {
      const after = await key(cdp, session, name, code);
      check(`keyboard ${name} skips disabled continue`, after.activeIndex === expected && after.activeIndex !== state.continueIndex, {expected,actual:after.activeIndex}, item);
    }
    const buttonRect = state.bounds.find(bound => bound.selector === '.menu-action[2]').rect;
    const point = {x:buttonRect.x+buttonRect.width/2,y:buttonRect.y+buttonRect.height/2};
    await cdp.call('Input.dispatchMouseEvent', {type:'mouseMoved',...point}, session);
    await cdp.call('Input.dispatchMouseEvent', {type:'mousePressed',button:'left',clickCount:1,...point}, session);
    await cdp.call('Input.dispatchMouseEvent', {type:'mouseReleased',button:'left',clickCount:1,...point}, session);
    const clicked = await cdp.evaluate(snapshotScript, session);
    check('click changes visual selection and status only', clicked.url === state.url && clicked.selected.filter(value => value.selected).length === 1
      && clicked.selected[2].selected && clicked.selected[2].aria === 'true' && clicked.status.includes(state.labels[2]), {url:clicked.url,status:clicked.status,selected:clicked.selected}, item);
    await cdp.evaluate(`document.getElementById('saved-example').click()`, session);
    const saved = await cdp.evaluate(snapshotScript, session);
    check('saved checkbox enables continue', saved.savedChecked && saved.continueDisabled === false, {saved:saved.savedChecked,disabled:saved.continueDisabled}, item);
    await focus(5); const first = await key(cdp, session, 'Home', 36);
    check('keyboard Home reaches enabled continue', first.activeIndex === state.continueIndex, first.activeIndex, item);
    await cdp.evaluate(`document.getElementById('saved-example').click()`, session);
    const reset = await cdp.evaluate(snapshotScript, session);
    check('clearing saved checkbox disables continue', reset.savedChecked === false && reset.continueDisabled === true && reset.url === state.url, {saved:reset.savedChecked,disabled:reset.continueDisabled}, item);
  } catch (error) { item.error = error.stack || String(error); }
  finally { if (target) await cdp.call('Target.closeTarget', { targetId: target }).catch(() => {}); }
  item.passed = !item.error && item.checks.length > 0 && item.checks.every(value => value.passed);
}

async function main() {
  let child, cdp; let stderr = '', stdout = '';
  try {
    await fs.mkdir(evidence, { recursive: true });
    if (typeof WebSocket !== 'function') throw new Error('Node 26 with global WebSocket is required.');
    await fs.access(html);
    const candidates = [option('edge'), process.env.PRISM_EDGE_PATH,
      process.env['ProgramFiles(x86)'] && path.join(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
      process.env.ProgramFiles && path.join(process.env.ProgramFiles, 'Microsoft/Edge/Application/msedge.exe'),
      process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'Microsoft/Edge/Application/msedge.exe')].filter(Boolean);
    for (const candidate of candidates) { try { await fs.access(candidate); report.edge = candidate; break; } catch {} }
    if (!report.edge) throw new Error('Microsoft Edge was not found; use --edge=<absolute path>.');
    await fs.mkdir(profile, { recursive: false });
    child = spawn(report.edge, ['--headless=new','--remote-debugging-port=0',`--user-data-dir=${profile}`,
      '--no-first-run','--no-default-browser-check','--hide-scrollbars','--force-device-scale-factor=1',
      '--allow-file-access-from-files','about:blank'], { windowsHide: true, shell: false, stdio: ['ignore','pipe','pipe'] });
    report.spawnedPid = child.pid;
    child.stderr.on('data', data => { stderr += data; }); child.stdout.on('data', data => { stdout += data; });
    let launchError; child.on('error', error => { launchError = error; });
    let endpoint;
    for (let attempt = 0; attempt < 140; attempt++) {
      if (launchError) throw launchError;
      try {
        const lines = (await fs.readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).trim().split(/\r?\n/);
        if (/^\d+$/.test(lines[0]) && lines[1]?.startsWith('/devtools/browser/')) { endpoint = `ws://127.0.0.1:${lines[0]}${lines[1]}`; break; }
      } catch {}
      await delay(150);
    }
    if (!endpoint) throw new Error('Dedicated Edge CDP endpoint did not appear within 21 seconds.');
    cdp = new CDP(endpoint); await cdp.open();
    report.browserVersion = await cdp.call('Browser.getVersion');
    const concepts=JSON.parse(await fs.readFile(path.join(__dirname,'concepts.json'),'utf8'));
    for(const concept of concepts)await captureCase(cdp,1920,1080,concept.id);
    await captureCase(cdp,1366,768,concepts[0].id);
    check('no browser JavaScript exceptions', report.pageErrors.length === 0, report.pageErrors);
    check('no failed page resources', report.resourceFailures.length === 0, report.resourceFailures);
    report.passed = report.cases.length === concepts.length+1 && report.cases.every(value => value.passed) && report.checks.every(value => value.passed);
  } catch (error) { report.errors.push(error.stack || String(error)); }
  finally {
    if (cdp) { await cdp.call('Browser.close', {}, undefined, 1500).catch(() => {}); cdp.socket.close(); }
    if (child && child.exitCode === null) {
      await Promise.race([new Promise(resolve => child.once('exit', resolve)), delay(4000)]);
      if (child.exitCode === null) { child.kill(); report.ownedProcessTerminationFallback = true; }
    }
    report.finishedUtc = new Date().toISOString();
    await fs.mkdir(evidence, { recursive: true });
    const json = JSON.stringify(report, null, 2) + '\n';
    await fs.writeFile(path.join(evidence, `render-validation-${stamp}.json`), json);
    await fs.writeFile(path.join(evidence, 'render-validation-latest.json'), json);
    await fs.writeFile(path.join(evidence, `edge-${stamp}.stderr.txt`), stderr);
    await fs.writeFile(path.join(evidence, `edge-${stamp}.stdout.txt`), stdout);
    console.log(JSON.stringify({passed:report.passed,report:path.join(evidence,'render-validation-latest.json'),
      screenshots:report.cases.map(value => value.screenshot),errors:report.errors}, null, 2));
    if (!report.passed) process.exitCode = 1;
  }
}

main().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
