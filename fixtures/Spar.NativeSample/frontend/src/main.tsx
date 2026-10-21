import { createRoot } from 'react-dom/client';
import { connect } from './generated/client.js';
import App from './App';
import './styles.css';

const root = createRoot(document.getElementById('root')!);
try {
  const credential = document.querySelector<HTMLMetaElement>('meta[name="spar-client-credential"]')?.content;
  await connect(credential);
  root.render(<App />);
} catch (error) {
  root.render(<main><h1>Unable to connect</h1><p>{String(error)}</p></main>);
}
