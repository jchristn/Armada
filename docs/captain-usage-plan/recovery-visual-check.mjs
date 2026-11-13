// Run from the repository root with Node 24 and installed Google Chrome on macOS.
// Checks representative existing surface markup against the production CSS bundle.
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
const root = process.cwd();
const temp = path.join(root, '.recovery-validation');
fs.mkdirSync(temp, { recursive: true });
const assets = path.join(root, 'src/Armada.Dashboard/dist/assets');
const css = fs.readdirSync(assets).filter(n => n.startsWith('index-') && n.endsWith('.css'));
if (css.length !== 1) throw Error('Build the dashboard first; expected one index CSS bundle');
const surfaces = {
  captains: '<h2>Captains</h2><div class="view-header"><select aria-label="Status"><option>All statuses</option></select><button class="btn">Add captain</button></div><div class="card"><span class="tag idle">Idle</span> <span class="tag working">Working</span> <span class="tag stalled">Stalled</span> <span class="tag quarantined">Quarantined</span></div>',
  captainEdit: '<h2>Captain settings</h2><div class="card"><div class="form-group"><label>Name</label><input aria-label="Name" value="Usage captain"></div><div class="form-group"><label>Runtime</label><select aria-label="Runtime"><option>Codex</option></select></div><button class="btn">Save</button></div>',
  tokenUsage: '<h2>Token usage</h2><div class="token-usage-controls"><div class="mission-history-time-tabs"><button class="mission-history-time-tab active">24 hours</button><button class="mission-history-time-tab">7 days</button></div><label class="token-usage-filter">Captain<select aria-label="Captain"><option>All captains</option></select></label></div><div class="card">No token usage for this time range.</div><button class="btn" disabled>Copy chart as image</button>',
  jobs: '<h2>Jobs</h2><div class="card"><span class="tag running">Running</span> <span class="tag succeeded">Succeeded</span> <span class="tag failed">Failed</span> <span class="tag cancelled">Cancelled</span><p>UsageProbe</p><button class="btn">Cancel</button></div>',
  settings: '<h2>Settings</h2><div class="card"><div class="form-group"><label>Retention days</label><input aria-label="Retention days" type="number" value="7"></div><div class="form-group"><label>Notes</label><textarea aria-label="Notes">Capture settings are deferred to phase 5.</textarea></div><button class="btn">Save</button></div>'
};
const fixture = path.join(temp, 'visual.html');
fs.writeFileSync(fixture, `<!doctype html><html><head><link rel="stylesheet" href="${pathToFileURL(path.join(assets, css[0]))}"></head><body><div id="root"><div class="app-shell"><div class="app-layout" style="grid-template-columns:220px 1fr"><aside class="sidebar">Armada</aside><div class="main-content-area"><main class="main"></main></div></div></div></div></body></html>`);
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', ['--headless=new', '--no-first-run', '--remote-debugging-port=19444', `--user-data-dir=${path.join(temp, 'chrome')}`, 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let ws;
try {
  let tabs;
  for (let i = 0; i < 100; i++) {
    try { tabs = await (await fetch('http://127.0.0.1:19444/json/list')).json(); break; } catch { await sleep(100); }
  }
  if (!tabs) throw Error('Chrome did not start');
  ws = new WebSocket(tabs.find(t => t.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = e => { const r = JSON.parse(e.data); if (pending.has(r.id)) { const p = pending.get(r.id); pending.delete(r.id); r.error ? p.reject(Error(JSON.stringify(r.error))) : p.resolve(r.result); } };
  const call = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify({ id: n, method, params })); });
  const evaluate = async expression => { const r = await call('Runtime.evaluate', { expression, returnByValue: true }); if (r.exceptionDetails) throw Error(JSON.stringify(r.exceptionDetails)); return r.result.value; };
  await call('Page.enable');
  await call('Page.navigate', { url: pathToFileURL(fixture).href });
  await sleep(500);
  const results = [];
  for (const theme of ['light', 'dark']) for (const width of [1280, 768, 390]) {
    await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
    for (const [surface, markup] of Object.entries(surfaces)) {
      await evaluate(`document.documentElement.dataset.theme=${JSON.stringify(theme)};document.querySelector('main').innerHTML=${JSON.stringify(markup + '<p class="text-dim">Loading...</p><div class="card">No records.</div><div class="alert alert-error">Unable to load. Try again.</div>')};document.activeElement.blur();`);
      await sleep(200);
      const geometry = await evaluate(`(() => { const main=document.querySelector('main'), pane=main.getBoundingClientRect(); const clipped=[...main.querySelectorAll('input,select,textarea,button,.card,.alert')].filter(e=>{const r=e.getBoundingClientRect();return r.width>0&&(r.left<pane.left-1||r.right>pane.right+1)}).map(e=>e.className||e.tagName);return {clipped,pageOverflow:document.documentElement.scrollWidth>innerWidth,font:getComputedStyle(document.body).fontSize,accent:getComputedStyle(document.documentElement).getPropertyValue('--accent').trim(),badges:[...main.querySelectorAll('.tag')].map(e=>({state:e.textContent,bg:getComputedStyle(e).backgroundColor,fg:getComputedStyle(e).color}))};})()`);
      const controls = await evaluate(`document.querySelectorAll('main button:not(:disabled),main input,main select,main textarea').length`);
      const focus = [];
      for (let i = 0; i < controls; i++) {
        await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
        await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
        await sleep(170);
        focus.push(await evaluate(`(() => {const e=document.activeElement,s=getComputedStyle(e);return {control:e.getAttribute('aria-label')||e.textContent,visible:e.matches(':focus-visible'),outline:s.outline,outlineWidth:s.outlineWidth,outlineStyle:s.outlineStyle};})()`));
      }
      const passed = !geometry.pageOverflow && geometry.clipped.length===0 && geometry.font==='14px' && focus.every(f=>f.visible&&f.outlineWidth==='2px'&&f.outlineStyle==='solid'&&f.outline.includes(theme==='light'?'rgb(37, 99, 235)':'rgb(96, 165, 250)')) && geometry.badges.every(b=>b.state!=='Running'||b.bg===(theme==='light'?'rgb(219, 234, 254)':'rgb(23, 37, 84)')) && geometry.badges.every(b=>b.state!=='Succeeded'||b.bg===(theme==='light'?'rgb(220, 252, 231)':'rgb(5, 46, 22)'));
      results.push({theme,width,surface,...geometry,focus,passed});
      if (theme==='dark'&&width===390&&surface==='settings') { const screenshot=await call('Page.captureScreenshot'); fs.writeFileSync(path.join(temp,'settings-dark-390.png'),Buffer.from(screenshot.data,'base64')); }
    }
  }
  fs.writeFileSync('docs/captain-usage-plan/recovery-visual-results.json', JSON.stringify({scope:'Representative markup with production CSS; not authenticated application pages',results},null,2)+'\n');
  console.log(`${results.filter(r=>r.passed).length}/${results.length} rendered surface checks passed`);
  if (results.some(r=>!r.passed)) { console.log(JSON.stringify(results.filter(r=>!r.passed))); process.exitCode=1; }
} finally { ws?.close(); chrome.kill(); }
