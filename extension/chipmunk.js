// Shared by the background worker and the popup: everything that talks to the page
// or to the Chipmunk desktop app.
export const HOST = 'com.chipmunk.app';

export const browserName = (() => {
  const ua = navigator.userAgent;
  if (ua.includes('Edg/')) return 'Edge';
  if (navigator.brave || (navigator.userAgentData && navigator.userAgentData.brands.some(b => b.brand === 'Brave'))) return 'Brave';
  if (ua.includes('OPR/')) return 'Opera';
  if (ua.includes('Vivaldi')) return 'Vivaldi';
  return 'Chrome';
})();

/** Sends one request to the Chipmunk app. Never throws; failures come back as { ok: false }. */
export function ask(message) {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendNativeMessage(HOST, message, (reply) => {
        const err = chrome.runtime.lastError;
        if (err) resolve({ ok: false, error: friendly(err.message), offline: true });
        else resolve(reply || { ok: false, error: 'Chipmunk did not answer.' });
      });
    } catch (e) {
      resolve({ ok: false, error: friendly(e.message), offline: true });
    }
  });
}

function friendly(msg) {
  if (/not found|forbidden|Specified native messaging host/i.test(msg || '')) {
    return 'Chipmunk isn\'t installed or hasn\'t been opened yet. Open the Chipmunk app once, then try again.';
  }
  return msg || 'Could not reach Chipmunk.';
}

export function add(item) {
  return ask({ type: 'add', item: { ...item, browser: browserName } });
}

const restricted = (url) => !url || /^(chrome|edge|brave|about|view-source|devtools|chrome-extension):/i.test(url)
  || /chromewebstore\.google\.com|microsoftedge\.microsoft\.com\/addons/i.test(url);

async function run(tab, func) {
  if (restricted(tab.url)) return null;
  try {
    const [res] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func });
    return res ? res.result : null;
  } catch {
    return null;
  }
}

/** The selected text, keeping line breaks (the context-menu text flattens them). */
export function readSelection(tab) {
  return run(tab, () => {
    const s = getSelection();
    return s ? s.toString() : '';
  });
}

/** The readable text of the page: the article or main region if there is one. */
export function readPageText(tab) {
  return run(tab, () => {
    const root = document.querySelector('article, main, [role="main"]') || document.body;
    const text = (root.innerText || '').replace(/\n{3,}/g, '\n\n').trim();
    return text.length > 60000 ? text.slice(0, 60000) + '\n…' : text;
  });
}

function toDataUrl(buffer, type) {
  const bytes = new Uint8Array(buffer);
  let bin = '';
  for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
  return `data:${type || 'image/png'};base64,${btoa(bin)}`;
}

/** Fetches an image the user right-clicked; returns a data URL or null. */
export async function fetchImage(src, tab) {
  if (src.startsWith('data:image/')) return src;
  try {
    const r = await fetch(src, { credentials: 'include' });
    if (r.ok) {
      const type = (r.headers.get('content-type') || 'image/png').split(';')[0];
      if (type.startsWith('image/') && !type.includes('svg')) return toDataUrl(await r.arrayBuffer(), type);
    }
  } catch { /* fall through to drawing it in the page */ }
  // Same-origin or blob images can still be read from inside the page.
  if (restricted(tab.url)) return null;
  try {
    const [res] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      args: [src],
      func: (url) => {
        const img = [...document.images].find(i => i.currentSrc === url || i.src === url);
        if (!img || !img.naturalWidth) return null;
        const c = document.createElement('canvas');
        c.width = img.naturalWidth;
        c.height = img.naturalHeight;
        c.getContext('2d').drawImage(img, 0, 0);
        try { return c.toDataURL('image/png'); } catch { return null; }
      },
    });
    return res ? res.result : null;
  } catch {
    return null;
  }
}

export async function screenshot(tab) {
  return chrome.tabs.captureVisibleTab(tab.windowId, { format: 'png' });
}

export function pageItem(tab) {
  return { kind: 'link', url: tab.url, title: tab.title };
}

// ---------------- whole chats from AI sites ----------------

