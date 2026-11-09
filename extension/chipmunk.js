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
