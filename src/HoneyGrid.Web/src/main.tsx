import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import './i18n'; // initialise i18next (pl default, en/ru) before first render
import './index.css';

async function enableMocking() {
  // Włączamy mocki tylko jeśli jawnie powiemy VITE_USE_MOCKS=true
  if (import.meta.env.VITE_USE_MOCKS !== 'true') return;
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
