import { Host } from './host.js';
import { icon } from './icons.js';

// ---------- tiny DOM helpers ----------
const $ = (sel, root = document) => root.querySelector(sel);

function h(tag, props, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'text') el.textContent = v;
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
    else if (v === true) el.setAttribute(k, '');
    else el.setAttribute(k, v);
  }
  for (const c of children.flat(Infinity)) {
    if (c == null || c === false) continue;
    el.append(c.nodeType ? c : document.createTextNode(String(c)));
  }
  return el;
}

const plural = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`;

function ago(iso) {
  if (!iso) return '';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 45) return 'just now';
  if (s < 3600) return `${Math.round(s / 60)} min ago`;
  if (s < 86400) return `${Math.round(s / 3600)} h ago`;
  const d = new Date(iso);
  const days = Math.round(s / 86400);
  if (days < 7) return `${days} d ago`;
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: d.getFullYear() === new Date().getFullYear() ? undefined : 'numeric' });
}

function fmtBytes(n) {
  if (!n && n !== 0) return '';
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${Math.round(n / 1024)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function fmtTokens(n) {
  if (n < 1000) return String(n);
  return `${(n / 1000).toFixed(n < 10000 ? 1 : 0)}k`;
}

function domainOf(url) {
  try { return new URL(url).hostname.replace(/^www\./, ''); } catch { return url; }
}

const newId = (p) => p + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);

// Rough token math: ~4 characters per token for text; images are scaled to a
// 1568px long edge and cost about (w*h)/750 tokens, capped near 1600.
function imageTokens(w, h) {
  if (!w || !h) return 1500;
  const s = Math.min(1, 1568 / Math.max(w, h));
  return Math.min(1600, Math.round((w * s) * (h * s) / 750));
}

function estimate(d) {
  let chars = (d.text || '').length;
  const out = { images: 0, files: 0, quotes: 0, links: 0, tokens: 0 };
  let imgTokens = 0;
  for (const a of d.attachments || []) {
    if (!a.include) continue;
    if (a.kind === 'image') { out.images++; imgTokens += imageTokens(a.w, a.h); }
    else if (a.kind === 'file') out.files++;
    else if (a.kind === 'text') { out.quotes++; chars += (a.text || '').length + 40; }
    else if (a.kind === 'link') { out.links++; chars += (a.url || '').length + (a.name || '').length + 8; }
  }
  out.tokens = Math.ceil(chars / 4) + imgTokens;
  return out;
}

// ---------- state ----------
const S = {
  settings: {},
  drafts: [],
  d: null,
  target: null,
  collect: false,
  save: { state: 'saved', at: null, error: null },
  overlay: null,
  dragAttId: null,
  seenAtt: new Set(),
};

const el = {
  title: $('#title'),
  editor: $('#editor'),
  doc: $('#doc'),
  dictation: $('#dictation'),
  saveState: $('#save-state'),
  collectToggle: $('#collect-toggle'),
  collectLabel: $('#collect'),
  checklist: $('#checklist-section'),
  atts: $('#attachments-section'),
  estimate: $('#estimate'),
  send: $('#btn-send'),
  sendMore: $('#btn-send-more'),
  copyText: $('#btn-copy-text'),
  copyFiles: $('#btn-copy-files'),
  overlay: $('#overlay-root'),
  toasts: $('#toasts'),
  drop: $('#drop'),
  dropCard: $('#drop-card'),
  wordCount: $('#word-count'),
  scroll: $('#scroll'),
};

const hk = (name) => (S.settings.hotkeys || {})[name] || '';

// ---------- saving ----------
let textTimer = null;
let titleTimer = null;

function setSave(state, error) {
  S.save.state = state;
  S.save.error = error || null;
  if (state === 'saved') S.save.at = new Date().toISOString();
  renderSaveState();
}

async function saveText() {
  clearTimeout(textTimer);
  textTimer = null;
  const id = S.d.id;
  const text = S.d.text;
  try {
    const r = await Host.call('saveText', { id, text });
    if (S.d && S.d.id === id) S.d.updated = r.updated;
    setSave('saved');
  } catch (e) {
    setSave('error', e.message);
  }
}

async function saveTitle() {
  clearTimeout(titleTimer);
  titleTimer = null;
  const id = S.d.id;
  try {
    await Host.call('updateDraft', { id, patch: { title: S.d.title } });
    setSave('saved');
  } catch (e) {
    setSave('error', e.message);
  }
}

async function flushSaves() {
  if (textTimer) await saveText();
  if (titleTimer) await saveTitle();
}

// Apply a draft object coming back from the host. Local text wins while the
// user is typing, so a reply can never undo keystrokes.
function applyDraft(d, opts = {}) {
  if (!d) return;
  if (!S.d || S.d.id !== d.id) return;
  const keepText = !opts.replaceText;
  const text = S.d.text;
  const title = S.d.title;
  S.d = d;
  if (keepText) { S.d.text = text; S.d.title = title; }
  else {
    el.editor.value = d.text || '';
    el.title.value = d.title || '';
    autosize();
  }
  renderDraftParts();
}

async function openDraft(id, draft) {
  await flushSaves();
  const d = draft || await Host.call('getDraft', { id });
  S.d = d;
  S.seenAtt = new Set((d.attachments || []).map(a => a.id));
  el.editor.value = d.text || '';
  el.title.value = d.title || '';
  autosize();
  el.scroll.scrollTop = 0;
  renderAll();
}

// ---------- rendering ----------
function renderAll() {
  renderSaveState();
  renderCollect();
  renderDictation();
  renderDraftParts();
}

function renderDraftParts() {
  renderChecklist();
  renderAttachments();
  renderFooter();
  renderWordCount();
}

function renderSaveState() {
  const s = S.save;
  el.saveState.className = 'save-state ' + s.state;
  const txt = el.saveState.querySelector('.txt');
  if (s.state === 'saving') txt.textContent = 'Saving…';
  else if (s.state === 'error') txt.textContent = 'Not saved: ' + (s.error || 'error');
  else txt.textContent = 'Saved' + (S.d && S.d.updated ? ' · ' + ago(S.d.updated) : '');
  el.saveState.title = S.d ? `Saved on disk in ${S.d.folder || 'your drafts folder'}` : '';
}

function renderCollect() {
  el.collectToggle.checked = !!S.collect;
  el.collectLabel.classList.toggle('on', !!S.collect);
}

// Chipmunk has no speech engine of its own: the dictation app you already use (Wispr Flow,
// Superwhisper, Aqua, Windows voice typing) types straight into the editor. We just say how.
function renderDictation() {
  const d = S.settings.dictation || {};
  el.dictation.replaceChildren(...[icon('mic'),
    h('span', {}, d.app ? `${d.app}: ${d.hint}` : 'Dictate'),
    d.app ? null : h('kbd', {}, d.hint || 'Win+H')].filter(Boolean));
  el.dictation.title = d.app
    ? `${d.app} is running. Click here, then ${d.hint} and talk: your words land in the draft.`
    : 'Click here, then press Win+H to talk. Wispr Flow, Superwhisper and Aqua work here too.';
}

function renderWordCount() {
  const t = S.d.text || '';
  const words = (t.match(/\S+/g) || []).length;
  const sent = S.d.sends && S.d.sends.length ? `Sent ${ago(S.d.sends[S.d.sends.length - 1].at)}` : '';
  el.wordCount.textContent = [words ? plural(words, 'word') : '', sent].filter(Boolean).join(' · ');
}

function autosize() {
  const top = el.scroll.scrollTop;
  el.editor.style.height = 'auto';
  el.editor.style.height = Math.max(200, el.editor.scrollHeight) + 'px';
  el.scroll.scrollTop = top;
}

function focusEditorEnd() {
  el.editor.focus();
  const v = el.editor.value;
  const end = v.length;
  // so dictated words don't run into the last one
  if (end > 0 && !/\s$/.test(v)) {
    el.editor.setRangeText(' ', end, end, 'end');
    el.editor.dispatchEvent(new Event('input'));
  } else {
    el.editor.setSelectionRange(end, end);
  }
  el.editor.scrollIntoView({ block: 'nearest' });
}

// ----- reminders -----
const checklist = () => S.d.checklist || [];

async function saveChecklist(items) {
  S.d.checklist = items;
  renderChecklist();
  renderFooter();
  try {
    applyDraft(await Host.call('updateDraft', { id: S.d.id, patch: { checklist: items } }));
  } catch (e) { toast(e.message, 'error'); }
}

function attById(id) { return (S.d.attachments || []).find(a => a.id === id); }

function renderChecklist() {
  const sec = el.checklist;
  sec.replaceChildren();
  const items = checklist();
  const done = items.filter(i => i.done).length;
  sec.append(h('div', { class: 'section-head' },
    h('div', { class: 'section-title' }, 'To attach',
      items.length ? h('span', { class: 'count-pill' }, `${done}/${items.length}`) : null)));

  const list = h('div', { class: 'checklist' });
  for (const it of items) list.append(checkRow(it));

  const input = h('input', {
    placeholder: items.length ? 'Add another reminder' : 'Remind yourself what to add, like "screenshot of the error"',
    'aria-label': 'Add a reminder',
    onkeydown: (e) => {
      if (e.key === 'Enter' && input.value.trim()) {
        const text = input.value.trim();
        input.value = '';
        saveChecklist([...checklist(), { id: newId('c'), text, done: false, attId: null }]).then(() => {
          const again = el.checklist.querySelector('.add-check input');
          if (again) again.focus();
        });
      }
    },
  });
  list.append(h('div', { class: 'add-check' }, icon('plus'), input));
  sec.append(list);
}

function checkRow(it) {
  const linked = it.attId ? attById(it.attId) : null;
  const armed = S.d.armedItem === it.id && !it.done;
  const row = h('div', {
    class: 'check-item' + (it.done ? ' done' : '') + (armed ? ' armed' : ''),
    ondragover: (e) => { if (canDropOnRow(e)) { e.preventDefault(); row.classList.add('drop-hover'); } },
    ondragleave: () => row.classList.remove('drop-hover'),
    ondrop: (e) => { row.classList.remove('drop-hover'); dropOnRow(e, it); },
  });
  const box = h('button', {
    class: 'check-box', title: it.done ? 'Mark as still needed' : 'Mark as added', 'aria-label': 'Toggle done',
    onclick: () => saveChecklist(checklist().map(x => x.id === it.id ? { ...x, done: !x.done, attId: x.done ? null : x.attId } : x)),
  }, it.done ? icon('check') : null);
  const text = h('span', { class: 'check-text', title: it.text, ondblclick: () => editRow(row, it) }, it.text);
  row.append(box, text);
  if (linked) {
    row.append(h('span', { class: 'check-link', title: linked.name },
      linked.kind === 'image' && linked.url ? h('img', { src: linked.url, alt: '' }) : icon(kindIcon(linked.kind)),
      h('span', {}, linked.name)));
  } else if (armed) {
    row.append(h('span', { class: 'armed-hint' }, icon('target'), 'Next capture lands here'));
  }
  const actions = h('div', { class: 'row-actions' });
  if (!it.done) {
    actions.append(h('button', {
      class: 'mini-btn', title: armed ? 'Stop waiting for a capture' : 'Fill this with my next capture',
      'aria-label': 'Fill with next capture',
      onclick: () => armItem(armed ? null : it.id),
    }, icon('target')));
  }
  actions.append(h('button', {
    class: 'mini-btn', title: 'Remove reminder', 'aria-label': 'Remove reminder',
    onclick: () => saveChecklist(checklist().filter(x => x.id !== it.id)),
  }, icon('x')));
  row.append(actions);
  return row;
}

function editRow(row, it) {
  const input = h('input', { class: 'check-text', value: it.text, style: { border: 'none', outline: 'none', background: 'transparent' } });
  row.querySelector('.check-text').replaceWith(input);
  input.focus();
  input.select();
  const commit = () => {
    const v = input.value.trim();
    if (v && v !== it.text) saveChecklist(checklist().map(x => x.id === it.id ? { ...x, text: v } : x));
    else renderChecklist();
  };
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter') commit(); if (e.key === 'Escape') renderChecklist(); });
  input.addEventListener('blur', commit);
}

async function armItem(itemId) {
  try {
    applyDraft(await Host.call('armItem', { id: S.d.id, itemId }));
    if (itemId) toast(`Waiting for your next capture. Snip with ${hk('snip')}, grab with ${hk('grab')}, or drop a file.`);
  } catch (e) { toast(e.message, 'error'); }
}

function canDropOnRow(e) {
  const types = [...e.dataTransfer.types];
  return types.includes('application/x-chipmunk-att') || types.includes('Files');
}

async function dropOnRow(e, it) {
  const types = [...e.dataTransfer.types];
  if (types.includes('application/x-chipmunk-att')) {
    e.preventDefault();
    e.stopPropagation();
    const attId = e.dataTransfer.getData('application/x-chipmunk-att');
    saveChecklist(checklist().map(x => x.id === it.id ? { ...x, done: true, attId } : x));
  } else if (types.includes('Files')) {
    e.preventDefault();
    e.stopPropagation();
    hideDrop();
    await addDroppedFiles([...e.dataTransfer.files], it.id);
  }
}

// ----- collected -----
const kindIcon = (k) => ({ image: 'image', file: 'file', text: 'quote', link: 'link' }[k] || 'file');

function chipBtn(iconName, label, onclick, title) {
  return h('button', { class: 'chip-btn', onclick, title: title || label }, icon(iconName), label);
}

function renderAttachments() {
  const sec = el.atts;
  sec.replaceChildren();
  const atts = S.d.attachments || [];
  sec.append(h('div', { class: 'section-head' },
    h('div', { class: 'section-title' }, 'Collected', atts.length ? h('span', { class: 'count-pill' }, atts.length) : null),
    chipBtn('scissors', 'Snip', () => snip(), `Snip part of your screen into this draft (${hk('snip')})`),
    chipBtn('paperclip', 'Files', () => pickFiles(), 'Choose files to add'),
    chipBtn('clipboard', 'Paste', () => addFromClipboard(), 'Add whatever you copied (Ctrl+Shift+V)'),
    chipBtn('clock', 'Past chats', () => openHistory(), 'Reuse prompts, replies and images from earlier chats')));

  if (!atts.length) {
    sec.append(h('div', { class: 'empty' },
      h('img', { src: 'assets/chip.png', alt: '' }),
      h('div', {},
        h('div', { class: 'e-title' }, 'Nothing collected yet'),
        h('div', { class: 'e-body' }, 'Snip, grab or drop screenshots, files, quotes and links, or reuse pieces of past chats. Chip carries it all to your AI.'),
        h('div', { class: 'keys' },
          h('kbd', {}, hk('snip')), h('span', {}, 'Snip part of the screen'),
          h('kbd', {}, hk('grab')), h('span', {}, 'Grab what you highlighted'),
          h('kbd', {}, hk('paste')), h('span', {}, 'Send it all to the app you\'re in')))));
    return;
  }

  const grid = h('div', { class: 'att-grid' });
  atts.forEach((a) => {
    const tile = attTile(a);
    if (!S.seenAtt.has(a.id)) { tile.classList.add('fresh'); S.seenAtt.add(a.id); }
    grid.append(tile);
  });
  sec.append(grid);
}

function linkedItemFor(attId) {
  return checklist().find(i => i.attId === attId);
}

function attTile(a) {
  const linked = linkedItemFor(a.id);
  const tile = h('div', {
    class: 'att' + (a.include ? '' : ' excluded') + (a.kind === 'text' ? ' wide' : '') + (a.kind === 'image' && a.url ? ' has-thumb' : ''),
    draggable: 'true',
    tabindex: '0',
    title: a.kind === 'image' ? 'Click to preview · drag to reorder' : 'Drag to reorder',
    ondragstart: (e) => {
      S.dragAttId = a.id;
      e.dataTransfer.setData('application/x-chipmunk-att', a.id);
      e.dataTransfer.effectAllowed = 'move';
      tile.classList.add('dragging');
    },
    ondragend: () => { S.dragAttId = null; tile.classList.remove('dragging'); clearDropMarks(); },
    ondragover: (e) => {
      if (!S.dragAttId || S.dragAttId === a.id) return;
      e.preventDefault();
      clearDropMarks();
      tile.classList.add('drop-before');
    },
    ondrop: (e) => {
      if (!S.dragAttId) return;
      e.preventDefault();
      e.stopPropagation();
      reorder(S.dragAttId, a.id);
    },
    onclick: (e) => { if (!e.target.closest('.att-tools')) openAttachment(a); },
    onkeydown: (e) => {
      if (e.key === 'Enter') openAttachment(a);
      if (e.key === 'Delete') removeAttachment(a);
    },
  });

  if (a.kind === 'image' && a.url) {
    tile.append(h('div', { class: 'thumb' }, h('img', { src: a.url, alt: a.name, loading: 'lazy', draggable: 'false' })));
    tile.append(h('div', { class: 'att-body' },
      h('div', { class: 'att-name' }, a.name),
      h('div', { class: 'att-sub' }, [a.source, a.w ? `${a.w}×${a.h}` : null].filter(Boolean).join(' · '))));
  } else if (a.kind === 'file' || a.kind === 'image') {
    tile.append(h('div', { class: 'att-body' },
      h('div', { class: 'kind-badge' }, icon(a.kind === 'image' ? 'image' : 'file'), h('span', {}, (a.ext || 'file').toUpperCase())),
      h('div', { class: 'att-name wrap', style: { marginTop: '6px' }, title: a.name }, a.name.replace(/([-_.])/g, '$1​')),
      h('div', { class: 'att-sub' + (a.missing ? ' warn-tag' : '') },
        a.missing ? 'File was moved or deleted' : [fmtBytes(a.bytes), a.linked ? 'linked, not copied' : a.source].filter(Boolean).join(' · '))));
  } else if (a.kind === 'text') {
    tile.append(h('div', { class: 'att-body' },
      h('div', { class: 'kind-badge' }, icon('quote'), h('span', {}, a.source || 'Quote')),
      h('div', { class: 'att-quote', style: { marginTop: '5px' } }, a.text)));
  } else if (a.kind === 'link') {
    tile.append(h('div', { class: 'att-body' },
      h('div', { class: 'kind-badge' }, icon('link'), h('span', {}, domainOf(a.url))),
      h('div', { class: 'att-name wrap', style: { marginTop: '6px' } }, a.name || a.url),
      h('div', { class: 'att-sub' }, a.url)));
  }

  if (linked) tile.append(h('div', { class: 'linked-tag', title: `Covers your reminder: "${linked.text}"` }, icon('check')));

  tile.append(h('div', { class: 'att-tools' },
    h('button', {
      title: a.include ? 'Leave this out when sending' : 'Include this when sending',
      'aria-label': a.include ? 'Leave out' : 'Include',
      onclick: () => toggleInclude(a),
    }, icon(a.include ? 'eye' : 'eyeOff')),
    h('button', { title: 'Remove', 'aria-label': 'Remove', onclick: () => removeAttachment(a) }, icon('x'))));
  return tile;
}

function clearDropMarks() {
  document.querySelectorAll('.att.drop-before').forEach(n => n.classList.remove('drop-before'));
}

async function reorder(movingId, beforeId) {
  clearDropMarks();
  const ids = (S.d.attachments || []).map(a => a.id).filter(id => id !== movingId);
  ids.splice(ids.indexOf(beforeId), 0, movingId);
  S.d.attachments = ids.map(id => attById(id));
  renderAttachments();
  try {
    applyDraft(await Host.call('updateDraft', { id: S.d.id, patch: { order: ids } }));
  } catch (e) { toast(e.message, 'error'); }
}

async function toggleInclude(a) {
  try {
    applyDraft(await Host.call('updateAttachment', { id: S.d.id, attId: a.id, patch: { include: !a.include } }));
  } catch (e) { toast(e.message, 'error'); }
}

async function removeAttachment(a) {
  try {
    applyDraft(await Host.call('removeAttachment', { id: S.d.id, attId: a.id }));
    toast(`Removed ${a.kind === 'image' ? 'screenshot' : a.kind === 'text' ? 'quote' : a.kind}`, null, {
      label: 'Undo',
      run: async () => applyDraft(await Host.call('restoreAttachment', { id: S.d.id, attId: a.id })),
    });
  } catch (e) { toast(e.message, 'error'); }
}

function openAttachment(a) {
  if (a.kind === 'image' && a.url) openLightbox(a.id);
  else if (a.kind === 'text') openQuote(a);
  else Host.call('openAttachment', { id: S.d.id, attId: a.id }).catch(e => toast(e.message, 'error'));
}

async function snip() {
  try { await Host.call('snip', { id: S.d.id }); } catch (e) { toast(e.message, 'error'); }
}

async function pickFiles() {
  try {
    const d = await Host.call('pickFiles', { id: S.d.id });
    if (d) applyDraft(d);
  } catch (e) { toast(e.message, 'error'); }
}

async function addFromClipboard() {
  try {
    const r = await Host.call('addClipboard', { id: S.d.id });
    applyDraft(r.draft);
    toast(r.message);
  } catch (e) { toast(e.message, 'error'); }
}

async function addTextOrLink(text, source, linkItem) {
  const t = text.trim();
  try {
    if (/^https?:\/\/\S+$/i.test(t)) applyDraft(await Host.call('addLink', { id: S.d.id, url: t, linkItem }));
    else applyDraft(await Host.call('addText', { id: S.d.id, text: t, source, linkItem }));
  } catch (e) { toast(e.message, 'error'); }
}

function readAsDataUrl(file) {
  return new Promise((resolve, reject) => {
    const r = new FileReader();
    r.onload = () => resolve(r.result);
    r.onerror = () => reject(r.error);
    r.readAsDataURL(file);
  });
}

async function addDroppedFiles(files, linkItem) {
  if (!files.length) return;
  try {
    const r = await Host.call('addFiles', { id: S.d.id, linkItem, names: files.map(f => f.name) }, files);
    applyDraft(r.draft);
    // Files without a disk path (e.g. an image dragged out of a web page) come back
    // as "missing"; send their bytes instead.
    for (const i of r.missing || []) {
      const f = files[i];
      const data = await readAsDataUrl(f);
      applyDraft(await Host.call('addData', { id: S.d.id, name: f.name, data, linkItem }));
    }
  } catch (e) { toast(e.message, 'error'); }
}

// ----- footer -----
function renderFooter() {
  const e = estimate(S.d);
  const missing = checklist().filter(i => !i.done).length;
  const parts = [`≈ ${fmtTokens(e.tokens)} tokens`];
  if (e.images) parts.push(plural(e.images, 'image'));
  if (e.files) parts.push(plural(e.files, 'file'));
  if (e.quotes) parts.push(plural(e.quotes, 'quote'));
  if (e.links) parts.push(plural(e.links, 'link'));

  const left = h('span', {}, parts.join(' · '));
  const right = missing ? h('span', { class: 'warn' }, `${missing} still to attach`) : h('span', {}, `${hk('paste')} sends from any app`);
  el.estimate.replaceChildren(left, right);

  const t = S.target;
  const hasContent = (S.d.text || '').trim() || e.images || e.files || e.quotes || e.links;
  el.send.replaceChildren(icon(t && t.terminal ? 'terminal' : 'send'),
    h('span', { class: 'label' },
      h('span', { class: 'l1' }, t ? `Send to ${t.label || t.app}` : 'Send to…'),
      h('span', { class: 'l2' }, t ? targetSub(t, 'Ctrl+Enter') : 'Open ChatGPT, Claude or any app')));
  el.send.disabled = !hasContent;
  el.sendMore.disabled = !hasContent;
  el.send.title = t ? `Switch to "${t.title}" and paste everything (Ctrl+Enter)` : 'Pick where to send';
  el.copyFiles.disabled = !(e.images + e.files);
  el.copyText.disabled = !((S.d.text || '').trim() || e.quotes || e.links);
}

// ---------- sending ----------
async function send(how, hwnd) {
  await flushSaves();
  const missing = checklist().filter(i => !i.done);
  if (how === 'paste' && missing.length) {
    const ok = await confirmModal({
      title: `${plural(missing.length, 'thing')} still to attach`,
      body: h('ul', {}, missing.map(m => h('li', {}, m.text))),
      ok: 'Send anyway',
      cancel: 'Go back',
    });
    if (!ok) return;
  }
  try {
    const r = await Host.call('send', { id: S.d.id, how, hwnd: hwnd || null });
    if (r && r.draft) applyDraft(r.draft);
    if (r && r.message && how !== 'paste') toast(r.message);
  } catch (e) { toast(e.message, 'error'); }
}

// The second line under a target: its window title, else the app it runs in.
function targetSub(t, fallback) {
  const name = t.label || t.app;
  if (t.title && t.title !== name) return t.title;
  return t.app && t.app !== name ? t.app : fallback;
}

// Every open window you could send to, AI apps first. One click switches there and pastes.
async function openSendMenu() {
  if (S.overlay && S.overlay.type === 'send-menu') return closeOverlay();
  let targets = [];
  try { targets = await Host.call('targets'); } catch (e) { toast(e.message, 'error'); return; }
  const r = el.send.parentElement.getBoundingClientRect();
  const row = (t) => h('button', { class: 'target', onclick: () => { closeOverlay(); send('paste', t.hwnd); } },
    h('span', { class: 'target-icon' + (t.ai ? ' ai' : '') }, icon(t.terminal ? 'terminal' : t.ai ? 'send' : 'window')),
    h('span', { class: 'target-text' },
      h('span', { class: 't1' }, t.label || t.app),
      h('span', { class: 't2' }, targetSub(t, t.terminal ? 'Terminal' : 'Desktop app'))),
    S.target && S.target.hwnd === t.hwnd ? h('span', { class: 'target-default' }, 'Default') : null);
  const ai = targets.filter(x => x.ai), other = targets.filter(x => !x.ai);
  const menu = h('div', { class: 'menu send-menu', role: 'menu', style: { bottom: (window.innerHeight - r.top + 6) + 'px', left: r.left + 'px', right: (window.innerWidth - r.right) + 'px' } },
    ai.length ? h('div', { class: 'menu-label' }, 'AI apps') : null,
    ai.map(row),
    other.length ? h('div', { class: 'menu-label' }, ai.length ? 'Other windows' : 'Recent windows') : null,
    other.map(row),
    targets.length ? null : h('div', { class: 'menu-empty' }, 'Open ChatGPT, Claude, Codex, Antigravity or any app and it shows up here.'),
    h('div', { class: 'menu-hint' }, 'Or press ', h('kbd', {}, hk('paste')), ' in any app'));
  showOverlay('send-menu', [h('div', { class: 'scrim', style: { background: 'transparent' }, onclick: closeOverlay }), menu]);
  el.sendMore.classList.add('active');
  const first = menu.querySelector('.target');
  if (first) first.focus();
}

// ---------- overlays ----------
function closeOverlay() {
  if (S.overlay && S.overlay.onClose) S.overlay.onClose();
  S.overlay = null;
  el.overlay.replaceChildren();
  document.querySelectorAll('.icon-btn.active, #btn-send-more.active').forEach(b => b.classList.remove('active'));
}

function showOverlay(type, nodes, extra = {}) {
  closeOverlay();
  S.overlay = { type, ...extra };
  el.overlay.replaceChildren(...nodes);
}

// ----- welcome -----
function openWelcome() {
  const startup = h('input', { type: 'checkbox', checked: true });
  const finish = async (thenSettings) => {
    try {
      S.settings = await Host.call('setSettings', { patch: { onboarded: true, startWithWindows: startup.checked } });
    } catch (e) { toast(e.message, 'error'); }
    closeOverlay();
    if (thenSettings) openSettings('browser');
    else el.editor.focus();
  };
  const step = (n, title, body) => h('div', { class: 'w-step' }, h('div', { class: 'w-num' }, n), h('div', {}, h('b', {}, title), h('span', {}, body)));
  const modal = h('div', { class: 'modal welcome', role: 'dialog', 'aria-label': 'Welcome to Chipmunk' },
    h('div', { class: 'w-hero' }, h('img', { src: 'assets/chip.png', alt: '' }), h('h3', {}, 'Meet Chip.')),
    h('div', { class: 'w-body' },
      h('p', { class: 'w-lede' }, 'Your AI is only as good as the context you give it. Chipmunk gathers it from anywhere and hands it over in one go.'),
      step(1, 'Collect', h('span', {}, 'Snip ', h('kbd', {}, hk('snip')), ', grab a selection ', h('kbd', {}, hk('grab')), ', drop anything on Chip, or reuse pieces of past chats.')),
      step(2, 'Shape it', 'Type or dictate your prompt. Everything lands in one draft that saves as you go.'),
      step(3, 'Send', h('span', {}, 'Pick ChatGPT, Claude, Codex, Antigravity or any app, or press ', h('kbd', {}, hk('paste')), ' wherever you are. Every screenshot, file and word goes in.')),
      h('div', { class: 'w-foot' },
        h('label', { class: 'switch-label' }, startup, h('span', { class: 'switch' }), 'Start with Windows'),
        h('div', { class: 'row-buttons' },
          h('button', { class: 'btn ghost', onclick: () => finish(true) }, icon('globe'), 'Browser extension'),
          h('button', { class: 'btn primary', style: { flex: 'none' }, onclick: () => finish(false) }, 'Start')))));
  showOverlay('welcome', [h('div', { class: 'modal-wrap' }, modal)]);
}

// ----- drafts drawer -----
async function openDrafts() {
  if (S.overlay && S.overlay.type === 'drafts') return closeOverlay();
  await flushSaves();
  S.drafts = await Host.call('listDrafts');
  const search = h('input', { placeholder: 'Search drafts', 'aria-label': 'Search drafts' });
  const body = h('div', { class: 'panel-body' });
  const draw = () => renderDraftList(body, search.value);
  search.addEventListener('input', draw);
  search.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      const first = body.querySelector('.draft-item');
      if (first) first.click();
    }
  });
  const drawer = h('div', { class: 'drawer', role: 'dialog', 'aria-label': 'Drafts' },
    h('div', { class: 'panel-head' },
      h('h2', {}, 'Drafts'),
      h('button', { class: 'btn', onclick: () => { closeOverlay(); newDraft(); } }, icon('plus'), 'New'),
      h('button', { class: 'icon-btn', 'aria-label': 'Close', onclick: closeOverlay }, icon('x'))),
    h('div', { class: 'search' }, icon('search'), search),
    body,
    h('div', { class: 'panel-foot' },
      h('button', { class: 'btn', style: { flex: 1 }, onclick: () => openHistory() }, icon('clock'), 'Past chats'),
      h('button', { class: 'btn', title: 'Open the drafts folder', 'aria-label': 'Open drafts folder', onclick: () => Host.call('openDataFolder') }, icon('folder'))));
  showOverlay('drafts', [h('div', { class: 'scrim', onclick: closeOverlay }), drawer]);
  $('#btn-drafts').classList.add('active');
  draw();
  search.focus();
}

function renderDraftList(body, q) {
  body.replaceChildren();
  const needle = q.trim().toLowerCase();
  const list = S.drafts.filter(d => !needle || (d.title + ' ' + d.preview).toLowerCase().includes(needle));
  if (!list.length) {
    body.append(h('div', { class: 'list-empty' }, needle ? 'No drafts match.' : 'No drafts yet.'));
    return;
  }
  const groups = [
    ['Pinned', list.filter(d => d.pinned)],
    ['In progress', list.filter(d => !d.pinned && d.status !== 'sent')],
    ['Sent', list.filter(d => !d.pinned && d.status === 'sent')],
  ];
  for (const [label, items] of groups) {
    if (!items.length) continue;
    body.append(h('div', { class: 'group-label' }, label));
    for (const d of items) body.append(draftItem(d));
  }
}

function draftItem(d) {
  const item = h('div', {
    class: 'draft-item' + (S.d && d.id === S.d.id ? ' current' : ''),
    role: 'button', tabindex: '0',
    onclick: async (e) => {
      if (e.target.closest('.di-actions')) return;
      closeOverlay();
      await Host.call('setCurrent', { id: d.id });
      openDraft(d.id);
    },
    onkeydown: (e) => { if (e.key === 'Enter') item.click(); },
  },
  h('div', { class: 'di-top' },
    d.pinned ? h('span', { class: 'pinned-mark' }, icon('pin')) : null,
    h('span', { class: 'di-title' }, d.title || 'Untitled draft'),
    d.status === 'sent' ? h('span', { class: 'badge sent' }, 'Sent') : null,
    d.imported ? h('span', { class: 'badge imported' }, 'Imported') : null),
  d.preview ? h('div', { class: 'di-preview' }, d.preview) : null,
  h('div', { class: 'di-meta' },
    h('span', {}, ago(d.updated)),
    d.imageCount ? h('span', { class: 'img-count' }, icon('image'), d.imageCount) : null,
    d.attachmentCount - d.imageCount > 0 ? h('span', { class: 'img-count' }, icon('paperclip'), d.attachmentCount - d.imageCount) : null,
    d.thumbs && d.thumbs.length ? h('span', { class: 'di-thumbs' }, d.thumbs.map(u => h('img', { src: u, alt: '' }))) : null),
  h('div', { class: 'di-actions' },
    h('button', {
      class: 'mini-btn', title: d.pinned ? 'Unpin' : 'Pin to top', 'aria-label': 'Pin',
      onclick: async () => {
        await Host.call('updateDraft', { id: d.id, patch: { pinned: !d.pinned } });
        S.drafts = await Host.call('listDrafts');
        refreshDrawer();
      },
    }, icon('pin')),
    h('button', {
      class: 'mini-btn', title: 'Delete draft', 'aria-label': 'Delete draft',
      onclick: () => deleteDraft(d.id, true),
    }, icon('trash'))));
  return item;
}

function refreshDrawer() {
  if (!S.overlay || S.overlay.type !== 'drafts') return;
  const body = el.overlay.querySelector('.panel-body');
  const q = el.overlay.querySelector('.search input').value;
  renderDraftList(body, q);
}

async function newDraft() {
  await flushSaves();
  const d = await Host.call('newDraft', {});
  await openDraft(d.id, d);
  el.editor.focus();
}

async function deleteDraft(id, fromDrawer) {
  try {
    const r = await Host.call('deleteDraft', { id });
    if (S.d && S.d.id === id) await openDraft(r.currentId);
    S.drafts = await Host.call('listDrafts');
    if (fromDrawer) refreshDrawer();
    toast('Draft moved to trash', null, {
      label: 'Undo',
      run: async () => {
        const d = await Host.call('undeleteDraft', { id });
        S.drafts = await Host.call('listDrafts');
        refreshDrawer();
        await openDraft(d.id, d);
      },
    });
  } catch (e) { toast(e.message, 'error'); }
}

// ----- more menu -----
function openMenu(anchor) {
  if (S.overlay && S.overlay.type === 'menu') return closeOverlay();
  const r = anchor.getBoundingClientRect();
  const item = (ic, label, fn, cls) => h('button', { class: cls || '', onclick: () => { closeOverlay(); fn(); } }, icon(ic), label);
  const menu = h('div', { class: 'menu', style: { top: r.bottom + 4 + 'px', right: (window.innerWidth - r.right) + 'px' } },
    item('duplicate', 'Duplicate draft', async () => {
      await flushSaves();
      const d = await Host.call('newDraft', { duplicateOf: S.d.id });
      await openDraft(d.id, d);
      toast('Made a copy. The original is unchanged.');
    }),
    item('clock', 'Version history', () => openVersions()),
    item('inbox', 'Past chats', () => openHistory()),
    item('folder', 'Show in folder', () => Host.call('revealDraft', { id: S.d.id })),
    item(S.d.status === 'sent' ? 'restore' : 'check', S.d.status === 'sent' ? 'Mark as in progress' : 'Mark as sent', async () => {
      applyDraft(await Host.call('updateDraft', { id: S.d.id, patch: { status: S.d.status === 'sent' ? 'draft' : 'sent' } }));
    }),
    h('div', { class: 'sep' }),
    item('trash', 'Delete draft', () => deleteDraft(S.d.id), 'danger'));
  showOverlay('menu', [h('div', { class: 'scrim', style: { background: 'transparent' }, onclick: closeOverlay }), menu]);
  $('#btn-more').classList.add('active');
}

// ----- quote viewer -----
function openQuote(a) {
  const modal = h('div', { class: 'modal', style: { maxWidth: '520px' } },
    h('h3', {}, a.source || 'Quote'),
    a.url ? h('div', { class: 'muted', style: { fontSize: '11.5px', marginBottom: '6px', wordBreak: 'break-all' } }, a.url) : null,
    h('div', { class: 'version-preview', style: { maxHeight: '55vh' } }, a.text),
    h('div', { class: 'modal-actions', style: { marginTop: '12px' } },
      h('button', { class: 'btn', onclick: () => { Host.call('copyToClipboard', { text: a.text }).then(() => toast('Quote copied')); } }, icon('copy'), 'Copy'),
      h('button', { class: 'btn primary', style: { flex: 'none' }, onclick: closeOverlay }, 'Close')));
  showOverlay('quote', [h('div', { class: 'modal-wrap', onclick: (e) => { if (e.target === e.currentTarget) closeOverlay(); } }, modal)]);
}

// ----- lightbox -----
function openLightbox(attId) {
  const images = (S.d.attachments || []).filter(a => a.kind === 'image' && a.url);
  let i = Math.max(0, images.findIndex(a => a.id === attId));
  const img = h('img', { alt: '' });
  const title = h('div', { class: 'lb-title' });
  const sub = h('div', { class: 'lb-sub' });
  const show = () => {
    const a = images[i];
    img.src = a.url;
    img.alt = a.name;
    title.textContent = a.name;
    sub.textContent = [`${i + 1} of ${images.length}`, a.source, a.w ? `${a.w}×${a.h}` : null, fmtBytes(a.bytes)].filter(Boolean).join(' · ');
  };
  const step = (n) => { i = (i + n + images.length) % images.length; show(); };
  const box = h('div', { class: 'lightbox', role: 'dialog', 'aria-label': 'Image preview' },
    h('div', { class: 'lb-head' },
      h('div', { style: { flex: 1, minWidth: 0 } }, title, sub),
      h('button', { class: 'icon-btn', title: 'Open in your image viewer', 'aria-label': 'Open externally', onclick: () => Host.call('openAttachment', { id: S.d.id, attId: images[i].id }) }, icon('external')),
      h('button', { class: 'icon-btn', title: 'Remove', 'aria-label': 'Remove', onclick: () => { const a = images[i]; closeOverlay(); removeAttachment(a); } }, icon('trash')),
      h('button', { class: 'icon-btn', title: 'Close (Esc)', 'aria-label': 'Close', onclick: closeOverlay }, icon('x'))),
    h('div', { class: 'lb-img', onclick: (e) => { if (e.target === e.currentTarget) closeOverlay(); } }, img),
    images.length > 1 ? h('button', { class: 'icon-btn lb-nav prev', 'aria-label': 'Previous', onclick: () => step(-1) }, icon('chevronLeft')) : null,
    images.length > 1 ? h('button', { class: 'icon-btn lb-nav next', 'aria-label': 'Next', onclick: () => step(1) }, icon('chevronRight')) : null);
  showOverlay('lightbox', [box], { step });
  show();
}

// ----- versions -----
async function openVersions() {
  await flushSaves();
  const versions = await Host.call('versions', { id: S.d.id });
  const body = h('div', { class: 'panel-body' });
  const preview = h('div', { class: 'version-preview' }, 'Pick a version to preview it.');
  let selected = null;
  const restoreBtn = h('button', { class: 'btn primary', disabled: true, onclick: async () => {
    if (!selected) return;
    const d = await Host.call('restoreVersion', { id: S.d.id, name: selected });
    closeOverlay();
    S.d.text = d.text;
    applyDraft(d, { replaceText: true });
    toast('Restored. Your previous text was kept as a version too.');
  } }, icon('restore'), 'Restore this version');
  if (!versions.length) body.append(h('div', { class: 'list-empty' }, 'No earlier versions yet. Chipmunk keeps one every minute while you write.'));
  for (const v of versions) {
    const row = h('button', { class: 'version-item', onclick: async () => {
      body.querySelectorAll('.version-item').forEach(n => n.classList.remove('selected'));
      row.classList.add('selected');
      selected = v.name;
      preview.textContent = await Host.call('getVersion', { id: S.d.id, name: v.name });
      restoreBtn.disabled = false;
    } },
    h('span', { class: 'vi-time' }, new Date(v.time).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })),
    h('span', { class: 'vi-prev' }, v.preview || '(empty)'),
    h('span', { class: 'muted', style: { fontSize: '11px' } }, plural(v.words, 'word')));
    body.append(row);
  }
  body.append(preview);
  const sheet = h('div', { class: 'sheet', role: 'dialog', 'aria-label': 'Version history' },
    h('div', { class: 'panel-head' }, h('h2', {}, h('span', {}, 'Version history')),
      h('button', { class: 'icon-btn', 'aria-label': 'Close', onclick: closeOverlay }, icon('x'))),
    body,
    h('div', { class: 'panel-foot' }, restoreBtn));
  showOverlay('versions', [h('div', { class: 'scrim', onclick: closeOverlay }), sheet]);
}

// ----- past chats -----
// Every chat in one place: Claude Code, Codex and Antigravity read straight from this PC;
// ChatGPT, Claude and Gemini chats saved with the browser extension or imported from a data
// export. Pick any prompts, replies or images, from one chat or several, and add them to a draft.
const HISTORY_HELP = {
  chatgpt: {
    title: 'Bring your ChatGPT chats here',
    steps: [
      ['Save a chat', 'Open it on chatgpt.com, click the Chipmunk extension and choose Save this chat.'],
      ['Use the ChatGPT app?', 'It keeps chats online, not on your PC. Open the same chat on chatgpt.com (it\'s synced) and save it there.'],
      ['Or bring everything', 'In ChatGPT: Settings → Data controls → Export data. Then import the .zip you get by email.'],
    ],
    importable: true,
  },
  claude: {
    title: 'Bring your Claude chats here',
    steps: [
      ['Save a chat', 'Open it on claude.ai, click the Chipmunk extension and choose Save this chat.'],
      ['Or bring everything', 'In Claude: Settings → Privacy → Export data. Then import the .zip you get by email.'],
    ],
    importable: true,
  },
  gemini: {
    title: 'Bring your Gemini chats here',
    steps: [['Save a chat', 'Open it on gemini.google.com, click the Chipmunk extension and choose Save this chat.']],
  },
};

let historySource = null;

async function openHistory(startSource) {
  if (S.overlay && S.overlay.type === 'history') return;
  await flushSaves();
  const titleEl = h('span', {}, 'Past chats');
  const back = h('button', { class: 'icon-btn', 'aria-label': 'Back', title: 'All chats', style: { display: 'none' } }, icon('chevronLeft'));
  const headExtra = h('span', { class: 'head-extra' });
  const tabs = h('div', { class: 'source-tabs', role: 'tablist', 'aria-label': 'Where the chats come from' });
  const search = h('input', { placeholder: 'Search chats', 'aria-label': 'Search' });
  const searchBox = h('div', { class: 'search' }, icon('search'), search);
  const body = h('div', { class: 'panel-body' });
  const foot = h('div', { class: 'panel-foot history-foot', style: { display: 'none' } });
  const sheet = h('div', { class: 'sheet history', role: 'dialog', 'aria-label': 'Past chats' },
    h('div', { class: 'panel-head' }, back, h('h2', {}, titleEl), headExtra,
      h('button', { class: 'icon-btn', 'aria-label': 'Close', title: 'Close (Esc)', onclick: closeOverlay }, icon('x'))),
    tabs, searchBox, body, foot);
  showOverlay('history', [h('div', { class: 'scrim', onclick: closeOverlay }), sheet]);
  const ov = S.overlay;

  let sources = [];
  let sessions = [];
  let source = startSource || historySource;
  let view = 'list';
  let onSearch = () => {};
  search.addEventListener('input', () => onSearch());

  const loadSources = async () => {
    sources = await Host.call('historySources');
    if (!sources.find(s => s.id === source)) source = sources[0].id;
  };
  const current = () => sources.find(s => s.id === source) || sources[0];

  const drawTabs = () => {
    tabs.replaceChildren(...sources.map(s => h('button', {
      class: 'source-tab' + (s.id === source ? ' on' : ''), role: 'tab', 'aria-selected': String(s.id === source),
      onclick: () => { if (s.id === source) return; source = historySource = s.id; search.value = ''; showList(); },
    }, s.name, s.count > 0 ? h('span', { class: 'n' }, s.count) : null)));
  };

  const importExport = async (btn) => {
    if (btn) btn.disabled = true;
    try {
      const r = await Host.call('historyImport');
      if (r) {
        source = historySource = r.source;
        await loadSources();
        toast(`Imported ${plural(r.chats, 'chat')}${r.images ? ` with ${plural(r.images, 'image')}` : ''}`);
        showList();
      }
    } catch (e) { toast(e.message, 'error'); }
    if (btn) btn.disabled = false;
  };

  const helpCard = (src, compact) => {
    const help = HISTORY_HELP[src.id];
    if (!help) return null;
    if (compact) {
      return h('div', { class: 'history-hint' },
        h('span', {}, icon('globe'), `Save more from ${src.name} with the browser extension.`),
        help.importable ? h('button', { class: 'chip-btn', onclick: (e) => importExport(e.currentTarget) }, icon('upload'), 'Import export') : null);
    }
    return h('div', { class: 'history-empty' },
      h('img', { src: 'assets/chip.png', alt: '' }),
      h('div', { class: 'he-title' }, help.title),
      h('ol', { class: 'he-steps' }, help.steps.map(([b, s]) => h('li', {}, h('b', {}, b), h('span', {}, s)))),
      h('div', { class: 'row-buttons' },
        help.importable ? h('button', { class: 'btn primary', style: { flex: 'none' }, onclick: (e) => importExport(e.currentTarget) }, icon('upload'), 'Import export (.zip)') : null,
        h('button', { class: 'btn', onclick: () => { closeOverlay(); openSettings('browser'); } }, icon('globe'), 'Set up the extension')));
  };

  const showList = async () => {
    view = 'list';
    back.style.display = 'none';
    headExtra.replaceChildren();
    titleEl.textContent = 'Past chats';
    tabs.style.display = '';
    searchBox.style.display = '';
    search.placeholder = 'Search chats';
    foot.style.display = 'none';
    onSearch = drawList;
    drawTabs();
    const src = current();
    body.replaceChildren(h('div', { class: 'list-empty' }, `Reading ${src.name}…`));
    let list;
    try { list = await Host.call('historySessions', { source: src.id }); }
    catch (e) { if (source === src.id) body.replaceChildren(h('div', { class: 'list-empty' }, e.message)); return; }
    if (source !== src.id || view !== 'list') return; // switched away while loading
    sessions = list;
    drawList();
  };

  const drawList = () => {
    const src = current();
    const q = search.value.trim().toLowerCase();
    const list = sessions.filter(s => !q || `${s.title} ${s.sub || ''} ${s.search || ''}`.toLowerCase().includes(q));
    body.replaceChildren();
    if (!sessions.length) {
      body.append(helpCard(src, false) || h('div', { class: 'list-empty' }, `No ${src.name} sessions on this PC yet.`));
      return;
    }
    const hint = src.local ? null : helpCard(src, true);
    if (hint) body.append(hint);
    if (!list.length) { body.append(h('div', { class: 'list-empty' }, 'No chats match.')); return; }
    for (const s of list.slice(0, 400)) {
      body.append(h('button', { class: 'session-item', onclick: () => showChat(s) },
        h('span', { class: 'si-title' }, s.title || 'Untitled chat'),
        h('span', { class: 'si-meta' },
          s.sub ? h('span', { class: 'si-sub' }, s.sub) : null,
          h('span', {}, ago(s.updated)),
          s.turns > 0 ? h('span', {}, plural(s.turns, 'prompt')) : null,
          s.images > 0 ? h('span', { class: 'img-count' }, icon('image'), s.images) : null)));
    }
  };

  const showChat = async (s) => {
    view = 'chat';
    const src = current();
    back.style.display = '';
    back.onclick = () => { view = 'list'; showListCached(); };
    titleEl.textContent = s.title || 'Untitled chat';
    tabs.style.display = 'none';
    search.value = '';
    search.placeholder = 'Search this chat';
    headExtra.replaceChildren();
    body.replaceChildren(h('div', { class: 'list-empty' }, 'Opening chat…'));
    let conv;
    try { conv = await Host.call('historyConversation', { source: src.id, sessionId: s.id }); }
    catch (e) { body.replaceChildren(h('div', { class: 'list-empty' }, e.message)); return; }
    if (view !== 'chat') return;

    if (!src.local) {
      const del = h('button', { class: 'icon-btn', title: 'Remove this saved chat from Chipmunk', 'aria-label': 'Remove saved chat' }, icon('trash'));
      let armed = false;
      del.onclick = async () => {
        if (!armed) { armed = true; del.classList.add('confirm'); del.replaceChildren(h('span', {}, 'Remove?')); setTimeout(() => { armed = false; del.classList.remove('confirm'); del.replaceChildren(icon('trash')); }, 3000); return; }
        try {
          await Host.call('historyDelete', { source: src.id, sessionId: s.id });
          await loadSources();
          toast('Removed from Past chats. The chat itself is untouched.');
          showList();
        } catch (e) { toast(e.message, 'error'); }
      };
      headExtra.append(del);
    }

    const picked = new Map();
    const turns = conv.turns || [];
    const userTurns = turns.filter(t => t.role === 'user' && t.text.trim());
    const imageKeys = turns.flatMap(t => (t.images || []).map(i => [`i${t.index}.${i.index}`, { kind: 'image', turn: t.index, image: i.index }]));

    const count = h('span', { class: 'pick-count' });
    const addBtn = h('button', { class: 'btn primary', style: { flex: 'none' } }, icon('plus'), 'Add to draft');
    const newBtn = h('button', { class: 'btn', title: 'Start a new draft with what you picked' }, 'New draft');
    const updateFoot = () => {
      const n = picked.size;
      const imgs = [...picked.values()].filter(p => p.kind === 'image').length;
      count.textContent = n ? [n - imgs ? plural(n - imgs, 'message') : null, imgs ? plural(imgs, 'image') : null].filter(Boolean).join(' + ') + ' picked' : 'Pick prompts, replies or images';
      addBtn.disabled = newBtn.disabled = !n;
      body.querySelectorAll('[data-key]').forEach(n2 => n2.classList.toggle('picked', picked.has(n2.dataset.key)));
    };
    const toggle = (key, item) => { if (picked.has(key)) picked.delete(key); else picked.set(key, item); updateFoot(); };

    const add = async (mode) => {
      addBtn.disabled = newBtn.disabled = true;
      try {
        const r = await Host.call('historyAdd', { source: src.id, sessionId: s.id, items: [...picked.values()], mode });
        if (mode === 'new') await openDraft(r.draft.id, r.draft);
        else { S.d.text = r.draft.text; applyDraft(r.draft, { replaceText: true }); }
        picked.clear();
        updateFoot();
        toast(`Added to “${r.draft.title || 'your draft'}”. Pick more from any chat, or close when you're done.`);
      } catch (e) { toast(e.message, 'error'); updateFoot(); }
    };
    addBtn.onclick = () => add('add');
    newBtn.onclick = () => add('new');

    const quick = h('div', { class: 'quick-picks' },
      userTurns.length ? h('button', { class: 'chip-btn', onclick: () => { userTurns.forEach(t => picked.set(`t${t.index}`, { kind: 'text', turn: t.index })); updateFoot(); } }, icon('quote'), `All my prompts (${userTurns.length})`) : null,
      imageKeys.length ? h('button', { class: 'chip-btn', onclick: () => { imageKeys.forEach(([k, v]) => picked.set(k, v)); updateFoot(); } }, icon('image'), `All images (${imageKeys.length})`) : null,
      h('button', { class: 'chip-btn', onclick: () => { picked.clear(); updateFoot(); } }, 'Clear'));

    const turnCard = (t) => {
      const mine = t.role === 'user';
      const key = `t${t.index}`;
      const long = t.text.length > (mine ? 600 : 360);
      const text = h('div', { class: 'turn-text' + (long ? ' clamped' : '') }, t.text || (mine ? '(images only)' : ''));
      const card = h('div', { class: 'turn ' + (mine ? 'mine' : 'reply'), 'data-search': t.text.toLowerCase() },
        h('div', { class: 'turn-head' },
          h('span', { class: 'who' }, mine ? 'You' : conv.sourceName),
          t.time ? h('span', { class: 'when' }, new Date(t.time).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })) : null,
          long ? h('button', { class: 'link-btn', onclick: (e) => { const c = text.classList.toggle('clamped'); e.currentTarget.textContent = c ? 'Show all' : 'Show less'; } }, 'Show all') : null),
        t.text.trim() ? h('button', {
          class: 'turn-body', 'data-key': key, 'aria-pressed': 'false',
          title: mine ? 'Add this prompt to your draft text' : 'Add this reply as a quote',
          onclick: (e) => { if (window.getSelection().toString()) return; toggle(key, { kind: 'text', turn: t.index }); },
        }, h('span', { class: 'tick' }, icon('check')), text) : null,
        t.images && t.images.length ? h('div', { class: 'turn-images' }, t.images.map(i => {
          const k = `i${t.index}.${i.index}`;
          return h('button', { class: 'turn-img', 'data-key': k, title: 'Add this image', onclick: () => toggle(k, { kind: 'image', turn: t.index, image: i.index }) },
            i.thumb ? h('img', { src: i.thumb, alt: '' }) : icon('image'), h('span', { class: 'tick' }, icon('check')));
        })) : null);
      return card;
    };

    const list = h('div', { class: 'turns' }, turns.map(turnCard));
    body.replaceChildren(quick, list);
    if (!turns.length) body.append(h('div', { class: 'list-empty' }, 'This chat has no messages Chipmunk can read.'));
    onSearch = () => {
      const q = search.value.trim().toLowerCase();
      list.querySelectorAll('.turn').forEach(c => { c.style.display = !q || c.dataset.search.includes(q) ? '' : 'none'; });
    };
    foot.replaceChildren(count, h('span', { style: { flex: 1 } }), newBtn, addBtn);
    foot.style.display = '';
    updateFoot();
    body.scrollTop = 0;
  };

  const showListCached = () => {
    back.style.display = 'none';
    headExtra.replaceChildren();
    titleEl.textContent = 'Past chats';
    tabs.style.display = '';
    search.value = '';
    search.placeholder = 'Search chats';
    foot.style.display = 'none';
    onSearch = drawList;
    drawTabs();
    drawList();
  };

  // Esc in a chat goes back to the list; Esc in the list closes Past chats.
  ov.onEscape = () => { if (view !== 'chat') return false; back.onclick(); return true; };
  ov.refresh = async () => { await loadSources(); if (view === 'list') showList(); else drawTabs(); };

  try { await loadSources(); }
  catch (e) { body.replaceChildren(h('div', { class: 'list-empty' }, e.message)); return; }
  showList();
  search.focus();
}

