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
  it('renderuje markę HoneyGrid', () => {
    renderApp();
    expect(screen.getAllByText(/Honey/).length).toBeGreaterThan(0);
  });

  it('renderuje główną nawigację z modułami', () => {
    renderApp();
    const nav = screen.getByRole('navigation', { name: 'Nawigacja główna' });
    expect(nav).toBeInTheDocument();
    // 8 modules in the sidebar (language-agnostic: count links, not labels).
    expect(screen.getAllByRole('link').length).toBeGreaterThanOrEqual(6);
  });

  it('renderuje przełącznik języka i status połączenia', () => {
    renderApp('/');
    expect(screen.getByRole('button', { name: 'Language' })).toBeInTheDocument();
    expect(screen.getByTestId('connection-status')).toBeInTheDocument();
  });
});
