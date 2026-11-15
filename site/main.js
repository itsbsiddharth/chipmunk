(() => {
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Nav gets a hairline once you scroll.
  const nav = $('.nav');
  const onScroll = () => nav.classList.toggle('scrolled', scrollY > 8);
  addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  // Not on Windows? Say so kindly, and make it easy to send the link to a PC.
  const ua = navigator.userAgent;
  const isWindows = /Windows/i.test(ua);
  const isMobile = /Android|iPhone|iPad|iPod/i.test(ua);
  if (!isWindows) {
    const note = $('[data-platform-note]');
    if (note) note.textContent = isMobile ? 'Chipmunk is a Windows app. Open this page on your PC to install it.' : 'Chipmunk is for Windows 10 and 11. A Mac version isn\'t out yet.';
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
  const prompt = 'The Sign in button sits on top of the footer on phones. Fix it without changing desktop.';
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
