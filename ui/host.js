// Bridge to the native Chipmunk host (WebView2). When the page runs in a normal
// browser (UI development), an in-memory mock host stands in for it.
const webview = window.chrome && window.chrome.webview;
const listeners = new Map();
const pending = new Map();
let seq = 0;
let mock = null;

function dispatch(msg) {
  if (!msg) return;
  if (msg.replyTo) {
    const p = pending.get(msg.replyTo);
    if (!p) return;
    pending.delete(msg.replyTo);
    if (msg.ok) p.resolve(msg.result);
    else p.reject(new Error(msg.error || 'Something went wrong'));
    return;
  }
  if (msg.event) (listeners.get(msg.event) || []).forEach(fn => fn(msg.data));
}

if (webview) {
  webview.addEventListener('message', e => dispatch(e.data));
} else {
  const { MockHost } = await import('./mock-host.js');
  mock = new MockHost((event, data) => dispatch({ event, data }));
}

export const Host = {
  isMock: !webview,

  call(cmd, args = {}, files = null) {
    if (mock) return mock.call(cmd, args, files);
    const id = ++seq;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      const msg = { id, cmd, args };
      if (files && files.length && webview.postMessageWithAdditionalObjects) {
        webview.postMessageWithAdditionalObjects(msg, files);
      } else {
        webview.postMessage(msg);
      }
    });
  },

  on(event, fn) {
    if (!listeners.has(event)) listeners.set(event, []);
    listeners.get(event).push(fn);
  },
};