export const CHAT_SITES = [
  { source: 'chatgpt', name: 'ChatGPT', match: /^https:\/\/(chatgpt\.com|chat\.openai\.com)\//i },
  { source: 'claude', name: 'Claude', match: /^https:\/\/claude\.ai\//i },
  { source: 'gemini', name: 'Gemini', match: /^https:\/\/gemini\.google\.com\//i },
];
export const CHAT_URL_PATTERNS = ['https://chatgpt.com/*', 'https://chat.openai.com/*', 'https://claude.ai/*', 'https://gemini.google.com/*'];

export function chatSite(url) {
  return CHAT_SITES.find(s => s.match.test(url || '')) || null;
}

/**
 * Runs inside the chat page: finds every prompt and reply in order, with the images attached
 * to them, and reads each image while the page's own login is available. These sites change
 * their markup often, so each one has a fallback that only relies on the message roles.
 */
async function scrapeChat() {
  const host = location.hostname.replace(/^www\./, '');
  const source = /^(chatgpt\.com|chat\.openai\.com)$/.test(host) ? 'chatgpt'
    : host === 'claude.ai' ? 'claude' : host === 'gemini.google.com' ? 'gemini' : null;
  if (!source) return { source: null };
  const clean = (t) => (t || '').replace(/\u00a0/g, ' ').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  const inside = (el, list) => list.some(o => o !== el && o.contains(el));
  const bigImages = (root) => [...root.querySelectorAll('img')].filter(i => {
    const w = i.naturalWidth || i.width, h = i.naturalHeight || i.height;
    return w >= 64 && h >= 64 && !i.closest('button[aria-label*="avatar" i], [data-testid*="avatar" i]');
  }).map(i => i.currentSrc || i.src).filter(Boolean);
  const turns = [];
  const push = (role, text, images) => {
    text = clean(text);
    images = [...new Set(images || [])];
    if (text || images.length) turns.push({ role, text, images });
  };

  if (source === 'chatgpt') {
    const blocks = [...document.querySelectorAll('[data-testid^="conversation-turn"]')];
    if (blocks.length) {
      for (const b of blocks) {
        const m = b.querySelector('[data-message-author-role]');
        if (!m) continue;
        const role = m.getAttribute('data-message-author-role') === 'user' ? 'user' : 'assistant';
        const body = role === 'user' ? (b.querySelector('.whitespace-pre-wrap') || m) : (b.querySelector('.markdown') || m);
        push(role, body.innerText, bigImages(b));
      }
    } else {
      for (const m of document.querySelectorAll('[data-message-author-role]')) {
        const role = m.getAttribute('data-message-author-role') === 'user' ? 'user' : 'assistant';
        push(role, (m.querySelector('.markdown, .whitespace-pre-wrap') || m).innerText, bigImages(m));
      }
    }
  } else if (source === 'claude') {
    const blocks = [...document.querySelectorAll('[data-test-render-count]')];
    const parse = (b) => {
      const u = b.querySelector('[data-testid="user-message"]');
      if (u) return push('user', u.innerText, bigImages(b));
      const a = b.querySelector('.font-claude-response, .font-claude-message, [data-is-streaming]');
      if (a) push('assistant', a.innerText, bigImages(a));
    };
    if (blocks.length) blocks.forEach(parse);
    else {
      const all = [...document.querySelectorAll('[data-testid="user-message"], .font-claude-response, .font-claude-message')];
      for (const el of all.filter(el => !inside(el, all))) {
        push(el.matches('[data-testid="user-message"]') ? 'user' : 'assistant', el.innerText, bigImages(el));
      }
    }
  } else if (source === 'gemini') {
    for (const el of document.querySelectorAll('user-query, model-response')) {
      if (el.tagName.toLowerCase() === 'user-query') {
        const q = el.querySelector('.query-text, [class*="query-text"]') || el;
        push('user', q.innerText.replace(/^You said\s*/i, ''), bigImages(el));
      } else {
        const r = el.querySelector('message-content, .markdown') || el;
        push('assistant', r.innerText, bigImages(el));
      }
    }
  }

  // Read the images here, where the site's own session can see them.
  const toDataUrl = (blob) => new Promise((resolve) => {
    const r = new FileReader();
    r.onload = () => resolve(r.result);
    r.onerror = () => resolve(null);
    r.readAsDataURL(blob);
  });
  const read = async (src) => {
    if (src.startsWith('data:image/')) return src;
    try {
      // Same-site files need the login cookie; image CDNs only allow requests without it.
      const res = await fetch(src, { credentials: 'include' }).catch(() => fetch(src, { credentials: 'omit' }));
      if (!res.ok) return null;
      const blob = await res.blob();
      if (!blob.type.startsWith('image/') || blob.type.includes('svg') || blob.size > 12 * 1024 * 1024) return null;
      return await toDataUrl(blob);
    } catch { return null; }
  };
  let budget = 40;
  for (const t of turns) {
    const out = [];
    for (const src of t.images) {
      if (budget-- <= 0) break;
      out.push((await read(src)) || src);
    }
    t.images = out;
  }

  const title = document.title.replace(/\s*[-|–]\s*(ChatGPT|Claude|Gemini)\s*$/i, '').trim();
  return { source, url: location.href, title: /^(ChatGPT|Claude|Gemini)$/i.test(title) ? '' : title, turns };
}

/** Reads the open chat on chatgpt.com, claude.ai or gemini.google.com. */
export async function readChat(tab) {
  const notChat = { error: 'Open a chat on ChatGPT, Claude or Gemini first.' };
  let site = chatSite(tab && tab.url);
  if (!tab || (tab.url && !site)) return notChat;
  // The page says which site it is, so this also works when the browser hides the tab's URL.
  let chat;
  try {
    const [res] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: scrapeChat });
    chat = res && res.result;
  } catch {
    return notChat;
  }
  if (!chat || !chat.source) return notChat;
  site = CHAT_SITES.find(s => s.source === chat.source);
  if (!chat.turns.length) return { error: `No messages found. Open a ${site.name} chat (not the start page) and try again.` };
  // Images the page couldn't read (other origins) are fetched here with the extension's access.
  for (const t of chat.turns) {
    const imgs = [];
    for (const src of t.images) imgs.push(src.startsWith('data:') ? src : await fetchImage(src, tab));
    t.images = imgs.filter(Boolean);
  }
  return { source: site.source, name: site.name, title: chat.title || tab.title, url: chat.url, turns: chat.turns };
}

export function saveChat(chat) {
  return ask({ type: 'saveChat', session: { source: chat.source, title: chat.title, url: chat.url, turns: chat.turns } });
}
