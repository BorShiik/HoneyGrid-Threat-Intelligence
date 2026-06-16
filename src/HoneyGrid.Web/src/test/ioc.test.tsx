import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { IocPage } from '@/pages/IocPage';

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <IocPage />
    </QueryClientProvider>,
  );
}

// `StixPattern` syntax-highlights the pattern by splitting it into multiple
// <span> tokens inside a single <code> element, so the full string is no
// longer a single text node. Match on the <code> element's combined
// textContent instead of react-testing-library's default single-node matcher.
function byPattern(pattern: string) {
  return (_content: string, element: Element | null) =>
    element?.tagName.toLowerCase() === 'code' && element.textContent === pattern;
}

describe('IocPage (kanał STIX)', () => {
  it('renderuje obiekty z zamockowanego bundla STIX', async () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Wskaźniki IoC (STIX)' })).toBeInTheDocument();

    // pattern from the mocked indicator appears once data loads
    await screen.findByText(byPattern("[ipv4-addr:value = '185.224.128.43']"));
    expect(screen.getByTestId('export-stix')).toBeEnabled();
  });

  it('filtruje obiekty po typie i po treści wzorca', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(byPattern("[ipv4-addr:value = '185.224.128.43']"));

    // Filter to attack-pattern only → IP indicator should disappear
    await user.click(screen.getByRole('button', { name: 'Wzorzec ataku' }));
    await waitFor(() =>
      expect(screen.queryByText(byPattern("[ipv4-addr:value = '185.224.128.43']"))).not.toBeInTheDocument(),
    );
    expect(screen.getByText('Brute Force: Password Guessing')).toBeInTheDocument();

    // Back to all, then text-search narrows to a single pattern
    await user.click(screen.getByRole('button', { name: 'Wszystkie' }));
    const searchBox = screen.getByRole('searchbox');
    await user.type(searchBox, '45.95.147.236');

    await waitFor(() =>
      expect(screen.getByText(byPattern("[ipv4-addr:value = '45.95.147.236']"))).toBeInTheDocument(),
    );
    expect(
      screen.queryByText(byPattern("[ipv4-addr:value = '185.224.128.43']")),
    ).not.toBeInTheDocument();
  });

  it('pokazuje podsumowanie z liczbą wskaźników', async () => {
    renderPage();
    const indicatorsStat = await screen.findByText('Wskaźniki');
    const card = indicatorsStat.closest('div')!;
    // 4 indicators in the mocked bundle
    expect(within(card.parentElement!).getByText('4')).toBeInTheDocument();
  });
});
