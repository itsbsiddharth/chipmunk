// Renders every marketing image from source, sharp on any screen:
//   node tools/shots.mjs
// Panel shots come from the real panel UI (ui/) with the browser mock host and demo captures
// (docs/shots/scenes.html), rendered at 3x. The README hero and the social card come from
// docs/hero.html and docs/og.html at 2x. Uses the Edge or Chrome already on this PC.
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { join, extname, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.webp': 'image/webp', '.svg': 'image/svg+xml', '.json': 'application/json' };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

// ---- a tiny static server for the repo ----
const server = createServer(async (req, res) => {
  const rel = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  // The website uses root paths (/styles.css), so anything not found in the repo is looked up in site/.
  let path = join(ROOT, rel);
  if (!existsSync(path) || rel === '/') path = join(ROOT, 'site', rel === '/' ? 'index.html' : rel);
  if (!path.startsWith(ROOT) || !existsSync(path)) { res.writeHead(404); res.end(); return; }
  try {
    const data = await readFile(path);
    res.writeHead(200, { 'Content-Type': MIME[extname(path)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(data);
  } catch { res.writeHead(404); res.end(); }
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const BASE = `http://127.0.0.1:${server.address().port}`;

// ---- a headless browser over the DevTools protocol ----
const browserPath = ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Google/Chrome/Application/chrome.exe'].find(existsSync);
if (!browserPath) throw new Error('Needs Microsoft Edge or Google Chrome.');
const profile = join(tmpdir(), 'chipmunk-shots-' + Date.now());
const port = 9400 + Math.floor(Math.random() * 400);
const browser = spawn(browserPath, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--hide-scrollbars', '--force-color-profile=srgb', '--font-render-hinting=none', 'about:blank']);
let ws;
for (let i = 0; i < 50 && !ws; i++) {
  await sleep(200);
  try {
    const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    const page = list.find(p => p.type === 'page');
    if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); await new Promise(r => ws.onopen = r); }
  } catch {}
}
let seq = 0;
const waiting = new Map();
ws.onmessage = (m) => {
  const d = JSON.parse(m.data);
  if (d.id && waiting.has(d.id)) { waiting.get(d.id)(d); waiting.delete(d.id); }
  // Surface page errors: a broken page would otherwise produce a quietly wrong image.
  if (d.method === 'Runtime.exceptionThrown') console.error('page error:', d.params.exceptionDetails.exception?.description || d.params.exceptionDetails.text);
  if (d.method === 'Runtime.consoleAPICalled' && d.params.type === 'error') console.error('console error:', d.params.args.map(a => a.value || a.description).join(' '));
};
const cdp = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++seq;
  waiting.set(id, (d) => d.error ? reject(new Error(method + ': ' + d.error.message)) : resolve(d.result));
  ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async (expression) => (await cdp('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })).result.value;

async function open(url, width, height, scale, dark = false) {
  await cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: scale, mobile: false });
  await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: dark ? 'dark' : 'light' }, { name: 'prefers-reduced-motion', value: 'reduce' }] });
  await evaluate('window.__oldPage = true');
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 80; i++) { await sleep(150); if (await evaluate(`!window.__oldPage && document.readyState === 'complete'`).catch(() => false)) break; }
  await evaluate('document.fonts.ready.then(() => true)');
  await sleep(600);
}

async function capture(file, { clip, format } = {}) {
  const fmt = format || (file.endsWith('.webp') ? 'webp' : 'png');
  const r = await cdp('Page.captureScreenshot', { format: fmt, ...(fmt === 'webp' ? { quality: 94 } : {}), ...(clip ? { clip: { ...clip, scale: 1 } } : {}), captureBeyondViewport: false });
  await mkdir(dirname(file), { recursive: true });
  await writeFile(file, Buffer.from(r.data, 'base64'));
  console.log('wrote', file.replace(ROOT, '.'));
  return 'data:image/' + fmt + ';base64,' + r.data;
}

await cdp('Page.enable');
await cdp('Runtime.enable');

// 1. Demo captures the panel will show (2x, like a real high-DPI screenshot)
await open(`${BASE}/docs/shots/scenes.html`, 1200, 4000, 2);
const demo = {};
for (const [id, small] of [['home', 'demo-home'], ['order', 'demo-order'], ['reviews', 'demo-reviews']]) {
  const box = await evaluate(`(() => { const r = document.getElementById('${id}').getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height }; })()`);
  const r = await cdp('Page.captureScreenshot', { format: 'png', clip: { ...box, scale: 1 } });
  demo[id] = 'data:image/png;base64,' + r.data;
  // A small, still-sharp copy for the website's animated demo tiles (shown about 120px wide).
  const t = await cdp('Page.captureScreenshot', { format: 'webp', quality: 92, clip: { ...box, scale: 0.2 } });
  await writeFile(join(ROOT, 'site/assets', small + '.webp'), Buffer.from(t.data, 'base64'));
  console.log('wrote', './site/assets/' + small + '.webp');
}
// `node tools/shots.mjs --scenes <folder>` stops here after saving the full-size scenes, for a quick look.
if (process.argv[2] === '--scenes') {
  for (const [id, url] of Object.entries(demo)) await writeFile(join(process.argv[3], id + '.png'), Buffer.from(url.split(',')[1], 'base64'));
  ws.close(); browser.kill(); server.close(); process.exit(0);
}
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `window.__CHIPMUNK_DEMO = ${JSON.stringify(demo)};` });

