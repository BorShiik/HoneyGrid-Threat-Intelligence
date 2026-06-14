import { useMemo, useState } from 'react';
import { motion } from 'framer-motion';
import { Pause, Play, Radio } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { EVENT_TYPE_LABELS, SENSOR_LABELS, SEVERITY_BG, eventDetails, eventSeverity } from '@/lib/format';
import type { HoneypotEvent, SensorType } from '@/types/api';

const SENSOR_FILTERS: readonly (SensorType | 'all')[] = ['all', 'ssh', 'web', 'rdp'];

function FeedRow({ event }: { event: HoneypotEvent }) {
  const severity = eventSeverity(event);
  return (
    <motion.li
      layout
      initial={{ opacity: 0, backgroundColor: 'rgba(120,120,120,0.12)' }}
      animate={{ opacity: 1, backgroundColor: 'rgba(0,0,0,0)' }}
      transition={{ duration: 0.6 }}
      className="flex items-center gap-3 border-b px-4 py-2 text-sm last:border-b-0"
    >
      <span aria-hidden className={cn('h-2 w-2 shrink-0 rounded-full', SEVERITY_BG[severity])} />
      <span className="w-20 shrink-0 font-mono text-xs text-muted-foreground">
        {new Date(event.timestamp).toLocaleTimeString('pl-PL')}
      </span>
      <Badge variant="outline" className="w-12 shrink-0 justify-center font-mono">
        {SENSOR_LABELS[event.sensorType]}
      </Badge>
      <span className="w-36 shrink-0 font-mono">{event.attackerIp}</span>
      <span className="hidden w-36 shrink-0 text-muted-foreground lg:inline">
        {event.geo?.countryName ?? 'Nieznany kraj'}
      </span>
      <span className="w-40 shrink-0">{EVENT_TYPE_LABELS[event.eventType]}</span>
      <span className="truncate font-mono text-xs text-muted-foreground">{eventDetails(event)}</span>
    </motion.li>
  );
}

export function LiveFeedPage() {
  const [paused, setPaused] = useState(false);
  const [sensor, setSensor] = useState<SensorType | 'all'>('all');
  const { events, simulated } = useLiveAttacks({ bufferSize: 200, enabled: !paused });

  const visible = useMemo(
    () => (sensor === 'all' ? events : events.filter((e) => e.sensorType === sensor)),
    [events, sensor],
  );

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
            Strumień na żywo
            <Radio className={cn('size-5', simulated ? 'text-severity-medium' : 'text-status-online')} />
          </h2>
          <p className="mt-1 max-w-2xl text-muted-foreground">
            Zdarzenia z sensorów honeypot w czasie rzeczywistym przez SignalR.
            {simulated && ' (tryb demonstracyjny — symulowany strumień, brak backendu)'}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {SENSOR_FILTERS.map((s) => (
            <Button
              key={s}
              size="sm"
              variant={sensor === s ? 'default' : 'outline'}
              onClick={() => setSensor(s)}
              aria-pressed={sensor === s}
            >
              {s === 'all' ? 'Wszystkie' : SENSOR_LABELS[s as SensorType]}
            </Button>
          ))}
          <Button size="sm" variant="secondary" onClick={() => setPaused((p) => !p)}>
            {paused ? <Play /> : <Pause />}
            {paused ? 'Wznów' : 'Wstrzymaj'}
          </Button>
        </div>
      </div>

      <div className="flex items-center gap-4 text-sm text-muted-foreground">
        <span>
          Zdarzeń w buforze: <span className="font-mono text-foreground">{visible.length}</span>
        </span>
        <span>
          Status:{' '}
          <span className={cn('font-medium', paused ? 'text-severity-medium' : 'text-status-online')}>
            {paused ? 'wstrzymany' : 'odbiór na żywo'}
          </span>
        </span>
      </div>

      <Card>
        <CardContent className="p-0">
          {visible.length === 0 ? (
            <p className="p-8 text-center text-sm text-muted-foreground">
              {paused ? 'Strumień wstrzymany.' : 'Oczekiwanie na zdarzenia z sensorów…'}
            </p>
          ) : (
            <ul aria-label="Strumień zdarzeń na żywo" className="max-h-[70vh] overflow-y-auto">
              {visible.map((event) => (
                <FeedRow key={event.id} event={event} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </motion.section>
  );
}
