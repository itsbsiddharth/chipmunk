// In-memory stand-in for the native host, used when the UI is opened in a normal
// browser. It mirrors the host's commands closely enough to develop and test the UI.
const wait = (ms) => new Promise(r => setTimeout(r, ms));
const now = () => new Date().toISOString();
const id = (p) => p + Math.random().toString(36).slice(2, 9);

function fakeShot(label, hue) {
  const c = document.createElement('canvas');
  c.width = 640; c.height = 360;
  const g = c.getContext('2d');
  g.fillStyle = `hsl(${hue} 30% 96%)`; g.fillRect(0, 0, 640, 360);
  g.fillStyle = `hsl(${hue} 25% 88%)`; g.fillRect(0, 0, 640, 34);
  ['#ff5f57', '#febc2e', '#28c840'].forEach((col, i) => { g.fillStyle = col; g.beginPath(); g.arc(18 + i * 18, 17, 5, 0, 7); g.fill(); });
  g.fillStyle = `hsl(${hue} 50% 45%)`; g.fillRect(24, 60, 220, 16);
  g.fillStyle = `hsl(${hue} 15% 70%)`;
  for (let i = 0; i < 7; i++) g.fillRect(24, 96 + i * 26, 300 + ((i * 53) % 260), 10);
  g.fillStyle = `hsl(${hue} 60% 55%)`; g.fillRect(400, 220, 200, 110);
  g.fillStyle = '#fff'; g.font = 'bold 20px Segoe UI, sans-serif'; g.fillText(label, 414, 284);
  return c.toDataURL('image/png');
}

function inferKind(text) {
  const t = text.toLowerCase();
  if (/screen ?shot|screenshot|image|photo|picture|snip|\bui\b/.test(t)) return 'image';
  if (/\.(csv|pdf|docx?|txt|json|ipynb|py|js|ts|log|zip|xlsx?|md)\b|\bfile\b|notebook|\blog\b/.test(t)) return 'file';
  if (/\blink\b|\burl\b|\bdocs?\b|source|article|page/.test(t)) return 'link';
  if (/quote|snippet|message|paragraph|text/.test(t)) return 'text';
  return null;
}

