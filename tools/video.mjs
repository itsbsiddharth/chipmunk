// Renders the launch video (docs/video/launch.html) to release/chipmunk-launch.mp4:
//   node tools/video.mjs              full video
//   node tools/video.mjs --stills 3,12,22,28   a few frames as PNGs into release/, for a quick look
// Each frame is drawn exactly with render(t), captured from the Edge or Chrome on this PC, encoded
// to H.264 by the browser itself (WebCodecs), and packed into an MP4 here. No ffmpeg needed.
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import { existsSync, createReadStream } from 'node:fs';
import { spawn } from 'node:child_process';
import { join, extname, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, 'release', 'chipmunk-launch.mp4');
const FPS = 30, W = 1920, H = 1080, BITRATE = 12_000_000;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp4': 'video/mp4' };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const frames = join(tmpdir(), 'chipmunk-video-' + Date.now());
await mkdir(frames, { recursive: true });
let upload = null;

const server = createServer(async (req, res) => {
  const url = new URL(req.url, 'http://x');
  if (req.method === 'POST' && url.pathname === '/__upload') {
    const parts = []; for await (const c of req) parts.push(c);
    upload = { meta: JSON.parse(decodeURIComponent(req.headers['x-meta'])), data: Buffer.concat(parts) };
    res.end('ok'); return;
  }
  if (url.pathname === '/__blank') { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<!doctype html><meta charset=utf-8><title>encode</title>'); return; }
  const path = url.pathname.startsWith('/__frames/') ? join(frames, url.pathname.slice(10)) : url.pathname === '/__out.mp4' ? OUT : join(ROOT, decodeURIComponent(url.pathname));
  if (!existsSync(path)) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'Content-Type': MIME[extname(path)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  createReadStream(path).pipe(res);
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const BASE = `http://127.0.0.1:${server.address().port}`;

// headless browser over the DevTools protocol
const browserPath = ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Google/Chrome/Application/chrome.exe'].find(existsSync);
const profile = join(tmpdir(), 'chipmunk-video-profile-' + Date.now());
const port = 9800 + Math.floor(Math.random() * 150);
const browser = spawn(browserPath, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--hide-scrollbars', '--force-color-profile=srgb', '--autoplay-policy=no-user-gesture-required', 'about:blank']);
let ws;
for (let i = 0; i < 50 && !ws; i++) {
  await sleep(200);
  try {
    const page = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((p) => p.type === 'page');
    if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); await new Promise((r) => ws.onopen = r); }
  } catch {}
}
let seq = 0;
const waiting = new Map();
ws.onmessage = (m) => {
  const d = JSON.parse(m.data);
  if (d.id && waiting.has(d.id)) { waiting.get(d.id)(d); waiting.delete(d.id); }
  if (d.method === 'Runtime.exceptionThrown') console.error('page error:', d.params.exceptionDetails.exception?.description || d.params.exceptionDetails.text);
  if (d.method === 'Runtime.consoleAPICalled') console.log('page:', d.params.args.map((a) => a.value ?? a.description).join(' '));
};
const cdp = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++seq;
  waiting.set(id, (d) => d.error ? reject(new Error(method + ': ' + d.error.message)) : resolve(d.result));
  ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async (expression) => {
  const r = await cdp('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};
async function open(url) {
  await evaluate('window.__old = true').catch(() => {});
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 100; i++) { await sleep(150); if (await evaluate(`!window.__old && document.readyState === 'complete'`).catch(() => false)) break; }
}

await cdp('Page.enable');
await cdp('Runtime.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false });
await open(`${BASE}/docs/video/launch.html`);
await evaluate('window.ready');
const duration = await evaluate('window.DURATION');
const draw = (t) => evaluate(`render(${t}), new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(() => r(true))))`);

if (process.argv[2] === '--stills') {
  await mkdir(dirname(OUT), { recursive: true });
  for (const t of process.argv[3].split(',').map(Number)) {
    await draw(t);
    const r = await cdp('Page.captureScreenshot', { format: 'png' });
    await writeFile(join(dirname(OUT), `still-${t}.png`), Buffer.from(r.data, 'base64'));
    console.log('wrote', `release/still-${t}.png`);
  }
} else {
  // 1. every frame, exactly
  const n = Math.round(duration * FPS);
  for (let i = 0; i < n; i++) {
    await draw(i / FPS);
    const r = await cdp('Page.captureScreenshot', { format: 'jpeg', quality: 95 });
    await writeFile(join(frames, String(i).padStart(5, '0') + '.jpg'), Buffer.from(r.data, 'base64'));
    if (i % 150 === 0) console.log(`frame ${i}/${n}`);
  }

  // 2. encode to H.264 in the browser
  await open(`${BASE}/__blank`);
  const codec = await evaluate(`(async () => {
    for (const codec of ['avc1.640028', 'avc1.4d0028', 'avc1.42e028']) {
      const s = await VideoEncoder.isConfigSupported({ codec, width: ${W}, height: ${H}, bitrate: ${BITRATE}, framerate: ${FPS}, avc: { format: 'avc' } });
      if (s.supported) return codec;
    }
    return null;
  })()`);
  if (!codec) throw new Error('This browser cannot encode H.264.');
  console.log('encoding with', codec);
  await evaluate(`(async () => {
    const chunks = [], sizes = [], keys = []; let avcC = null, err = null;
    const enc = new VideoEncoder({
      output: (chunk, meta) => {
        if (meta && meta.decoderConfig && meta.decoderConfig.description && !avcC) avcC = new Uint8Array(meta.decoderConfig.description.buffer || meta.decoderConfig.description);
        const b = new Uint8Array(chunk.byteLength); chunk.copyTo(b); chunks.push(b); sizes.push(b.length); keys.push(chunk.type === 'key');
      },
      error: (e) => { err = e; },
    });
    enc.configure({ codec: '${codec}', width: ${W}, height: ${H}, bitrate: ${BITRATE}, framerate: ${FPS}, avc: { format: 'avc' }, latencyMode: 'quality' });
    for (let i = 0; i < ${n}; i++) {
      const blob = await (await fetch('/__frames/' + String(i).padStart(5, '0') + '.jpg')).blob();
      const bmp = await createImageBitmap(blob);
      const frame = new VideoFrame(bmp, { timestamp: Math.round(i * 1e6 / ${FPS}), duration: Math.round(1e6 / ${FPS}) });
      enc.encode(frame, { keyFrame: i % (${FPS} * 2) === 0 });
      frame.close(); bmp.close();
      while (enc.encodeQueueSize > 6) await new Promise((r) => setTimeout(r, 5));
      if (err) throw err;
    }
    await enc.flush();
    if (err) throw err;
    let s = ''; for (const x of avcC) s += String.fromCharCode(x);
    const meta = encodeURIComponent(JSON.stringify({ sizes, keys, avcC: btoa(s) }));
    await fetch('/__upload', { method: 'POST', headers: { 'x-meta': meta }, body: new Blob(chunks) });
    return true;
  })()`);
  if (!upload) throw new Error('No encoded video came back.');

  // 3. pack into an MP4 (moov first, so it starts playing before it fully downloads)
  const { sizes, keys, avcC } = upload.meta;
  await mkdir(dirname(OUT), { recursive: true });
  await writeFile(OUT, Buffer.concat([...mp4Header({ sizes, keys, avcC: Buffer.from(avcC, 'base64') }), upload.data]));
  console.log('wrote', OUT.replace(ROOT, '.'), (upload.data.length / 1e6).toFixed(1) + ' MB');

  // 4. check it plays: duration, size, and a decoded frame
  await open(`${BASE}/__blank`);
  const info = await evaluate(`new Promise((resolve) => {
    const v = document.createElement('video'); v.muted = true; v.src = '/__out.mp4';
    v.onloadedmetadata = () => { v.currentTime = 14; };
    v.onseeked = () => resolve({ duration: v.duration, width: v.videoWidth, height: v.videoHeight, frames: v.getVideoPlaybackQuality ? true : false });
    v.onerror = () => resolve({ error: v.error && v.error.message });
  })`);
  console.log('plays back:', JSON.stringify(info));
}

ws.close(); browser.kill(); server.close();
await sleep(500);
await rm(frames, { recursive: true, force: true }).catch(() => {});
await rm(profile, { recursive: true, force: true }).catch(() => {});

function mp4Header({ sizes, keys, avcC }) {
  const box = (type, ...parts) => { const body = Buffer.concat(parts.map((p) => Buffer.isBuffer(p) ? p : Buffer.from(p, 'latin1'))); const h = Buffer.alloc(8); h.writeUInt32BE(8 + body.length); h.write(type, 4, 'latin1'); return Buffer.concat([h, body]); };
  const full = (type, version, flags, ...parts) => { const v = Buffer.alloc(4); v.writeUInt32BE(((version << 24) | flags) >>> 0); return box(type, v, ...parts); };
  const u32 = (...n) => { const b = Buffer.alloc(4 * n.length); n.forEach((x, i) => b.writeUInt32BE(x >>> 0, 4 * i)); return b; };
  const u16 = (...n) => { const b = Buffer.alloc(2 * n.length); n.forEach((x, i) => b.writeUInt16BE(x & 0xffff, 2 * i)); return b; };
  const MATRIX = u32(0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000);
  const n = sizes.length, timescale = FPS * 1000, delta = 1000, mvDur = Math.round(n * 1000 / FPS);
  const ftyp = box('ftyp', 'isom', u32(512), 'isom', 'iso2', 'avc1', 'mp41');
  const sync = keys.map((k, i) => k ? i + 1 : 0).filter(Boolean);
  const moov = (offset) => box('moov',
    full('mvhd', 0, 0, u32(0, 0, 1000, mvDur, 0x00010000), u16(0x0100, 0), Buffer.alloc(8), MATRIX, Buffer.alloc(24), u32(2)),
    box('trak',
      full('tkhd', 0, 3, u32(0, 0, 1, 0, mvDur), Buffer.alloc(8), u16(0, 0, 0, 0), MATRIX, u32(W * 65536, H * 65536)),
      box('mdia',
        full('mdhd', 0, 0, u32(0, 0, timescale, n * delta), u16(0x55c4, 0)),
        full('hdlr', 0, 0, u32(0), 'vide', Buffer.alloc(12), 'VideoHandler\0'),
        box('minf',
          full('vmhd', 0, 1, Buffer.alloc(8)),
          box('dinf', full('dref', 0, 0, u32(1), full('url ', 0, 1))),
          box('stbl',
            full('stsd', 0, 0, u32(1), box('avc1', Buffer.alloc(6), u16(1), Buffer.alloc(16), u16(W, H), u32(0x00480000, 0x00480000, 0), u16(1), Buffer.alloc(32), u16(0x0018, 0xffff), box('avcC', avcC))),
            full('stts', 0, 0, u32(1, n, delta)),
            full('stss', 0, 0, u32(sync.length, ...sync)),
            full('stsc', 0, 0, u32(1, 1, n, 1)),
            full('stsz', 0, 0, u32(0, n, ...sizes)),
            full('stco', 0, 0, u32(1, offset)))))));
  const total = sizes.reduce((a, b) => a + b, 0);
  const head = moov(0).length + ftyp.length + 8;
  const mdat = Buffer.alloc(8); mdat.writeUInt32BE(8 + total); mdat.write('mdat', 4, 'latin1');
  return [ftyp, moov(head), mdat];
}