// ----- settings -----
const HOTKEY_LABELS = {
  toggle: ['Open or hide Chipmunk', 'From any app'],
  snip: ['Snip', 'Select part of the screen'],
  grab: ['Grab selection', 'Adds what you highlighted'],
  paste: ['Send', 'Pastes the draft into the app you\'re in'],
};

function codeBox(text) {
  return h('div', { class: 'code' }, text,
    h('button', { class: 'mini-btn copy', title: 'Copy', 'aria-label': 'Copy', onclick: async () => {
      await Host.call('copyToClipboard', { text });
      toast('Copied');
    } }, icon('copy')));
}

async function openSettings(focus) {
  if (S.overlay && S.overlay.type === 'settings' && !focus) return closeOverlay();
  const body = h('div', { class: 'panel-body' });
  const errors = {};
  let agent = null, browser = null, storage = null;

  const save = async (patch) => {
    try {
      const r = await Host.call('setSettings', { patch });
      S.settings = r;
      applyTheme();
      renderAll();
      if (r.hotkeyErrors && Object.keys(r.hotkeyErrors).length) Object.assign(errors, r.hotkeyErrors);
      drawBody();
    } catch (e) { toast(e.message, 'error'); }
  };

  const toggleRow = (key, title, sub) => {
    const input = h('input', { type: 'checkbox', checked: !!S.settings[key], onchange: () => save({ [key]: input.checked }) });
    return h('div', { class: 'setting-row' },
      h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, title), sub ? h('div', { class: 'sr-sub' }, sub) : null),
      h('label', { class: 'switch-label' }, input, h('span', { class: 'switch' })));
  };

  const selectRow = (title, sub, value, options, onchange) => {
    const sel = h('select', { class: 'select', onchange: () => onchange(sel.value) },
      options.map(([v, l]) => h('option', { value: v, selected: String(value) === String(v) }, l)));
    return h('div', { class: 'setting-row' },
      h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, title), sub ? h('div', { class: 'sr-sub' }, sub) : null), sel);
  };

  const hotkeyRow = (name) => {
    const [title, sub] = HOTKEY_LABELS[name];
    const value = (S.settings.hotkeys || {})[name] || '';
    const input = h('input', { class: 'hotkey-input' + (errors[name] ? ' error' : ''), value, readonly: true, 'aria-label': title });
    input.addEventListener('focus', () => { input.value = 'Press keys…'; Host.call('suspendHotkeys', { on: true }); });
    input.addEventListener('blur', () => { input.value = (S.settings.hotkeys || {})[name] || ''; Host.call('suspendHotkeys', { on: false }); });
    input.addEventListener('keydown', (e) => {
      e.preventDefault();
      if (e.key === 'Escape') { input.blur(); return; }
      if (['Control', 'Alt', 'Shift', 'Meta', 'OS'].includes(e.key)) return;
      const mods = [e.ctrlKey && 'Ctrl', e.metaKey && 'Win', e.shiftKey && 'Shift', e.altKey && 'Alt'].filter(Boolean);
      if (!mods.length || (mods.length === 1 && mods[0] === 'Shift')) {
        input.value = 'Add Win, Ctrl or Alt';
        return;
      }
      const key = keyName(e);
      if (!key) return;
      const order = ['Ctrl', 'Win', 'Shift', 'Alt'];
      const combo = [...order.filter(m => mods.includes(m)), key].join('+');
      delete errors[name];
      input.blur();
      save({ hotkeys: { ...S.settings.hotkeys, [name]: combo } });
    });
    return h('div', { class: 'setting-row' },
      h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, title),
        h('div', { class: 'sr-sub' }, sub),
        errors[name] ? h('div', { class: 'setting-error' }, errors[name]) : null),
      input);
  };

  const group = (iconName, title, ...rows) => h('div', { class: 'settings-group', 'data-group': title.toLowerCase() }, h('h3', {}, icon(iconName), title), ...rows);

  const drawBody = () => {
    const st = S.settings;
    const app = st.dictationApp;
    body.replaceChildren(
      group('command', 'Shortcuts', ...Object.keys(HOTKEY_LABELS).map(hotkeyRow)),

      group('mic', 'Dictation',
        h('div', { class: 'setting-row' },
          h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, app ? `${app} works here` : 'Use the dictation app you already have'),
            h('div', { class: 'sr-sub' }, app
              ? `Click in your prompt and ${(st.dictation || {}).hint || 'use its shortcut'}. ${app} types straight into the draft.`
              : 'Wispr Flow, Superwhisper, Aqua and Windows voice typing (Win+H) all type straight into your draft. Click in the prompt and talk.')),
          app ? h('span', { class: 'status-pill ok' }, icon('check'), 'Detected') : h('span', { class: 'status-pill idle' }, 'Win+H'))),

      group('globe', 'Browser extension',
        h('div', { class: 'setting-row col' },
          h('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '10px' } },
            h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, 'Collect from any website'),
              h('div', { class: 'sr-sub' }, 'Right-click a selection, image, link or page and choose "Add to Chipmunk". On ChatGPT, Claude and Gemini, "Save this chat" keeps the whole conversation for Past chats. Works in Chrome, Edge and Brave.')),
            st.usedExtension ? h('span', { class: 'status-pill ok' }, icon('check'), 'Connected') : h('span', { class: 'status-pill idle' }, 'Not set up')),
          h('ol', { class: 'steps' },
            h('li', {}, 'Open ', h('b', {}, 'chrome://extensions'), ' (or ', h('b', {}, 'edge://extensions'), ') and turn on ', h('b', {}, 'Developer mode'), '.'),
            h('li', {}, 'Click ', h('b', {}, 'Load unpacked'), ' and pick the extension folder below.'),
            h('li', {}, 'Pin Chipmunk to your toolbar. That\'s it.')),
          browser ? codeBox(browser.folder) : null,
          h('div', { class: 'row-buttons' },
            h('button', { class: 'btn', onclick: () => Host.call('openExtensionFolder') }, icon('folder'), 'Open extension folder'),
            h('button', { class: 'btn ghost', onclick: async () => { await Host.call('copyToClipboard', { text: 'chrome://extensions' }); toast('Copied chrome://extensions. Paste it into your address bar.'); } }, icon('copy'), 'Copy chrome://extensions')))),

      group('cpu', 'AI agents',
        h('div', { class: 'setting-row col' },
          h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, 'Let coding agents read your draft'),
            h('div', { class: 'sr-sub' }, 'Connect once, then just say "use my Chipmunk draft". The agent gets your text, screenshots and files directly, with no pasting.')),
          agent ? h('div', { class: 'sr-sub', style: { fontWeight: 600 } }, 'Claude Code') : null,
          agent ? codeBox(agent.claude) : null,
          agent ? h('div', { class: 'sr-sub', style: { fontWeight: 600 } }, 'Codex') : null,
          agent ? codeBox(agent.codex) : null,
          agent ? h('div', { class: 'sr-sub', style: { fontWeight: 600 } }, 'Antigravity, Cursor, Claude Desktop and other MCP apps') : null,
          agent ? codeBox(agent.json) : null)),

      group('smile', 'Chip',
        toggleRow('showPet', 'Show Chip on your desktop', 'Click Chip to send your draft to any open AI app. Drop files, images, links or text on it to collect them. Hides during fullscreen apps.'),
        st.showPet ? toggleRow('petHints', 'Chip suggestions', 'Chip offers to paste your draft when you switch to an AI app, and to keep screenshots you take. One click, or ignore it.') : null),

      group('send', 'Sending',
        toggleRow('shrinkImages', 'Shrink huge screenshots', 'AI apps scale big images down anyway. This makes uploads faster.'),
        toggleRow('terminalPaths', 'Claude Code in a terminal: send files as @paths', 'Claude Code reads files by path, which is more reliable than pasting images.'),
        toggleRow('restoreClipboard', 'Put my clipboard back afterwards', 'Whatever you had copied before is restored after sending.'),
        selectRow('Paste speed', 'Pause between each pasted item', st.pasteDelayMs, [[250, 'Fast'], [450, 'Normal'], [900, 'Slow, for laggy apps']], v => save({ pasteDelayMs: Number(v) }))),

      group('scissors', 'Capture',
        toggleRow('collectText', 'Collect mode also grabs copied text', 'Off: only screenshots are collected.')),

      group('sliders', 'General',
        toggleRow('startWithWindows', 'Start with Windows', 'Runs quietly in the tray so your shortcuts always work.'),
        toggleRow('alwaysOnTop', 'Keep the panel on top'),
        selectRow('Theme', null, st.theme || 'system', [['system', 'Match Windows'], ['light', 'Light'], ['dark', 'Dark']], v => save({ theme: v })),
        h('div', { class: 'setting-row' },
          h('div', { class: 'sr-text' }, h('div', { class: 'sr-title' }, 'Drafts folder'),
            h('div', { class: 'path-box' }, st.dataDir || ''),
            storage ? h('div', { class: 'sr-sub' }, `${fmtBytes(storage.bytes)} in ${plural(storage.drafts, 'draft')}. Files over 25 MB are linked, not copied${storage.linked ? ` (${storage.linked} linked)` : ''}.`) : null),
          h('button', { class: 'btn', onclick: () => Host.call('openDataFolder') }, icon('folder'), 'Open'))),

      h('div', { class: 'muted', style: { fontSize: '11.5px', textAlign: 'center', padding: '6px 0 0' } }, `Chipmunk ${st.version || ''} · open source · your drafts are plain files you own`));
  };
  drawBody();

  const sheet = h('div', { class: 'sheet', role: 'dialog', 'aria-label': 'Settings' },
    h('div', { class: 'panel-head' }, h('h2', {}, 'Settings'),
      h('button', { class: 'icon-btn', 'aria-label': 'Close', onclick: closeOverlay }, icon('x'))),
    body);
  showOverlay('settings', [h('div', { class: 'scrim', onclick: closeOverlay }), sheet]);
  $('#btn-settings').classList.add('active');

  try {
    [agent, browser] = await Promise.all([Host.call('agentInfo'), Host.call('browserInfo')]);
    drawBody();
    storage = await Host.call('storageInfo');
    if (S.overlay && S.overlay.type === 'settings') drawBody();
  } catch (e) { toast(e.message, 'error'); }
  if (focus) {
    const target = body.querySelector(`[data-group="${focus === 'browser' ? 'browser extension' : focus}"]`);
    if (target) target.scrollIntoView({ block: 'start' });
  }
}

