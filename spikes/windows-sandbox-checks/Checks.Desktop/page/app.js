// Title bar spike page: reports its title-bar layout to the host, mirrors window state, and logs input events.
const q = new URLSearchParams(location.search);
const buttons = q.get('buttons') ?? 'page';        // page | system | none
const bar = q.get('bar') !== '0';
const host = window.chrome?.webview;
const post = (m) => host?.postMessage(m);
const $ = (id) => document.getElementById(id);

document.documentElement.style.setProperty('--bar-h', `${Number(q.get('h') ?? 32)}px`);
if (!bar) document.body.classList.add('no-bar');
if (buttons === 'page') $('caption').hidden = false;
if (q.get('capdrag') === '1') document.body.classList.add('capdrag');
if (buttons === 'system') {
  $('reserve').hidden = false;
  document.documentElement.style.setProperty('--reserve', `${Number(q.get('reserve') ?? 138)}px`);
}

const rect = (el) => { const r = el.getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom]; };
function layout() {
  const nodrag = [...document.querySelectorAll('.bar .nodrag, .bar .nodrag button, .bar input')]
    .filter((el) => el.offsetParent !== null).map(rect);
  post({
    type: 'layout', dpr: devicePixelRatio, barHeight: bar ? $('bar').getBoundingClientRect().height : 0, nodrag,
    min: buttons === 'page' ? rect($('min')) : null,
    max: buttons === 'page' ? rect($('max')) : null,
    close: buttons === 'page' ? rect($('close')) : null,
    appRegion: CSS.supports('app-region', 'drag'),
    appRegionValues: Object.fromEntries(['drag', 'no-drag', 'minimize', 'maximize', 'close', 'caption']
      .map((v) => [v, CSS.supports('app-region', v)])),
  });
}
new ResizeObserver(layout).observe(document.body);
addEventListener('resize', layout);

// Page caption buttons: plain HTML buttons ask the host; when the host owns their hit test it relays hover/press.
for (const id of ['min', 'max', 'close']) $(id).addEventListener('click', () => post({ type: 'command', command: id }));
host?.addEventListener('message', (e) => {
  const m = e.data;
  if (m.type === 'state') {
    document.body.classList.toggle('inactive', !m.active);
    $('max').setAttribute('aria-label', m.maximized ? 'Restore' : 'Maximize');
    $('max').firstElementChild.textContent = m.maximized ? '' : '';
  } else if (m.type === 'hover') {
    for (const id of ['min', 'max', 'close']) {
      $(id).classList.toggle('hover', m.button === id);
      $(id).classList.toggle('pressed', m.button === id && m.pressed);
    }
  }
});

// Input log for the real-input runs.
for (const kind of ['click', 'dblclick', 'contextmenu', 'pointerdown']) {
  addEventListener(kind, (e) => post({ type: 'input', kind, target: e.target.id || e.target.tagName, x: e.clientX, y: e.clientY }), true);
}
$('search').addEventListener('input', (e) => post({ type: 'input', kind: 'text', target: 'search', value: e.target.value }));
addEventListener('focusin', (e) => post({ type: 'input', kind: 'focus', target: e.target.id || e.target.tagName }));
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => post({ type: 'scheme', dark: e.matches, t: performance.now() }));

$('status').textContent = `buttons=${buttons} dpr=${devicePixelRatio}`;
requestAnimationFrame(() => requestAnimationFrame(() => { layout(); post({ type: 'ready', t: performance.now(), dark: matchMedia('(prefers-color-scheme: dark)').matches }); }));