// 2. The panel at 3x: 470 x 820 points becomes 1410 x 2460 pixels
const W = 470, H = 820, S = 3;
const panel = `${BASE}/ui/index.html?onboarded`;
const out = (name) => [join(ROOT, 'docs/img', name + '.png'), join(ROOT, 'site/assets', name + '.webp')];
async function shotBoth(name) { const [png, webp] = out(name); await capture(png); await capture(webp); }

await open(panel, W, H, S);
await evaluate(`document.activeElement && document.activeElement.blur(), true`);
await shotBoth('panel-light');

await open(panel, W, H, S, true);
await evaluate(`document.activeElement && document.activeElement.blur(), true`);
await shotBoth('panel-dark');

await open(panel, W, H, S);
await evaluate(`document.getElementById('btn-send-more').click(), true`);
await sleep(700);
await shotBoth('send-menu');

await open(panel, W, H, S);
await evaluate(`[...document.querySelectorAll('.chip-btn')].find(b => b.textContent.includes('Past chats')).click(), true`);
await sleep(900);
await evaluate(`[...document.querySelectorAll('.source-tab')].find(b => b.textContent.startsWith('ChatGPT')).click(), true`);
await sleep(900);
await evaluate(`document.querySelector('.session-item').click(), true`);
await sleep(900);
await evaluate(`(() => { const b = [...document.querySelectorAll('.turn-body')]; b[0].click(); const imgs = document.querySelectorAll('.turn-img'); imgs[0].click(); imgs[1].click(); return true; })()`);
await sleep(400);
await shotBoth('past-chats');

// 3. README hero and social card at 2x
await open(`${BASE}/docs/hero.html`, 1600, 900, 2);
await capture(join(ROOT, 'docs/img/hero.png'));
await open(`${BASE}/docs/og.html`, 1200, 630, 2);
await capture(join(ROOT, 'site/assets/og.png'));

// 4. The hero animation from the website, as an animated WebP for the README and posts.
//    The page clock is frozen and stepped one frame at a time, so motion is perfectly smooth.
{
  const FPS = 15, SECONDS = 12.6;
  await open(`${BASE}/site/index.html`, 1280, 900, 2);
  const box = await evaluate(`(() => { const d = document.getElementById('demo'); d.scrollIntoView({ block: 'center' }); const r = d.getBoundingClientRect(); const steps = d.querySelector('.steps-ind').getBoundingClientRect(); const bottom = Math.max(r.bottom, steps.bottom) + 28; return { x: Math.max(0, r.x - 28), y: Math.max(0, r.y - 28), width: r.width + 56, height: bottom - Math.max(0, r.y - 28) }; })()`);
  // Restart the page with a paused virtual clock, then step it.
  await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }, { name: 'prefers-reduced-motion', value: 'no-preference' }] });
  await cdp('Emulation.setVirtualTimePolicy', { policy: 'pause' });
  await cdp('Page.navigate', { url: `${BASE}/site/index.html` });
  await cdp('Emulation.setVirtualTimePolicy', { policy: 'advance', budget: 100 });
  await sleep(2500);
  await evaluate(`document.getElementById('demo').scrollIntoView({ block: 'center' }), true`);
  const dir = join(tmpdir(), 'chipmunk-frames-' + Date.now());
  await mkdir(dir, { recursive: true });
  const frames = Math.round(FPS * SECONDS);
  for (let i = 0; i < frames; i++) {
    await new Promise((resolve) => {
      const done = (m) => { const d = JSON.parse(m.data); if (d.method === 'Emulation.virtualTimeBudgetExpired') { ws.removeEventListener('message', done); resolve(); } };
      ws.addEventListener('message', done);
      cdp('Emulation.setVirtualTimePolicy', { policy: 'advance', budget: 1000 / FPS });
    });
    const r = await cdp('Page.captureScreenshot', { format: 'png', clip: { ...box, scale: 1 } });
    await writeFile(join(dir, String(i).padStart(4, '0') + '.png'), Buffer.from(r.data, 'base64'));
  }
  await new Promise((resolve, reject) => {
    const py = spawn('python', [join(ROOT, 'tools/frames2webp.py'), dir, join(ROOT, 'docs/img/demo.webp'), String(Math.round(1000 / FPS))], { stdio: 'inherit' });
    py.on('exit', (code) => code === 0 ? resolve() : reject(new Error('frames2webp failed')));
  });
  await rm(dir, { recursive: true, force: true });
}

ws.close();
browser.kill();
server.close();
await sleep(500);
await rm(profile, { recursive: true, force: true }).catch(() => {});