function keyName(e) {
  const k = e.key;
  if (/^[a-z]$/i.test(k)) return k.toUpperCase();
  if (/^[0-9]$/.test(k)) return k;
  if (/^F([1-9]|1[0-9]|2[0-4])$/.test(k)) return k;
  const map = { ' ': 'Space', Enter: 'Enter', Tab: 'Tab', Backspace: 'Backspace', Delete: 'Delete', Insert: 'Insert', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown', ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right', '`': 'Backquote', ',': 'Comma', '.': 'Period', '/': 'Slash', ';': 'Semicolon', "'": 'Quote', '[': 'OpenBracket', ']': 'CloseBracket', '\\': 'Backslash', '-': 'Minus', '=': 'Plus' };
  if (map[k]) return map[k];
  if (e.code && /^Key[A-Z]$/.test(e.code)) return e.code.slice(3);
  if (e.code && /^Digit[0-9]$/.test(e.code)) return e.code.slice(5);
  if (e.code === 'Space') return 'Space';
  return null;
}

function applyTheme() {
  const t = S.settings.theme || 'system';
  if (t === 'system') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = t;
}

// ----- modal + toast -----
function confirmModal({ title, body, ok, cancel }) {
  return new Promise((resolve) => {
    const done = (v) => { closeOverlay(); resolve(v); };
    const okBtn = h('button', { class: 'btn primary', style: { flex: 'none' }, onclick: () => done(true) }, ok);
    const modal = h('div', { class: 'modal', role: 'alertdialog' },
      h('h3', {}, title), body,
      h('div', { class: 'modal-actions' },
        h('button', { class: 'btn', onclick: () => done(false) }, cancel), okBtn));
    showOverlay('modal', [h('div', { class: 'modal-wrap' }, modal)], { onClose: () => resolve(false) });
    okBtn.focus();
  });
}

