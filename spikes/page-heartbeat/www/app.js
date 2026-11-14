// The app page. It reports a timer tick every second (with the animation frames counted since the last tick, so the
// host sees timer and rAF throttling), and runs the hang, busy and load commands the host sends.
const post = (text) => window.__sparPost(text);
const now = () => performance.now().toFixed(1);

let frames = 0;
const countFrame = () => {
  frames++;
  requestAnimationFrame(countFrame);
};
requestAnimationFrame(countFrame);

let tick = 0;
setInterval(() => {
  tick++;
  post(`tick:${tick}:${now()}:${document.visibilityState}:${frames}`);
  frames = 0;
}, 1000);

const spin = (ms) => {
  const end = performance.now() + ms;
  let x = 0;
  while (performance.now() < end) {
    x++;
  }
  return x;
};

// Healthy but heavy load: an animated element repainted every frame, or 100 ms tasks back to back.
let loadTimer = 0;
let loadKind = '';
const box = document.getElementById('box');
const animate = () => {
  if (loadKind !== 'anim') {
    return;
  }
  const t = performance.now();
  box.style.transform = `translateX(${Math.round(100 + 90 * Math.sin(t / 300))}px) rotate(${Math.round(t / 5) % 360}deg)`;
  spin(8); // 8 ms of script per frame on top of the paint
  requestAnimationFrame(animate);
};
const tasks = (ms) => {
  if (loadKind !== `tasks${ms}`) {
    return;
  }
  spin(ms);
  loadTimer = setTimeout(() => tasks(ms), 0);
};

const run = (command) => {
  const [verb, arg] = command.split(':');
  switch (verb) {
    case 'hang-idle':
      // From a timer, so the hang starts with the page idle (no message handler on the stack).
      setTimeout(() => {
        post(`hang:idle:${now()}`);
        for (;;) { /* hang */ }
      }, 0);
      break;
    case 'hang-handler':
      post(`hang:handler:${now()}`);
      for (;;) { /* hang inside the message handler */ }
    case 'hang-microtask':
      setTimeout(() => {
        post(`hang:microtask:${now()}`);
        const again = () => Promise.resolve().then(again);
        again();
      }, 0);
      break;
    case 'busy':
      setTimeout(() => {
        post(`hang:busy-${arg}:${now()}`);
        spin(Number(arg));
        post(`busy-end:${arg}:${now()}`);
      }, 0);
      break;
    case 'hang-worker': {
      const worker = new Worker('worker.js');
      worker.onmessage = (event) => post(`worker:${event.data}`);
      post(`hang:worker:${now()}`);
      break;
    }
    case 'load':
      loadKind = arg;
      clearTimeout(loadTimer);
      if (arg === 'anim') {
        requestAnimationFrame(animate);
      } else if (arg && arg.startsWith('tasks')) {
        tasks(Number(arg.slice(5)));
      }
      post(`load:${arg}:${now()}`);
      break;
    case 'state':
      post('state?');
      break;
    default:
      post(`unknown:${command}`);
  }
};

window.addEventListener('spar-host', (event) => {
  const data = event.detail;
  if (data.startsWith('cmd:')) {
    run(data.slice(4));
  } else if (data.startsWith('state:')) {
    post(`state-seen:${data.slice(6)}`);
    document.getElementById('state').textContent = data.slice(6);
  } else if (data.startsWith('blob:')) {
    // Stress traffic: counted, not stored.
    window.__blobs = (window.__blobs || 0) + 1;
  }
});

document.addEventListener('DOMContentLoaded', () => {
  post(`ready:${location.pathname}:${now()}:${document.visibilityState}`);
  post('state?');
});
