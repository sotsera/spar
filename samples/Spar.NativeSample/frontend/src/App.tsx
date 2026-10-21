import { useEffect, useState } from 'react';
import { backend, connect } from './generated/client.js';

export default function App() {
  const [local, setLocal] = useState(0);
  const [text, setText] = useState('');
  const [counter, setCounter] = useState(0);
  const [greeting, setGreeting] = useState('Connecting…');
  const [status, setStatus] = useState('Waiting for C#');
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
    if (!import.meta.env.DEV) return;
    let current = true;
    const updated = async (event: { revision: number; contract: string; document: string }) => {
      const identity = await connect();
      if (event.contract !== identity.contract || event.document !== identity.document) return;
      // An update invalidates read data. Never replay counter or other mutations here.
      const reply = await backend.greet({ name: 'Spar' });
      if (!current) return;
      setGreeting(reply.greeting);
      setCounter(reply.counter);
      setStatus('Connected');
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

  return <main>
    <span className="mark">SPAR</span>
    <h1>{import.meta.env.DEV ? 'A live development loop.' : 'Your desktop. Your app.'}</h1>
    <p>{import.meta.env.DEV ? 'Edit this component or its CSS. Vite and React update this native window.'
      : 'A local web interface, connected to C#.'}</p>
    <div className="actions">
      <button id="local" onClick={() => setLocal(value => value + 1)}>Local counter: {local}</button>
      <button id="backend" onClick={async () => {
        try { setCounter((await backend.counter({ delta: 1 })).value); }
        catch (error) { setStatus(String(error)); }
      }}>Backend counter: {counter}</button>
    </div>
    <label>Local state <input value={text} onChange={event => setText(event.target.value)}
      placeholder={import.meta.env.DEV ? 'Type, then edit the component' : 'Type something'} /></label>
    <p id="greeting">{greeting}</p>
    <p id="status" role="status">{status}</p>
    <button id="refresh-backend" disabled={reading} onClick={() => setReadRevision(value => value + 1)}>
      {reading ? 'Reading backend data…' : 'Refresh backend data'}
    </button>
    {import.meta.env.DEV && developmentStatus && <p id="development-status" role="status">{developmentStatus}</p>}
  </main>;
}