export class MockHost {
  constructor(emit) {
    this.emit = emit;
    this.settings = {
      hotkeys: { toggle: 'Win+Alt+P', snip: 'Win+Alt+S', grab: 'Win+Alt+C', paste: 'Win+Alt+V' },
      pasteDelayMs: 450, shrinkImages: true, terminalPaths: true, restoreClipboard: true,
      collectText: false, startWithWindows: false, alwaysOnTop: false, theme: 'system',
      showPet: true, petHints: true, usedExtension: false,
      dictation: new URLSearchParams(location.search).has('wispr') ? { app: 'Wispr Flow', hint: 'hold Ctrl+Win' } : { app: null, hint: 'Win+H' },
      onboarded: new URLSearchParams(location.search).has('onboarded'), dataDir: 'C:\\Users\\you\\Documents\\Chipmunk', version: '1.1.0 (preview)',
    };
    this.collect = false;
    this.drafts = new Map();
    this.trash = new Map();
    this.removed = new Map();
    this.versionsStore = new Map();
    this.shotNo = 3;

    const a = this.make('Fix Colab run that stops midway', 'My Colab notebook stops after ~40 minutes with the screen below. I am training the token-patching run from the repo.\n\nWhat I need:\n1. Why it stops (OOM? idle timeout?)\n2. A fix that keeps the run alive\n3. How to resume from the last checkpoint');
    a.attachments.push(
      this.img('Screenshot 2:41 PM', 'Win+Shift+S · Chrome', 210, 1920, 1080),
      this.img('Screenshot 2:43 PM', 'Win+Shift+S · Chrome', 160, 1600, 900),
      { id: id('a'), kind: 'file', name: 'train_patching.ipynb', ext: 'ipynb', bytes: 48210, source: 'Dropped', include: true },
      { id: id('a'), kind: 'text', text: 'RuntimeError: CUDA out of memory. Tried to allocate 2.00 GiB (GPU 0; 15.89 GiB total capacity; 13.42 GiB already allocated)', source: 'Colab — Google Chrome', include: true },
      { id: id('a'), kind: 'link', url: 'https://research.google.com/colaboratory/faq.html', name: 'Colab FAQ: runtime limits', include: true });
    a.checklist = [
      { id: 'c1', text: 'screenshot of the error', done: true, attId: a.attachments[0].id },
      { id: 'c2', text: 'GPU usage graph screenshot', done: false, attId: null },
      { id: 'c3', text: 'the notebook file', done: true, attId: a.attachments[2].id },
    ];
    a.updated = new Date(Date.now() - 4 * 60000).toISOString();

    const b = this.make('Landing page hero copy', 'Rewrite the hero section so it explains Chipmunk in one line.');
    b.status = 'sent'; b.sends.push({ at: new Date(Date.now() - 86400000).toISOString(), target: 'Claude' });
    b.updated = new Date(Date.now() - 86400000).toISOString();
    b.attachments.push(this.img('Screenshot 9:12 AM', 'Collect · Edge', 30, 1440, 900));

    const c = this.make('Resume bullet points', 'Tighten these into 3 bullets with numbers.');
    c.pinned = true; c.updated = new Date(Date.now() - 3 * 86400000).toISOString();

    this.current = a.id;
    this.versionsStore.set(a.id, [
      { name: 'v3', time: new Date(Date.now() - 6 * 60000).toISOString(), text: 'My Colab notebook stops after ~40 minutes.\n\nWhat I need: why it stops.' },
      { name: 'v2', time: new Date(Date.now() - 20 * 60000).toISOString(), text: 'Colab keeps stopping.' },
    ]);

    this.windows = [
      { hwnd: '101', label: 'ChatGPT', app: 'ChatGPT', title: 'ChatGPT', terminal: false, ai: true },
      { hwnd: '102', label: 'Claude Code', app: 'Terminal', title: 'Claude Code · chipmunk', terminal: true, ai: true },
      { hwnd: '103', label: 'Antigravity', app: 'Antigravity', title: 'Antigravity · landing-page', terminal: false, ai: true },
      { hwnd: '104', label: 'Claude', app: 'Chrome', title: 'Fix the onboarding flow - Claude', terminal: false, ai: true },
      { hwnd: '105', label: 'Slack', app: 'Slack', title: 'Slack · #design', terminal: false, ai: false },
      { hwnd: '106', label: 'File Explorer', app: 'File Explorer', title: 'Downloads', terminal: false, ai: false },
    ];
    this.saved = {
      chatgpt: [
        { id: 'g1', title: 'Debug the Colab OOM', sub: 'chatgpt.com', updated: new Date(Date.now() - 2 * 3600000).toISOString(), turns: 3, images: 2 },
        { id: 'g2', title: 'Pricing page ideas', sub: 'chatgpt.com', updated: new Date(Date.now() - 4 * 86400000).toISOString(), turns: 6, images: 0 },
      ],
      claude: [], gemini: [],
    };
    setTimeout(() => this.emit('target', this.windows[0]), 50);
  }

  make(title, text) {
    const d = { id: id('d'), title, text, status: 'draft', pinned: false, created: now(), updated: now(), checklist: [], attachments: [], sends: [], armedItem: null, folder: 'Documents\\Chipmunk\\drafts' };
    this.drafts.set(d.id, d);
    return d;
  }

  img(name, source, hue, w, h) {
    return { id: id('a'), kind: 'image', name, source, url: fakeShot(name.replace('Screenshot ', ''), hue), w, h, bytes: Math.round(w * h * 0.35), include: true };
  }

  summary(d) {
    const imgs = d.attachments.filter(a => a.kind === 'image');
    return {
      id: d.id, title: d.title, status: d.status, pinned: d.pinned, updated: d.updated,
      preview: d.text.split('\n')[0].slice(0, 120), imageCount: imgs.length, attachmentCount: d.attachments.length,
      thumbs: imgs.slice(0, 3).map(a => a.url), imported: !!d.imported,
    };
  }

  list() {
    return [...this.drafts.values()].sort((x, y) => y.updated.localeCompare(x.updated)).map(d => this.summary(d));
  }

