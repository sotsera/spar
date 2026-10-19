import { useEffect, useState } from 'react';
import { backend, connect } from './generated/client.js';

export default function App() {
  const [local, setLocal] = useState(0);
  const [text, setText] = useState('');
  const [counter, setCounter] = useState(0);
  const [greeting, setGreeting] = useState('Connecting…');
  const [mount] = useState(() => crypto.randomUUID());
  const [status, setStatus] = useState('Waiting for C#');

  useEffect(() => {
    let current = true;
    backend.greet({ name: 'Spar' }).then(reply => {
      if (current) { setGreeting(reply.greeting); setCounter(reply.counter); setStatus('Connected'); }
    }).catch(error => { if (current) setStatus(String(error)); });
    return () => { current = false; };
  }, []);

  useEffect(() => {
    const report = async () => {
      // Report committed DOM after a layout opportunity; desktop pixel verification is a separate check.
      await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
      const identity = await connect();
      const detail = { mount, document: identity.document, contract: identity.contract, local, text, counter,
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
      import.meta.hot?.off('vite:beforeUpdate', beforeUpdate);
      import.meta.hot?.off('vite:afterUpdate', afterUpdate);
    };
  }, [mount, local, text, counter, greeting, status]);

  useEffect(() => {
    if (document.querySelector<HTMLMetaElement>('meta[name="spar-probe"]')?.content !== '1') return;
    const probe = (action: string) => {
      if (action === 'seed') {
        setLocal(7);
        setText('Retain this text through HMR');
        backend.counter({ delta: 2 }).then(reply => setCounter(reply.value)).catch(error => setStatus(String(error)));
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
  </main>;
}
