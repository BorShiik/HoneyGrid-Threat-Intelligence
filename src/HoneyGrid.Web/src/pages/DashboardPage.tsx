import { PlaceholderPage } from '@/components/PlaceholderPage';

export function DashboardPage() {
  return (
    <PlaceholderPage
      title="Pulpit"
      description="Przegląd najważniejszych wskaźników: liczba zdarzeń, unikalni atakujący, aktywne sesje, najczęściej atakowane sensory oraz trendy z ostatnich 24 godzin."
      week={1}
    />
  );
}
