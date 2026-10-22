const assert = require('node:assert/strict');
const test = require('node:test');
const { createPresentation } = require('./presentation.cjs');

test('withdrawn readiness cannot be restored by a metadata update or stale generation', () => {
  const present = createPresentation();
  const send = (kind, code, generation = 1) => present({ kind, code, generation })?.text;
  assert.equal(send('application-ready'), 'Spar: ready');
  assert.equal(send('backend-updated'), 'Spar: updated');
  for (const kind of ['application-not-ready', 'bridge-connected']) {
    assert.equal(send(kind), 'Spar: waiting for application readiness');
    assert.equal(send('backend-updated'), 'Spar: waiting for application readiness');
    assert.equal(send('application-ready'), 'Spar: ready');
  }
  assert.equal(send('session-phase', 'building', 2), 'Spar: building');
  assert.equal(send('application-ready', undefined, 1), undefined);
  assert.equal(send('backend-updated', undefined, 2), 'Spar: building');
});

test('paused startup, failed rebuild and correction remain distinct from ready', () => {
  const present = createPresentation();
  const send = (kind, code) => present({ kind, code, generation: 1 }).text;
  assert.equal(send('session-phase', 'waiting-debugger'), 'Spar: waiting for debugger');
  assert.equal(send('debugger-state', 'paused'), 'Spar: debugger paused');
  assert.equal(send('bridge-connected'), 'Spar: debugger paused');
  assert.equal(send('debugger-state', 'running'), 'Spar: waiting for application readiness');
  assert.equal(send('application-ready'), 'Spar: ready');
  assert.equal(send('backend-restarting'), 'Spar: retiring backend');
  assert.equal(send('backend-stopped'), 'Spar: build failed');
  assert.equal(send('backend-updated'), 'Spar: build failed');
  assert.equal(send('session-phase', 'building'), 'Spar: building');
  assert.equal(send('application-ready'), 'Spar: ready');
});

test('display diagnostics are bounded and cannot inject terminal controls', () => {
  const present = createPresentation();
  const value = present({ kind: 'session-failed', detail: '\u001b[2J\n' + 'x'.repeat(5000) });
  assert.equal(value.text, 'Spar: failed');
  assert.equal(value.message.length, 4096);
  assert(!/[\x00-\x1f\x7f]/.test(value.message));
});
