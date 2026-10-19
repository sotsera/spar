import { useEffect, useState } from 'react';
import { backend, connect } from './generated/client.js';

export default function App() {
  const [local, setLocal] = useState(0);
  const [text, setText] = useState('');
  const [counter, setCounter] = useState(0);
  const [greeting, setGreeting] = useState('Connecting…');
  const [mount] = useState(() => crypto.randomUUID());
  const [status, setStatus] = useState('Waiting for C#');
  const [backendRevision, setBackendRevision] = useState(0);
  const [developmentStatus, setDevelopmentStatus] = useState('');
  const [readRevision, setReadRevision] = useState(0);
  const [reading, setReading] = useState(false);

  useEffect(() => {
    let current = true;
    setReading(true);
    backend.greet({ name: 'Spar' }).then(reply => {
      if (current) { setGreeting(reply.greeting); setCounter(reply.counter); setStatus('Connected'); }
    }).catch(error => {
      if (!current) return;
      setStatus(error.code === 'transport_timeout' || error.code === 'timeout'
        ? 'Request timed out. If paused in the debugger, continue, then refresh backend data.'
        : String(error));
    }).finally(() => { if (current) setReading(false); });
    return () => { current = false; };
  }, [readRevision]);

  useEffect(() => {
    let current = true;
    const updated = async (event: { revision: number; contract: string; document: string }) => {
      const identity = await connect();
      if (event.contract !== identity.contract || event.document !== identity.document) return;
      // An update invalidates read data. Never replay counter/delay or other mutations here.
      const reply = await backend.greet({ name: 'Spar' });
      if (!current) return;
      setGreeting(reply.greeting);
      setCounter(reply.counter);
      setStatus('Connected');
      setBackendRevision(event.revision);
      setDevelopmentStatus(`Backend updated (revision ${event.revision})`);
    };
    const diagnostic = async (event: { detail: string; contract: string; document: string }) => {
      const identity = await connect();
      if (current && event.contract === identity.contract && event.document === identity.document) {
        setDevelopmentStatus(`Edit not applied: ${event.detail}`);
      }
    };
    const update = (event: Parameters<typeof updated>[0]) => { updated(event).catch(error => setStatus(String(error))); };
    const report = (event: Parameters<typeof diagnostic>[0]) => { diagnostic(event).catch(error => setStatus(String(error))); };
    import.meta.hot?.on('spar:backend-update', update);
    import.meta.hot?.on('spar:backend-status', report);
    return () => {
      current = false;
      import.meta.hot?.off('spar:backend-update', update);
      import.meta.hot?.off('spar:backend-status', report);
    };
  }, []);

  useEffect(() => {
    let current = true;
    const report = async () => {
      // Report committed DOM after a layout opportunity; desktop pixel verification is a separate check.
      await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
      if (!current) return;
      const identity = await connect();
      if (!current) return;
      const detail = { mount, document: identity.document, contract: identity.contract, local, text, counter,
        backendRevision, developmentStatus,
        greeting, heading: document.querySelector('h1')?.textContent,
        accent: getComputedStyle(document.documentElement).getPropertyValue('--accent').trim(),
        color: getComputedStyle(document.querySelector('button')!).backgroundColor };
      await backend.report({ stage: 'ui', detail: JSON.stringify(detail) });
    };
    if (status === 'Connected') report().catch(error => setStatus(String(error)));
    const beforeUpdate = () => { /* Vite performs its normal updates. */ };
    const afterUpdate = () => { if (status === 'Connected') report().catch(error => setStatus(String(error))); };
    import.meta.hot?.on('vite:beforeUpdate', beforeUpdate);
    import.meta.hot?.on('vite:afterUpdate', afterUpdate);
    return () => {
      current = false;
      import.meta.hot?.off('vite:beforeUpdate', beforeUpdate);
      import.meta.hot?.off('vite:afterUpdate', afterUpdate);
    };
  }, [mount, local, text, counter, greeting, status, backendRevision, developmentStatus]);

  useEffect(() => {
    if (document.querySelector<HTMLMetaElement>('meta[name="spar-probe"]')?.content !== '1') return;
    const probe = (action: string) => {
      if (action === 'seed') {
        setLocal(7);
        setText('Retain this text through HMR');
        backend.counter({ delta: 2 }).then(reply => setCounter(reply.value)).catch(error => setStatus(String(error)));
      } else if (action === 'delay') {
        backend.delay({ milliseconds: 12000, ignoreCancellation: true }, { timeoutMs: 30000 })
          .catch(() => { /* Retirement rejects delivery; the native owner still waits for actual completion. */ });
      } else if (action.startsWith('stale-contract:')) {
        connect().then(identity => {
          const channel = (window as Window & { chrome?: { webview?: { postMessage(value: unknown): void } } }).chrome?.webview;
          channel?.postMessage({ version: 1, kind: 'call', id: 1, contract: action.slice('stale-contract:'.length),
            client: crypto.randomUUID().replaceAll('-', ''), document: identity.document,
            operation: 'counter', payload: { delta: 777 }, timeoutMs: 5000 });
        }).catch(error => setStatus(String(error)));
      } else if (action === 'close') window.close();
    };
    import.meta.hot?.on('spar:sample-probe', probe);
    return () => { import.meta.hot?.off('spar:sample-probe', probe); };
  }, []);

  return <main>
    <span className="mark">SPAR</span>
    <h1>A live development loop.</h1>
    <p>Edit this component or its CSS. Vite and React update this native window.</p>
    <div className="actions">
      <button id="local" onClick={() => setLocal(value => value + 1)}>Local counter: {local}</button>
      <button id="backend" onClick={async () => {
        try { setCounter((await backend.counter({ delta: 1 })).value); }
        catch (error) { setStatus(String(error)); }
      }}>Backend counter: {counter}</button>
    </div>
    <label>Local state <input value={text} onChange={event => setText(event.target.value)} placeholder="Type, then edit the component" /></label>
    <p id="greeting">{greeting}</p>
    <p id="status" role="status">{status}</p>
    <button id="refresh-backend" disabled={reading} onClick={() => setReadRevision(value => value + 1)}>
      {reading ? 'Reading backend data…' : 'Refresh backend data'}
    </button>
    {developmentStatus && <p id="development-status" role="status">{developmentStatus}</p>}
  </main>;
}
