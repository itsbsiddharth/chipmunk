(() => {
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Nav gets a hairline once you scroll.
  const nav = $('.nav');
  const onScroll = () => nav.classList.toggle('scrolled', scrollY > 8);
  addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  // Mac visitors get the Mac download; phones get a nudge to open the page on a computer.
  const ua = navigator.userAgent;
  const isMobile = /Android|iPhone|iPad|iPod/i.test(ua);
  const isMac = !isMobile && /Macintosh|Mac OS X/i.test(ua);
  const note = $('[data-platform-note]');
  if (isMac) {
    const apple = '<svg class="winlogo" viewBox="0 0 24 24" aria-hidden="true"><path d="M16.37 12.6c-.02-2.2 1.8-3.26 1.88-3.31-1.03-1.5-2.62-1.7-3.18-1.73-1.35-.14-2.64.8-3.33.8-.69 0-1.74-.78-2.86-.76-1.47.02-2.83.86-3.59 2.17-1.53 2.66-.39 6.6 1.1 8.76.73 1.06 1.6 2.24 2.73 2.2 1.1-.04 1.51-.71 2.84-.71 1.32 0 1.7.71 2.86.69 1.18-.02 1.93-1.07 2.65-2.13.84-1.22 1.18-2.41 1.2-2.47-.03-.01-2.3-.88-2.3-3.51zM14.2 6.13c.6-.73 1-1.74.9-2.75-.86.04-1.91.58-2.53 1.3-.55.64-1.04 1.67-.91 2.66.96.07 1.94-.49 2.54-1.21z"/></svg>';
    $$('[data-download]').forEach((el) => {
      el.href = '/download/mac';
      if (el.classList.contains('btn-lg')) el.innerHTML = apple + 'Download for Mac <span class="beta">beta</span>';
    });
    if (note) note.textContent = 'macOS 13 or later \u00b7 free \u00b7 the first time, right-click the app and choose Open';
  } else if (isMobile) {
    if (note) note.textContent = 'Chipmunk runs on Windows and Mac. Open this page on your computer to install it.';
    const mac = $('#mac-note');
    if (mac) {
      mac.hidden = false;
      $('[data-copy-link]', mac).addEventListener('click', async (e) => {
        try { await navigator.clipboard.writeText('https://chipmunk.bsid.dev'); e.target.textContent = 'Copied'; } catch { e.target.textContent = 'chipmunk.bsid.dev'; }
      });
    }
  }

  // Sections ease in as they scroll into view.
  if (!reduced && 'IntersectionObserver' in window) {
    const io = new IntersectionObserver((entries) => {
      for (const e of entries) if (e.isIntersecting) { e.target.classList.add('seen'); io.unobserve(e.target); }
    }, { rootMargin: '0px 0px -8% 0px' });
    $$('.loop-in, .how .card, .shots figure, .grid, .chip-in, .key-table, .install-in, .faq-in').forEach((el) => { el.classList.add('reveal'); io.observe(el); });
  }

  // The hero demo: three screenshots land, the prompt gets typed, Send pastes it all.
  const demo = $('#demo');
  if (!demo) return;
  const tiles = $$('.tile', demo);
  const typed = $('#demo-typed');
  const count = $('#demo-count');
  const flash = $('#demo-flash');
  const send = $('#demo-send');
  const pointer = $('#demo-pointer');
  const steps = $$('.steps-ind li', demo);
  const prompt = 'Fix these three things on my bakery’s website. Keep everything else the same.';
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const step = (n) => steps.forEach((s) => s.classList.toggle('on', Number(s.dataset.step) === n));

  function finalFrame() {
    tiles.forEach((t) => t.classList.add('in'));
    typed.textContent = prompt;
    count.textContent = '3';
    step(3);
  }
  if (reduced) { finalFrame(); return; }

  let visible = true;
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(demo);

  async function run() {
    for (;;) {
      while (!visible) await wait(400);
      demo.classList.remove('sent');
      tiles.forEach((t) => t.classList.remove('in'));
      typed.textContent = '';
      count.textContent = '0';
      pointer.classList.remove('show');
      step(1);
      await wait(700);
      for (let i = 0; i < tiles.length; i++) {
        flash.classList.remove('on'); void flash.offsetWidth; flash.classList.add('on');
        await wait(180);
        tiles[i].classList.add('in');
        count.textContent = String(i + 1);
        await wait(720);
      }
      step(2);
      await wait(250);
      for (let i = 1; i <= prompt.length; i++) {
        typed.textContent = prompt.slice(0, i);
        await wait(prompt[i - 1] === ' ' ? 34 : 22 + Math.random() * 26);
      }
      await wait(450);
      step(3);
      pointer.style.left = '86%'; pointer.style.top = '104%';
      pointer.classList.add('show');
      await wait(60);
      const r = send.getBoundingClientRect(), d = demo.getBoundingClientRect();
      pointer.style.left = (r.left - d.left + r.width * 0.42) + 'px';
      pointer.style.top = (r.top - d.top + r.height * 0.55) + 'px';
      await wait(900);
      send.classList.add('press');
      await wait(160);
      send.classList.remove('press');
      demo.classList.add('sent');
      pointer.classList.remove('show');
      await wait(650);
      typed.textContent = '';
      count.textContent = '0';
      await wait(3200);
    }
  }
  run();
})();
