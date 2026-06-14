import { useMemo } from 'react';
import { motion } from 'framer-motion';
import { Activity, KeyRound, ServerCog, Users } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { useStatsOverview } from '@/api/queries';
import { useLiveAttacks } from '@/lib/liveAttacks';
import {
  EVENT_TYPE_LABELS,
  SENSOR_LABELS,
  SEVERITY_BG,
  eventDetails,
  eventSeverity,
  formatCompact,
  formatInt,
} from '@/lib/format';
import type { HoneypotEventType, SensorType, StatsOverview } from '@/types/api';

function Kpi({
  icon: Icon,
  label,
  value,
  hint,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string;
  hint?: string;
}) {
  return (
    <Card>
      <CardContent className="flex items-center gap-4 py-5">
        <div className="flex size-11 items-center justify-center rounded-lg bg-accent text-accent-foreground">
          <Icon className="size-5" />
        </div>
        <div className="min-w-0">
          <div className="text-2xl font-bold tabular-nums">{value}</div>
          <div className="truncate text-xs text-muted-foreground">{label}</div>
          {hint && <div className="truncate text-xs text-muted-foreground/70">{hint}</div>}
        </div>
      </CardContent>
    </Card>
  );
}

const SENSOR_BAR: Record<SensorType, string> = {
  ssh: 'bg-severity-critical',
  web: 'bg-severity-medium',
  rdp: 'bg-severity-info',
};

function Breakdown({
  title,
  rows,
}: {
  title: string;
  rows: { label: string; value: number; className: string }[];
}) {
  const total = Math.max(
    1,
    rows.reduce((s, r) => s + r.value, 0),
  );
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2.5">
        {rows.map((r) => (
          <div key={r.label} className="space-y-1">
            <div className="flex items-center justify-between text-sm">
              <span>{r.label}</span>
              <span className="font-mono tabular-nums text-muted-foreground">{formatInt(r.value)}</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-muted">
              <div
                className={cn('h-full rounded-full', r.className)}
                style={{ width: `${(r.value / total) * 100}%` }}
              />
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

export function DashboardPage() {
  const { data, isPending, isError } = useStatsOverview();
  const { events } = useLiveAttacks({ bufferSize: 12 });

  const sensorRows = useMemo(() => {
    if (!data) return [];
    return (Object.keys(data.eventsBySensorType) as SensorType[]).map((s) => ({
      label: SENSOR_LABELS[s],
      value: data.eventsBySensorType[s],
      className: SENSOR_BAR[s],
    }));
  }, [data]);

  const typeRows = useMemo(() => {
    if (!data) return [];
    return (Object.keys(data.eventsByType) as HoneypotEventType[]).map((t) => ({
      label: EVENT_TYPE_LABELS[t],
      value: data.eventsByType[t],
      className: 'bg-primary',
    }));
  }, [data]);

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div>
        <h2 className="text-2xl font-bold tracking-tight">Pulpit</h2>
        <p className="mt-1 max-w-2xl text-muted-foreground">
          Przegląd aktywności platformy: wolumen zdarzeń, unikalni atakujący, aktywne sesje oraz
          rozkład ruchu według sensorów i typów zdarzeń.
        </p>
      </div>

      {isError && (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">
          Nie udało się pobrać statystyk. Spróbuj ponownie później.
        </p>
      )}

      {isPending ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-24 w-full" />
          ))}
        </div>
      ) : data ? (
        <KpiRow data={data} />
      ) : null}

      <div className="grid gap-4 lg:grid-cols-2">
        {data && <Breakdown title="Zdarzenia wg sensora" rows={sensorRows} />}
        {data && <Breakdown title="Zdarzenia wg typu" rows={typeRows} />}
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr_1.4fr]">
        {data && (
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-base">Najczęstsze kraje pochodzenia</CardTitle>
            </CardHeader>
            <CardContent className="space-y-1.5">
              {data.topCountries.map((c, i) => (
                <div key={c.country} className="flex items-center gap-2 text-sm">
                  <span className="w-5 text-right font-mono text-xs text-muted-foreground">
                    {i + 1}
                  </span>
                  <span className="w-8 font-mono text-xs text-muted-foreground">{c.country}</span>
                  <span className="flex-1 truncate">{c.countryName}</span>
                  <span className="font-mono tabular-nums">{formatInt(c.count)}</span>
                </div>
              ))}
            </CardContent>
          </Card>
        )}

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-base">Ostatnie zdarzenia</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <ul>
              {events.length === 0 && (
                <li className="px-4 py-6 text-center text-sm text-muted-foreground">
                  Oczekiwanie na zdarzenia…
                </li>
              )}
              {events.slice(0, 8).map((e) => (
                <li
                  key={e.id}
                  className="flex items-center gap-2 border-b px-4 py-2 text-sm last:border-b-0"
                >
                  <span
                    aria-hidden
                    className={cn('h-2 w-2 shrink-0 rounded-full', SEVERITY_BG[eventSeverity(e)])}
                  />
                  <span className="w-28 shrink-0 font-mono text-xs">{e.attackerIp}</span>
                  <span className="w-32 shrink-0 text-muted-foreground">
                    {EVENT_TYPE_LABELS[e.eventType]}
                  </span>
                  <span className="truncate font-mono text-xs text-muted-foreground">
                    {eventDetails(e)}
                  </span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      </div>
    </motion.section>
  );
}

function KpiRow({ data }: { data: StatsOverview }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <Kpi
        icon={Activity}
        label="Zdarzenia (24 h)"
        value={formatCompact(data.eventsLast24h)}
        hint={`${formatInt(data.totalEvents)} łącznie`}
      />
      <Kpi icon={Users} label="Unikalni atakujący" value={formatCompact(data.uniqueAttackers)} />
      <Kpi icon={ServerCog} label="Aktywne sesje" value={formatInt(data.activeSessions)} />
      <Kpi
        icon={KeyRound}
        label="Top kraj"
        value={data.topCountries[0]?.country ?? '—'}
        hint={data.topCountries[0]?.countryName}
      />
    </div>
  );
}
