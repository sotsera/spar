// Camera and microphone probes, called by the host only (window.media.<op>(id, ...)); every answer is a 'media'
// message carrying the call's id.
const host = window.chrome?.webview;
const post = (m) => host?.postMessage(m);
const streams = [];
const status = (text) => { document.getElementById('status').textContent = text; };

const describe = (track) => ({ kind: track.kind, label: track.label, state: track.readyState, muted: track.muted, settings: track.getSettings() });

window.media = {
  async enumerate(id) {
    try {
      const devices = await navigator.mediaDevices.enumerateDevices();
      post({ type: 'media', id, op: 'enum', devices: devices.map((d) => ({ kind: d.kind, label: d.label, hasId: d.deviceId !== '' })) });
    } catch (e) {
      post({ type: 'media', id, op: 'enum', error: `${e.name}: ${e.message}` });
    }
  },

  async query(id) {
    const r = { type: 'media', id, op: 'query' };
    for (const name of ['camera', 'microphone']) {
      try { r[name] = (await navigator.permissions.query({ name })).state; } catch (e) { r[name] = `error ${e.name}`; }
    }
    r.policyCamera = document.featurePolicy?.allowsFeature('camera') ?? null;
    r.policyMicrophone = document.featurePolicy?.allowsFeature('microphone') ?? null;
    post(r);
  },

  async gum(id, constraints, timeoutMs) {
    const t0 = performance.now();
    status(`getUserMedia ${JSON.stringify(constraints)}`);
    const call = navigator.mediaDevices.getUserMedia(constraints).then((s) => s, (e) => e);
    const result = await Promise.race([call, new Promise((r) => setTimeout(() => r('timeout'), timeoutMs))]);
    const ms = performance.now() - t0;
    if (result === 'timeout') {
      post({ type: 'media', id, op: 'gum', outcome: 'timeout', ms });
      call.then((late) => post({ type: 'media', op: 'gum-late', of: id, outcome: late instanceof MediaStream ? 'stream' : late.name, ms: performance.now() - t0 }));
      return;
    }
    if (result instanceof MediaStream) {
      streams.push(result);
      for (const t of result.getTracks()) {
        t.addEventListener('ended', () => post({ type: 'media', op: 'track-ended', kind: t.kind, t: performance.now() }));
        t.addEventListener('mute', () => post({ type: 'media', op: 'track-mute', kind: t.kind, t: performance.now() }));
        t.addEventListener('unmute', () => post({ type: 'media', op: 'track-unmute', kind: t.kind, t: performance.now() }));
      }
      if (result.getVideoTracks().length > 0) {
        const v = document.getElementById('video');
        v.srcObject = result;
        v.play().catch((e) => post({ type: 'media', op: 'play-error', error: `${e.name}: ${e.message}` }));
      }
      status(`stream ${result.getTracks().map((t) => t.label).join(', ')}`);
      post({ type: 'media', id, op: 'gum', outcome: 'stream', ms, tracks: result.getTracks().map(describe) });
    } else {
      status(`error ${result.name}`);
      post({ type: 'media', id, op: 'gum', outcome: 'error', name: result.name, message: result.message, ms });
    }
  },

  // Video frames delivered to the <video> element in one second.
  async frames(id) {
    const v = document.getElementById('video');
    let n = 0;
    let on = true;
    if (v.srcObject && v.requestVideoFrameCallback) {
      const cb = () => { n++; if (on) v.requestVideoFrameCallback(cb); };
      v.requestVideoFrameCallback(cb);
    }
    const track = v.srcObject?.getVideoTracks()[0];
    const before = track?.stats ? { deliveredFrames: track.stats.deliveredFrames, totalFrames: track.stats.totalFrames } : null;
    await new Promise((r) => setTimeout(r, 1000));
    on = false;
    const after = track?.stats ? { deliveredFrames: track.stats.deliveredFrames, totalFrames: track.stats.totalFrames } : null;
    let grabbed = null;
    try {
      if (track && 'ImageCapture' in window) {
        const bitmap = await Promise.race([new window.ImageCapture(track).grabFrame(), new Promise((_, no) => setTimeout(() => no(new Error("no frame in 2 s")), 2000))]);
        grabbed = `${bitmap.width}x${bitmap.height}`;
      }
    } catch (e) {
      grabbed = `${e.name}: ${e.message}`;
    }
    post({ type: 'media', id, op: 'frames', frames: n, width: v.videoWidth, height: v.videoHeight, readyState: v.readyState, paused: v.paused,
      error: v.error?.message ?? null, statsBefore: before, statsAfter: after, grabbed });
  },

  tracks(id) {
    post({ type: 'media', id, op: 'tracks', tracks: streams.flatMap((s) => s.getTracks().map(describe)) });
  },

  stop(id) {
    for (const s of streams) for (const t of s.getTracks()) t.stop();
    streams.length = 0;
    document.getElementById('video').srcObject = null;
    post({ type: 'media', id, op: 'stop' });
  },
};

status('ready');
requestAnimationFrame(() => requestAnimationFrame(() => post({ type: 'ready', t: performance.now(), dark: matchMedia('(prefers-color-scheme: dark)').matches })));
