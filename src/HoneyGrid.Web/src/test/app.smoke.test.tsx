import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { App } from '@/App';

function renderApp(initialPath = '/') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <App />
    </MemoryRouter>,
  );
}

describe('Powłoka aplikacji (smoke test)', () => {
  it('renderuje nazwę projektu HoneyGrid', () => {
    renderApp();
    expect(screen.getAllByText(/Honey/).length).toBeGreaterThan(0);
    expect(screen.getByText(/HoneyGrid — centrum operacji bezpieczeństwa/)).toBeInTheDocument();
  });

  it('renderuje polskie pozycje nawigacji', () => {
    renderApp();
    const nav = screen.getByRole('navigation', { name: 'Nawigacja główna' });
    expect(nav).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Pulpit/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Mapa ataków/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Strumień na żywo/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Aktorzy zagrożeń/ })).toBeInTheDocument();
  });

  it('renderuje stronę pulpitu (zaimplementowaną, bez odznaki „W budowie")', () => {
    renderApp('/');
    expect(screen.getByRole('heading', { name: 'Pulpit' })).toBeInTheDocument();
    expect(screen.queryByText(/W budowie/)).not.toBeInTheDocument();
  });

  it('przechodzi w stan „Połączono" po starcie strumienia na żywo', async () => {
    renderApp('/');
    // The live-attacks hook drives the global connection state via the
    // client-side simulator in dev/test mode (no WebSocket backend).
    expect(await screen.findByText('Połączono')).toBeInTheDocument();
  });

  it('renderuje stronę strumienia na żywo z listą zdarzeń', async () => {
    renderApp('/strumien');
    expect(screen.getByRole('heading', { name: /Strumień na żywo/ })).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: 'Strumień zdarzeń na żywo' });
    expect(list.children.length).toBeGreaterThan(0);
  });
});