function toast(text, kind, action) {
  if (!text) return;
  const t = h('div', { class: 'toast' + (kind === 'error' ? ' error' : '') }, h('span', { class: 't-text' }, text));
  if (action) t.append(h('button', { onclick: () => { t.remove(); action.run(); } }, action.label));
  el.toasts.append(t);
  while (el.toasts.children.length > 3) el.toasts.firstChild.remove();
  setTimeout(() => t.remove(), action ? 6000 : 3400);
}

// ---------- drag and drop from outside ----------
let dragDepth = 0;
const isInternal = (e) => [...(e.dataTransfer?.types || [])].includes('application/x-chipmunk-att');
const hasPayload = (e) => {
  const types = [...(e.dataTransfer?.types || [])];
  return types.includes('Files') || types.includes('text/uri-list') || types.includes('text/plain');
};
function hideDrop() { dragDepth = 0; el.drop.classList.remove('show'); }

window.addEventListener('dragenter', (e) => {
  if (isInternal(e) || !hasPayload(e)) return;
  dragDepth++;
  el.drop.classList.add('show');
});
window.addEventListener('dragleave', (e) => {
  if (isInternal(e)) return;
  dragDepth = Math.max(0, dragDepth - 1);
  if (!dragDepth) el.drop.classList.remove('show');
});
window.addEventListener('dragover', (e) => {
  if (isInternal(e) || !hasPayload(e)) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = 'copy';
});
window.addEventListener('drop', async (e) => {
  if (isInternal(e)) return;
  e.preventDefault();
  hideDrop();
  const dt = e.dataTransfer;
  const files = [...dt.files];
  if (files.length) return addDroppedFiles(files);
  const uri = (dt.getData('text/uri-list') || '').split('\n').find(l => l && !l.startsWith('#'));
  if (uri) return addTextOrLink(uri.trim(), 'Dropped link');
  const text = dt.getData('text/plain');
  if (text && text.trim()) return addTextOrLink(text, 'Dropped text');
});

