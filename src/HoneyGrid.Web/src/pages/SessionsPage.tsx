import { lazy, Suspense, useState } from 'react';
import { motion } from 'framer-motion';
import { MonitorPlay, Terminal as TerminalIcon } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { formatClock } from '@/lib/replay';
import { useSessions } from '@/api/queries';
import type { SessionSummary } from '@/types/api';

// Code-split the heavy xterm.js terminal — only loaded when a session is opened.
const SessionReplayPlayer = lazy(() => import('@/components/replay/SessionReplayPlayer'));

function SessionRow({
  session,
  selected,
  onSelect,
}: {
  session: SessionSummary;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={selected}
      className={cn(
        'flex w-full items-center gap-3 border-b px-4 py-3 text-left text-sm transition-colors last:border-b-0 hover:bg-muted/50',
        selected && 'bg-muted/60',
      )}
    >
      <TerminalIcon
        className={cn('size-4 shrink-0', session.hasTty ? 'text-primary' : 'text-muted-foreground')}
      />
      <span className="w-24 shrink-0 font-mono text-xs">{session.sessionId}</span>
      <span className="w-36 shrink-0 font-mono">{session.attackerIp}</span>
      <span className="hidden w-32 shrink-0 text-muted-foreground sm:inline">
        {session.countryName}
      </span>
      <span className="hidden w-28 shrink-0 font-mono text-xs text-muted-foreground md:inline">
        {session.sensorId}
      </span>
      <span className="ml-auto shrink-0 font-mono text-xs text-muted-foreground tabular-nums">
        {session.hasTty ? `${session.commandCount} kmd · ${formatClock(session.durationMs)}` : '—'}
      </span>
      {!session.hasTty && (
        <Badge variant="outline" className="shrink-0 text-xs">
          bez TTY
        </Badge>
      )}
    </button>
  );
}

export function SessionsPage() {
  const { data, isPending, isError } = useSessions();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const selectedSession = data?.find((s) => s.sessionId === selectedId) ?? null;

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div>
        <h2 className="text-2xl font-bold tracking-tight">Sesje / Odtwarzanie</h2>
        <p className="mt-1 max-w-2xl text-muted-foreground">
          Lista przechwyconych sesji SSH. Wybierz sesję, aby odtworzyć nagranie terminala (xterm.js)
          — komenda po komendzie, dokładnie tak, jak widział to atakujący.
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
        {/* Session list */}
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Przechwycone sesje</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            {isPending && (
              <div className="space-y-2 p-4">
                {Array.from({ length: 4 }, (_, i) => (
                  <Skeleton key={i} className="h-10 w-full" />
                ))}
              </div>
            )}
            {isError && (
              <p className="p-4 text-sm text-destructive">
                Nie udało się pobrać listy sesji. Spróbuj ponownie później.
              </p>
            )}
            {data && data.length === 0 && (
              <p className="p-4 text-sm text-muted-foreground">Brak przechwyconych sesji.</p>
            )}
            {data && data.length > 0 && (
              <div aria-label="Lista sesji">
                {data.map((s) => (
                  <SessionRow
                    key={s.sessionId}
                    session={s}
                    selected={s.sessionId === selectedId}
                    onSelect={() => setSelectedId(s.sessionId)}
                  />
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* Replay player */}
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <MonitorPlay className="size-4" />
              Odtwarzanie sesji
              {selectedSession && (
                <Badge variant="secondary" className="ml-2 font-mono">
                  {selectedSession.sessionId}
                </Badge>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {!selectedId && (
              <p className="rounded-lg border border-dashed bg-muted/30 p-6 text-center text-sm text-muted-foreground">
                Wybierz sesję z listy, aby rozpocząć odtwarzanie.
              </p>
            )}
            {selectedId && (
              <Suspense fallback={<Skeleton className="h-80 w-full" />}>
                <SessionReplayPlayer key={selectedId} sessionId={selectedId} />
              </Suspense>
            )}
          </CardContent>
        </Card>
      </div>
    </motion.section>
  );
}
