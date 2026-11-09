import { add, readSelection, readPageText, fetchImage, screenshot, pageItem } from './chipmunk.js';

const MENUS = [
  { id: 'selection', title: 'Add selection to Chipmunk', contexts: ['selection'] },
  { id: 'image', title: 'Add image to Chipmunk', contexts: ['image'] },
  { id: 'link', title: 'Add link to Chipmunk', contexts: ['link'] },
  { id: 'page', title: 'Add this page to Chipmunk', contexts: ['page'] },
  { id: 'page-text', title: 'Add this page\'s text to Chipmunk', contexts: ['page'] },
  { id: 'shot', title: 'Add a screenshot of this tab to Chipmunk', contexts: ['page', 'image', 'selection', 'link'] },
];

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    for (const m of MENUS) chrome.contextMenus.create(m);
  });
});

/** Shows the result on the toolbar icon: a check, or a "!" with the reason in the tooltip. */
async function flash(tab, reply) {
  const ok = reply && reply.ok;
  const text = ok ? '✓' : '!';
  try {
    await chrome.action.setBadgeBackgroundColor({ color: ok ? '#2f7a4f' : '#b3372f', tabId: tab.id });
    await chrome.action.setBadgeText({ text, tabId: tab.id });
    await chrome.action.setTitle({ title: ok ? `Added to “${reply.title || 'your draft'}” (${reply.count})` : (reply && reply.error) || 'Chipmunk', tabId: tab.id });
  } catch { /* the tab may be gone */ }
  setTimeout(() => {
    chrome.action.setBadgeText({ text: '', tabId: tab.id }).catch(() => {});
    chrome.action.setTitle({ title: 'Chipmunk', tabId: tab.id }).catch(() => {});
  }, ok ? 2200 : 6000);
  return reply;
}

export async function collect(kind, tab, info = {}) {
  switch (kind) {
    case 'selection': {
      const text = (await readSelection(tab)) || info.selectionText || '';
      if (!text.trim()) return flash(tab, await add(pageItem(tab)));
      return flash(tab, await add({ kind: 'text', text, url: tab.url, title: tab.title }));
    }
    case 'image': {
      const data = await fetchImage(info.srcUrl, tab);
      if (!data) return flash(tab, await add({ kind: 'link', url: info.srcUrl, title: 'Image on ' + tab.title }));
      return flash(tab, await add({ kind: 'image', data, url: tab.url, title: tab.title }));
    }
    case 'link':
      return flash(tab, await add({ kind: 'link', url: info.linkUrl, title: info.selectionText || info.linkUrl }));
    case 'page':
      return flash(tab, await add(pageItem(tab)));
    case 'page-text': {
      const text = await readPageText(tab);
      // Some pages (browser pages, the extension store) can't be read; keep the link instead.
      if (!text) return flash(tab, await add(pageItem(tab)));
      return flash(tab, await add({ kind: 'page', text, url: tab.url, title: tab.title }));
    }
    case 'shot': {
      const data = await screenshot(tab);
      return flash(tab, await add({ kind: 'image', data, screenshot: true, url: tab.url, title: tab.title, name: 'Screenshot of ' + (tab.title || 'page') }));
    }
    default:
      return { ok: false, error: 'Unknown action' };
  }
}

// Lets automated tests drive the same code path as the menus.
self.chipmunkCollect = collect;

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (tab) collect(info.menuItemId, tab, info);
});

chrome.commands.onCommand.addListener(async (command, tab) => {
  tab = tab || (await chrome.tabs.query({ active: true, currentWindow: true }))[0];
  if (!tab) return;
  if (command === 'add-selection') collect('selection', tab);
  else if (command === 'screenshot-tab') collect('shot', tab);
});

// The popup asks the worker to do the work so it survives the popup closing.
chrome.runtime.onMessage.addListener((msg, sender, respond) => {
  if (msg && msg.type === 'collect') {
    collect(msg.kind, msg.tab).then(respond);
    return true;
  }
  return false;
});
