import { PlaceholderPage } from '@/components/PlaceholderPage';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useFeed } from '@/api/queries';
import type { HoneypotEvent } from '@/types/api';

const EVENT_TYPE_LABELS: Record<HoneypotEvent['eventType'], string> = {
  'login.failed': 'Nieudane logowanie',
  'login.success': 'Udane logowanie',
  command: 'Komenda',
  'http.request': 'Żądanie HTTP',
  connect: 'Połączenie',
};

function eventDetails(event: HoneypotEvent): string {
  if (event.credentials) return `${event.credentials.username} / ${event.credentials.password}`;
  if (event.command) return event.command;
  if (event.http) return `${event.http.method} ${event.http.path}`;
  return '—';
}

function FeedRow({ event }: { event: HoneypotEvent }) {
  return (
    <li className="flex items-center gap-3 border-b px-4 py-2.5 text-sm last:border-b-0">
      <span className="w-16 shrink-0 font-mono text-xs text-muted-foreground">
        {new Date(event.timestamp).toLocaleTimeString('pl-PL')}
      </span>
      <Badge variant="outline" className="w-12 shrink-0 justify-center font-mono uppercase">
        {event.sensorType}
      </Badge>
      <span className="w-36 shrink-0 font-mono">{event.attackerIp}</span>
      <span className="w-40 shrink-0 text-muted-foreground">
        {event.geo?.countryName ?? 'Nieznany kraj'}
      </span>
      <span className="w-40 shrink-0">{EVENT_TYPE_LABELS[event.eventType]}</span>
      <span className="truncate font-mono text-xs text-muted-foreground">
        {eventDetails(event)}
      </span>
    </li>
  );
}

export function LiveFeedPage() {
  const { data, isPending, isError } = useFeed({ take: 25 });

  return (
    <PlaceholderPage
      title="Strumień na żywo"
      description="Zdarzenia z sensorów w czasie rzeczywistym (SignalR). Poniżej podgląd danych z zamockowanego punktu końcowego /api/feed — wirtualizacja listy i strumień na żywo pojawią się w Tygodniu 2."
      week={2}
    >
      <Card>
        <CardContent className="p-0">
          {isPending && (
            <div className="space-y-2 p-4">
              {Array.from({ length: 8 }, (_, i) => (
                <Skeleton key={i} className="h-8 w-full" />
              ))}
            </div>
          )}
          {isError && (
            <p className="p-4 text-sm text-destructive">
              Nie udało się pobrać strumienia zdarzeń. Spróbuj ponownie później.
            </p>
          )}
          {data && data.length === 0 && (
            <p className="p-4 text-sm text-muted-foreground">
              Brak zdarzeń do wyświetlenia — sensory milczą.
            </p>
          )}
          {data && data.length > 0 && (
            <ul aria-label="Lista ostatnich zdarzeń">
              {data.map((event) => (
                <FeedRow key={event.id} event={event} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </PlaceholderPage>
  );
}