// ---------- paste into the panel ----------
document.addEventListener('paste', (e) => {
  const dt = e.clipboardData;
  if (!dt) return;
  const inField = e.target.closest && e.target.closest('textarea, input');
  const hasFiles = dt.files.length > 0 || [...dt.types].includes('Files');
  if (hasFiles) {
    e.preventDefault();
    addFromClipboard();
    return;
  }
  if (!inField) {
    const text = dt.getData('text/plain');
    if (text && text.trim()) {
      e.preventDefault();
      addTextOrLink(text, 'Pasted text');
    }
  }
});

// ---------- keyboard ----------
document.addEventListener('keydown', (e) => {
  const ov = S.overlay && S.overlay.type;
  if (ov === 'lightbox') {
    if (e.key === 'ArrowRight') { S.overlay.step(1); e.preventDefault(); return; }
    if (e.key === 'ArrowLeft') { S.overlay.step(-1); e.preventDefault(); return; }
  }
  if (e.key === 'Escape') {
    if (e.target.classList && e.target.classList.contains('hotkey-input')) return;
    e.preventDefault();
    if (ov === 'welcome') return;
    if (ov && S.overlay.onEscape && S.overlay.onEscape()) return;
    if (ov) closeOverlay();
    else Host.call('hide');
    return;
  }
  const ctrl = e.ctrlKey || e.metaKey;
  if (!ctrl) return;
  const k = e.key.toLowerCase();
  if (k === 'enter') { e.preventDefault(); if (!el.send.disabled) send('paste'); }
  else if (k === 'n' && !e.shiftKey) { e.preventDefault(); closeOverlay(); newDraft(); }
  else if (k === 'k') { e.preventDefault(); openDrafts(); }
  else if (k === ',') { e.preventDefault(); openSettings(); }
  else if (k === 'h' && e.shiftKey) { e.preventDefault(); openHistory(); }
  else if (k === 'v' && e.shiftKey) { e.preventDefault(); addFromClipboard(); }
  else if (k === 's') { e.preventDefault(); flushSaves().then(() => toast('Saved. It already was, by the way.')); }
});

