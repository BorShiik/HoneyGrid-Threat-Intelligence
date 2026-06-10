import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import './index.css';

async function enableMocking() {
  if (!import.meta.env.DEV) return;
  const { worker } = await import('./mocks/browser');
  await worker.start({ onUnhandledRequest: 'bypass' });
}

/*
 * NOTE: React.StrictMode is intentionally OFF.
 * StrictMode double-invokes effects in dev, which breaks xterm.js terminal
 * geometry (fit addon measurements) in the upcoming Session Replay feature.
 * Do not re-enable it without re-testing the terminal component.
 */
void enableMocking().then(() => {
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <BrowserRouter>
      <App />
    </BrowserRouter>,
  );
});
