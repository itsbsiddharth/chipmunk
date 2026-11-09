import { ask, readSelection, chatSite } from './chipmunk.js';

const $ = (id) => document.getElementById(id);
const status = (text, kind) => { $('status').textContent = text || ''; $('status').className = 'status ' + (kind || ''); };

const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
$('url').textContent = tab && tab.url ? new URL(tab.url).hostname.replace(/^www\./, '') : '';

function showDraft(r) {
  $('draft').textContent = 'Adding to “' + (r.title || 'Untitled draft') + '”';
  $('count').hidden = !r.count;
  $('count').textContent = r.count;
}

const state = await ask({ type: 'status' });
if (state.ok) {
  showDraft(state);
} else {
  $('draft').textContent = 'Not connected';
  $('offline').hidden = !state.offline;
  if (!state.offline) status(state.error, 'err');
}

const site = tab && chatSite(tab.url);
if (site) {
  $('chat').hidden = false;
  $('chat-title').textContent = `Save this ${site.name} chat`;
}

const selection = tab ? await readSelection(tab) : '';
if (!selection || !selection.trim()) {
  $('sel').disabled = true;
  $('sel').querySelector('small').textContent = 'Highlight text on the page first';
}

for (const b of document.querySelectorAll('#actions button')) {
  b.addEventListener('click', async () => {
    status('Adding…');
    for (const x of document.querySelectorAll('#actions button')) x.disabled = true;
    const r = await chrome.runtime.sendMessage({ type: 'collect', kind: b.dataset.kind, tab });
    if (r && r.ok) {
      showDraft(r);
      status(r.message || (r.duplicate ? 'Already in your draft' : 'Added'), 'ok');
      setTimeout(() => window.close(), r.message ? 1800 : 900);
    } else {
      status((r && r.error) || 'Could not add that.', 'err');
      if (r && r.offline) $('offline').hidden = false;
      for (const x of document.querySelectorAll('#actions button')) x.disabled = false;
    }
  });
}

$('open').addEventListener('click', async () => {
  const r = await ask({ type: 'open' });
  if (r.ok) window.close();
  else status(r.error, 'err');
});
