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

  it('pokazuje stan połączenia SignalR (domyślnie „Rozłączono")', () => {
    renderApp();
    expect(screen.getByTestId('connection-status')).toHaveTextContent('Rozłączono');
  });

  it('renderuje stronę pulpitu z odznaką „W budowie"', () => {
    renderApp('/');
    expect(screen.getByRole('heading', { name: 'Pulpit' })).toBeInTheDocument();
    expect(screen.getByText(/W budowie — Tydzień 1/)).toBeInTheDocument();
  });

  it('renderuje stronę strumienia i pobiera dane z zamockowanego /api/feed', async () => {
    renderApp('/strumien');
    expect(screen.getByRole('heading', { name: 'Strumień na żywo' })).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: 'Lista ostatnich zdarzeń' });
    expect(list.children.length).toBeGreaterThan(0);
  });
});