  copy(d) { return JSON.parse(JSON.stringify(d)); }

  addAtt(d, att, linkItem) {
    d.attachments.push(att);
    const target = linkItem ? d.checklist.find(i => i.id === linkItem)
      : d.armedItem ? d.checklist.find(i => i.id === d.armedItem)
      : d.checklist.find(i => !i.done && !i.attId && inferKind(i.text) === att.kind);
    if (target) { target.done = true; target.attId = att.id; }
    d.armedItem = null;
    d.updated = now();
  }

  async call(cmd, args = {}, files) {
    await wait(30);
    const d = args.id ? this.drafts.get(args.id) : null;
    switch (cmd) {
      case 'init':
        return { settings: this.settings, drafts: this.list(), current: this.copy(this.drafts.get(this.current)), target: null, collect: this.collect };
      case 'listDrafts': return this.list();
      case 'getDraft': return this.copy(d);
      case 'setCurrent': this.current = args.id; return true;
      case 'saveText': d.text = args.text; d.updated = now(); return { updated: d.updated };
      case 'updateDraft': {
        const p = args.patch;
        if (p.title !== undefined) d.title = p.title;
        if (p.checklist) d.checklist = p.checklist;
        if (p.pinned !== undefined) d.pinned = p.pinned;
        if (p.status) d.status = p.status;
        if (p.order) d.attachments = p.order.map(x => d.attachments.find(a => a.id === x));
        d.updated = now();
        return this.copy(d);
      }
      case 'armItem': d.armedItem = args.itemId; return this.copy(d);
      case 'updateAttachment': Object.assign(d.attachments.find(a => a.id === args.attId), args.patch); return this.copy(d);
      case 'removeAttachment': {
        const i = d.attachments.findIndex(a => a.id === args.attId);
        this.removed.set(args.attId, { att: d.attachments[i], index: i });
        d.attachments.splice(i, 1);
        d.checklist.forEach(it => { if (it.attId === args.attId) { it.attId = null; it.done = false; } });
        return this.copy(d);
      }
      case 'restoreAttachment': {
        const r = this.removed.get(args.attId);
        if (r) d.attachments.splice(r.index, 0, r.att);
        return this.copy(d);
      }
      case 'snip': {
        await wait(600);
        this.addAtt(d, this.img(`Screenshot ${new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`, 'Snip', (this.shotNo++ * 47) % 360, 1920, 1080));
        this.emit('draftChanged', this.copy(d));
        this.emit('toast', { text: 'Screenshot added' });
        return true;
      }
      case 'pickFiles':
        this.addAtt(d, { id: id('a'), kind: 'file', name: 'requirements.txt', ext: 'txt', bytes: 812, source: 'Chosen', include: true });
        return this.copy(d);
      case 'addClipboard':
        this.addAtt(d, this.img('Pasted image', 'Clipboard', (this.shotNo++ * 47) % 360, 1280, 720));
        return { draft: this.copy(d), message: 'Added the image from your clipboard' };
      case 'addLink':
        this.addAtt(d, { id: id('a'), kind: 'link', url: args.url, name: args.url.replace(/^https?:\/\//, '').slice(0, 60), include: true }, args.linkItem);
        return this.copy(d);
      case 'addText':
        this.addAtt(d, { id: id('a'), kind: 'text', text: args.text, source: args.source || 'Text', include: true }, args.linkItem);
        return this.copy(d);
      case 'addFiles': {
        (files || []).forEach(f => {
          const isImg = /^image\//.test(f.type);
          this.addAtt(d, isImg
            ? { id: id('a'), kind: 'image', name: f.name, source: 'Dropped', url: URL.createObjectURL(f), w: 1200, h: 800, bytes: f.size, include: true }
            : { id: id('a'), kind: 'file', name: f.name, ext: f.name.split('.').pop(), bytes: f.size, source: 'Dropped', include: true }, args.linkItem);
        });
        return { draft: this.copy(d), missing: [] };
      }
      case 'addData':
        this.addAtt(d, { id: id('a'), kind: 'image', name: args.name, source: 'Dropped', url: args.data, include: true }, args.linkItem);
        return this.copy(d);
      case 'openAttachment': this.emit('toast', { text: '(mock) Would open the file in its app' }); return true;
      case 'revealDraft': case 'openDataFolder': this.emit('toast', { text: '(mock) Would open File Explorer' }); return true;
      case 'newDraft': {
        let n;
        if (args.duplicateOf) {
          const src = this.drafts.get(args.duplicateOf);
          n = this.make(src.title + ' (copy)', src.text);
          n.attachments = JSON.parse(JSON.stringify(src.attachments));
          n.checklist = JSON.parse(JSON.stringify(src.checklist));
        } else n = this.make('', '');
        this.current = n.id;
        return this.copy(n);
      }
      case 'deleteDraft': {
        this.trash.set(args.id, this.drafts.get(args.id));
        this.drafts.delete(args.id);
        if (!this.drafts.size) this.make('', '');
        const next = this.list()[0].id;
        this.current = next;
        return { currentId: next };
      }
      case 'undeleteDraft': {
        const t = this.trash.get(args.id);
        this.drafts.set(t.id, t);
        this.current = t.id;
        return this.copy(t);
      }
      case 'versions':
        return (this.versionsStore.get(args.id) || []).map(v => ({ name: v.name, time: v.time, preview: v.text.split('\n')[0], words: (v.text.match(/\S+/g) || []).length }));
      case 'getVersion': return (this.versionsStore.get(args.id) || []).find(v => v.name === args.name).text;
      case 'restoreVersion': {
        const v = (this.versionsStore.get(args.id) || []).find(x => x.name === args.name);
        d.text = v.text;
        return this.copy(d);
      }
      case 'setSettings': {
        const p = args.patch;
        if (p.hotkeys) this.settings.hotkeys = { ...this.settings.hotkeys, ...p.hotkeys };
        Object.keys(p).forEach(k => { if (k !== 'hotkeys') this.settings[k] = p[k]; });
        return { ...this.settings };
      }
      case 'setCollect': this.collect = args.on; return true;
      case 'send': {
        if (args.how === 'paste') {
          d.status = 'sent'; d.sends.push({ at: now(), target: 'Chrome' });
          const w = this.windows.find(x => x.hwnd === args.hwnd) || this.windows[0];
          this.emit('toast', { text: `(mock) Pasted everything into ${w.label}` });
          return { draft: this.copy(d) };
        }
        return { draft: this.copy(d), message: args.how === 'copyText' ? 'Copied the text' : 'Copied 3 files. Paste them anywhere.' };
      }
      case 'targets': return this.windows;
      case 'historySources':
        return [
          { id: 'claude-code', name: 'Claude Code', count: -1, local: true },
          { id: 'codex', name: 'Codex', count: -1, local: true },
          { id: 'antigravity', name: 'Antigravity', count: -1, local: true },
          { id: 'chatgpt', name: 'ChatGPT', count: this.saved.chatgpt.length, local: false },
          { id: 'claude', name: 'Claude', count: this.saved.claude.length, local: false },
          { id: 'gemini', name: 'Gemini', count: this.saved.gemini.length, local: false },
        ];
      case 'historySessions':
        await wait(150);
        if (this.saved[args.source]) return this.saved[args.source];
        return [
          { id: 's1', title: 'CaML token patching run', sub: 'caml-work', updated: new Date(Date.now() - 31 * 86400000).toISOString(), turns: 9, images: 5 },
          { id: 's2', title: 'Portfolio website redesign', sub: 'my-website', updated: new Date(Date.now() - 6 * 86400000).toISOString(), turns: 14, images: 1 },
          { id: 's3', title: 'Streamer overlay ideas', sub: 'streamer-projects', updated: new Date(Date.now() - 18 * 86400000).toISOString(), turns: 4, images: 6 },
        ];
      case 'historyConversation': {
        await wait(150);
        const names = { 'claude-code': 'Claude Code', codex: 'Codex', antigravity: 'Antigravity', chatgpt: 'ChatGPT', claude: 'Claude', gemini: 'Gemini' };
        return {
          source: args.source, sourceName: names[args.source], id: args.sessionId, title: 'Debug the Colab OOM',
          turns: [
            { index: 0, role: 'user', time: new Date(Date.now() - 3 * 3600000).toISOString(), text: 'My Colab notebook stops after ~40 minutes. Here is what it shows when it dies.', images: [{ index: 0, thumb: fakeShot('Colab', 20) }, { index: 1, thumb: fakeShot('GPU', 200) }] },
            { index: 1, role: 'assistant', time: null, text: 'That error is a CUDA out-of-memory crash, not an idle timeout. Your batch of 64 sequences at 4k tokens needs about 19 GB, and the T4 has 16 GB.\n\nThree fixes, in order of effort:\n1. Drop the batch to 32 and use gradient accumulation of 2.\n2. Turn on bf16 autocast.\n3. Checkpoint every 500 steps so a crash costs minutes, not hours.', images: [] },
            { index: 2, role: 'user', time: new Date(Date.now() - 2.5 * 3600000).toISOString(), text: 'Okay, did 1 and 3. How do I resume from the last checkpoint without redoing the warmup?', images: [] },
            { index: 3, role: 'assistant', time: null, text: 'Load the optimizer and scheduler state along with the weights, and set the start step from the checkpoint name. The warmup is part of the scheduler, so restoring its state skips it.', images: [] },
            { index: 4, role: 'user', time: new Date(Date.now() - 2 * 3600000).toISOString(), text: 'Here is the new loss curve after resuming.', images: [{ index: 0, thumb: fakeShot('Loss', 140) }] },
          ],
        };
      }
      case 'historyAdd': {
        await wait(200);
        const target = args.mode === 'new' ? this.make('Debug the Colab OOM', '') : this.drafts.get(this.current);
        for (const it of args.items) {
          if (it.kind === 'image') target.attachments.push(this.img('Image from ChatGPT', 'ChatGPT · Debug the Colab OOM', (it.turn * 70 + 20) % 360, 1600, 900));
          else if (it.turn % 2 === 0) target.text = (target.text ? target.text.trimEnd() + '\n\n' : '') + 'From an earlier chat: prompt ' + it.turn;
          else target.attachments.push({ id: id('a'), kind: 'text', text: 'That error is a CUDA out-of-memory crash, not an idle timeout.', source: 'ChatGPT reply · Debug the Colab OOM', include: true });
        }
        target.updated = now();
        this.current = target.id;
        return { draft: this.copy(target), added: args.items.length };
      }
      case 'historyImport':
        await wait(400);
        this.saved.claude.push({ id: 'c1', title: 'Write the launch tweet', sub: 'Claude', updated: now(), turns: 2, images: 0 });
        return { source: 'claude', chats: 1, images: 0 };
      case 'historyDelete':
        this.saved[args.source] = (this.saved[args.source] || []).filter(x => x.id !== args.sessionId);
        return true;
      case 'agentInfo':
        return {
          exe: 'C:\\Apps\\Chipmunk\\Chipmunk.exe',
          claude: 'claude mcp add --scope user chipmunk -- "C:\\Apps\\Chipmunk\\Chipmunk.exe" --mcp',
          codex: 'codex mcp add chipmunk -- "C:\\Apps\\Chipmunk\\Chipmunk.exe" --mcp',
          json: '{\n  "mcpServers": {\n    "chipmunk": {\n      "command": "C:\\\\Apps\\\\Chipmunk\\\\Chipmunk.exe",\n      "args": ["--mcp"]\n    }\n  }\n}',
        };
      case 'browserInfo': return { folder: 'C:\\Apps\\Chipmunk\\extension', exists: true, id: 'fcgflimapmefbojakginmcacgegeonii', connected: false };
      case 'storageInfo': return { bytes: 18_400_000, files: 42, drafts: this.drafts.size, linked: 1 };
      case 'copyToClipboard': return true;
      case 'openExtensionFolder': this.emit('toast', { text: '(mock) Would open the extension folder' }); return true;
      case 'suspendHotkeys': case 'hide': case 'flushed': return true;
      default: throw new Error('Mock host does not know ' + cmd);
    }
  }
}