// ---------- wire up ----------
function bind() {
  $('#btn-drafts').append(icon('list'));
  $('#btn-new').append(icon('plus'));
  $('#btn-more').append(icon('more'));
  $('#btn-settings').append(icon('settings'));
  el.copyText.prepend(icon('copy'));
  el.copyFiles.prepend(icon('paperclip'));
  el.dropCard.prepend(icon('upload'));

  $('#btn-drafts').addEventListener('click', openDrafts);
  $('#btn-new').addEventListener('click', () => { closeOverlay(); newDraft(); });
  $('#btn-more').addEventListener('click', (e) => openMenu(e.currentTarget));
  $('#btn-settings').addEventListener('click', () => openSettings());
  el.dictation.addEventListener('click', () => focusEditorEnd());

  el.editor.addEventListener('input', () => {
    S.d.text = el.editor.value;
    autosize();
    setSave('saving');
    clearTimeout(textTimer);
    textTimer = setTimeout(saveText, 350);
    renderFooter();
    renderWordCount();
  });
  el.editor.addEventListener('blur', () => { if (textTimer) saveText(); });
  el.title.addEventListener('input', () => {
    S.d.title = el.title.value;
    setSave('saving');
    clearTimeout(titleTimer);
    titleTimer = setTimeout(saveTitle, 400);
  });
  el.title.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); el.editor.focus(); } });

  el.collectToggle.addEventListener('change', async () => {
    S.collect = el.collectToggle.checked;
    renderCollect();
    try {
      await Host.call('setCollect', { on: S.collect });
      toast(S.collect ? 'Collecting: every screenshot you take now lands in this draft' : 'Stopped collecting screenshots');
    } catch (e) { toast(e.message, 'error'); }
  });

  el.send.addEventListener('click', () => (S.target ? send('paste') : openSendMenu()));
  el.sendMore.addEventListener('click', openSendMenu);
  el.copyText.addEventListener('click', () => send('copyText'));
  el.copyFiles.addEventListener('click', () => send('copyFiles'));
  window.addEventListener('resize', autosize);
  setInterval(() => { renderSaveState(); }, 30000);

  Host.on('draftChanged', (d) => {
    if (S.d && d.id === S.d.id) applyDraft(d, { replaceText: !!d.textChanged });
  });
  Host.on('openDraft', (d) => openDraft(d.id, d));
  Host.on('toast', (t) => toast(t.text, t.kind));
  Host.on('collect', (on) => { S.collect = on; renderCollect(); });
  Host.on('historyChanged', () => { if (S.overlay && S.overlay.type === 'history' && S.overlay.refresh) S.overlay.refresh(); });
  Host.on('target', (t) => { S.target = t; renderFooter(); });
  Host.on('settings', (st) => { S.settings = st; applyTheme(); renderAll(); });
  Host.on('focusEditorEnd', () => { closeOverlay(); focusEditorEnd(); });
  Host.on('shown', () => {
    if (!S.overlay) el.editor.focus();
  });
  Host.on('showView', (v) => {
    if (v === 'settings') openSettings();
    else if (v === 'drafts') openDrafts();
    else if (v === 'history' || v === 'import') openHistory();
  });
  Host.on('flush', async () => { await flushSaves(); Host.call('flushed'); });
}

async function init() {
  bind();
  const r = await Host.call('init');
  S.settings = r.settings;
  S.drafts = r.drafts;
  S.target = r.target;
  S.collect = r.collect;
  applyTheme();
  await openDraft(r.current.id, r.current);
  requestAnimationFrame(() => requestAnimationFrame(() => document.getElementById('app').classList.remove('preload')));
  if (!S.settings.onboarded) openWelcome();
  else el.editor.focus();
}

init().catch((e) => {
  document.body.append(h('pre', { style: { padding: '16px', color: 'var(--danger)', whiteSpace: 'pre-wrap' } }, 'Chipmunk failed to start: ' + (e && e.stack || e)));
});
