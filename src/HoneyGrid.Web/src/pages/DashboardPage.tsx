import { useMemo } from 'react';
import { motion } from 'framer-motion';
import { Activity, Globe2, KeyRound, ServerCog, Shield, TrendingUp, Users } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { AreaChart, Area, ResponsiveContainer } from 'recharts';
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
import type { HoneypotEventType, SensorType } from '@/types/api';

/* ── Mock sparkline data ── */
const generateSparkline = (base: number, variance: number) =>
  Array.from({ length: 24 }, (_, i) => ({
    h: i,
    v: Math.max(0, Math.floor(base + (Math.random() - 0.5) * variance)),
  }));

/* ── Animated KPI Card ── */
function KpiCard({
  icon: Icon,
  label,
  value,
  hint,
  color,
  sparkData,
  delay = 0,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string;
  hint?: string;
  color: string;
  sparkData?: { h: number; v: number }[];
  delay?: number;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      whileHover={{ y: -2, transition: { duration: 0.2 } }}
      className="group relative overflow-hidden rounded-xl glass-strong p-4 cursor-default"
    >
      {/* Subtle gradient line at top */}
      <div className={cn('absolute top-0 left-0 right-0 h-[2px]', color)} />

      <div className="flex items-start justify-between gap-3">
        <div className="space-y-1.5">
          <div className={cn('flex h-9 w-9 items-center justify-center rounded-lg', color.replace('bg-gradient-to-r', 'bg').split(' ')[0] + '/10')}>
            <Icon className={cn('h-4.5 w-4.5', color.includes('amber') ? 'text-amber-500' : color.includes('emerald') ? 'text-emerald-500' : color.includes('rose') ? 'text-rose-500' : 'text-blue-500')} />
          </div>
          <div className="text-2xl font-bold tracking-tight tabular-nums text-white">{value}</div>
          <div className="text-xs text-zinc-400">{label}</div>
          {hint && <div className="text-[11px] text-zinc-600">{hint}</div>}
        </div>
        {sparkData && (
          <div className="w-20 h-12 opacity-60 group-hover:opacity-100 transition-opacity">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={sparkData}>
                <defs>
                  <linearGradient id="sparkGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#f59e0b" stopOpacity={0.3} />
                    <stop offset="100%" stopColor="#f59e0b" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <Area
                  type="monotone"
                  dataKey="v"
                  stroke="#f59e0b"
                  strokeWidth={1.5}
                  fill="url(#sparkGrad)"
                  dot={false}
                  isAnimationActive={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>
    </motion.div>
  );
}

/* ── Sensor Breakdown (heatmap-style bars) ── */
const SENSOR_COLORS: Record<SensorType, { bar: string; text: string }> = {
  ssh: { bar: 'bg-rose-500', text: 'text-rose-400' },
  web: { bar: 'bg-amber-500', text: 'text-amber-400' },
  rdp: { bar: 'bg-blue-500', text: 'text-blue-400' },
};

function SensorBreakdown({ data }: { data: Record<SensorType, number> }) {
  const { t } = useTranslation();
  const total = Math.max(1, Object.values(data).reduce((s, v) => s + v, 0));
  const sensors = (Object.keys(data) as SensorType[]).sort((a, b) => data[b] - data[a]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.3, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-xl glass-strong p-4 space-y-3"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-zinc-200">{t('dashboard.eventsBySensor')}</h3>
        <span className="text-xs text-zinc-500 font-mono">{formatInt(total)} {t('common.total')}</span>
      </div>

      {/* Combined heatmap bar */}
      <div className="flex h-3 rounded-full overflow-hidden bg-zinc-800/50">
        {sensors.map((s) => (
          <motion.div
            key={s}
            initial={{ width: 0 }}
            animate={{ width: `${(data[s] / total) * 100}%` }}
            transition={{ delay: 0.5, duration: 0.8, ease: 'easeOut' }}
            className={cn(SENSOR_COLORS[s].bar, 'h-full')}
          />
        ))}
      </div>

      <div className="space-y-2">
        {sensors.map((s) => (
          <div key={s} className="flex items-center justify-between text-sm">
            <div className="flex items-center gap-2">
              <span className={cn('h-2.5 w-2.5 rounded-sm', SENSOR_COLORS[s].bar)} />
              <span className="text-zinc-300 font-medium">{SENSOR_LABELS[s]}</span>
            </div>
            <div className="flex items-center gap-3">
              <span className="font-mono text-xs text-zinc-400 tabular-nums">
                {((data[s] / total) * 100).toFixed(1)}%
              </span>
              <span className={cn('font-mono text-xs tabular-nums font-medium', SENSOR_COLORS[s].text)}>
                {formatInt(data[s])}
              </span>
            </div>
          </div>
        ))}
      </div>
    </motion.div>
  );
}

/* ── Event Type Breakdown ── */
function EventTypeBreakdown({ data }: { data: Record<HoneypotEventType, number> }) {
  const { t } = useTranslation();
  const total = Math.max(1, Object.values(data).reduce((s, v) => s + v, 0));
  const types = (Object.keys(data) as HoneypotEventType[]).sort((a, b) => data[b] - data[a]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.4, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-xl glass-strong p-4 space-y-3"
    >
      <h3 className="text-sm font-semibold text-zinc-200">{t('dashboard.eventsByType')}</h3>
      <div className="space-y-2">
        {types.slice(0, 5).map((typeKey) => (
          <div key={typeKey} className="space-y-1">
            <div className="flex items-center justify-between text-sm">
              <span className="text-zinc-400">
                {t(`eventType.${typeKey.replace('.', '') === 'loginfailed' ? 'loginFailed' : typeKey.replace('.', '') === 'loginsuccess' ? 'loginSuccess' : typeKey.replace('.', '') === 'httprequest' ? 'httpRequest' : typeKey}` as any, EVENT_TYPE_LABELS[typeKey])}
              </span>
              <span className="font-mono text-xs text-zinc-400 tabular-nums">{formatInt(data[typeKey])}</span>
            </div>
            <div className="h-1.5 rounded-full bg-zinc-800/50 overflow-hidden">
              <motion.div
                initial={{ width: 0 }}
                animate={{ width: `${(data[typeKey] / total) * 100}%` }}
                transition={{ delay: 0.6, duration: 0.7, ease: 'easeOut' }}
                className="h-full rounded-full bg-gradient-to-r from-amber-500 to-amber-500/50"
              />
            </div>
          </div>
        ))}
      </div>
    </motion.div>
  );
}

/* ── Compact Event Table ── */
function RecentEventsTable({ events }: { events: ReturnType<typeof useLiveAttacks>['events'] }) {
  const { t } = useTranslation();
  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.5, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-xl glass-strong overflow-hidden"
    >
      <div className="flex items-center justify-between px-4 py-3 border-b border-white/5">
        <h3 className="text-sm font-semibold text-zinc-200">{t('dashboard.recentEvents')}</h3>
        <span className="text-xs text-zinc-500">{t('liveFeed.live')}</span>
      </div>
      <div className="max-h-[320px] overflow-y-auto">
        {events.length === 0 ? (
          <div className="p-6 text-center text-sm text-zinc-500">{t('common.waiting')}</div>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-zinc-500">
                <th className="px-4 py-2 font-medium">Время</th>
                <th className="px-4 py-2 font-medium">Протокол</th>
                <th className="px-4 py-2 font-medium">Источник</th>
                <th className="px-4 py-2 font-medium">Тип</th>
                <th className="px-4 py-2 font-medium">Payload</th>
              </tr>
            </thead>
            <tbody>
              {events.slice(0, 10).map((e, i) => {
                const sev = eventSeverity(e);
                return (
                  <motion.tr
                    key={e.id}
                    initial={{ opacity: 0, x: -8 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: i * 0.03 }}
                    className="border-t border-white/[0.03] hover:bg-white/[0.03] transition-colors"
                  >
                    <td className="px-4 py-2 font-mono text-xs text-zinc-400">
                      {new Date(e.timestamp).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                    </td>
                    <td className="px-4 py-2">
                      <span className={cn(
                        'inline-flex items-center rounded-md px-1.5 py-0.5 font-mono text-[10px] font-semibold ring-1 ring-inset',
                        e.sensorType === 'ssh' && 'bg-rose-500/10 text-rose-400 ring-rose-500/20',
                        e.sensorType === 'web' && 'bg-amber-500/10 text-amber-400 ring-amber-500/20',
                        e.sensorType === 'rdp' && 'bg-blue-500/10 text-blue-400 ring-blue-500/20',
                      )}>
                        {SENSOR_LABELS[e.sensorType]}
                      </span>
                    </td>
                    <td className="px-4 py-2 font-mono text-xs text-zinc-200">{e.attackerIp}</td>
                    <td className="px-4 py-2">
                      <span className="flex items-center gap-1.5">
                        <span className={cn('h-1.5 w-1.5 rounded-full', SEVERITY_BG[sev])} />
                        <span className="text-zinc-300 text-xs">
                          {t(`eventType.${e.eventType.replace('.', '') === 'loginfailed' ? 'loginFailed' : e.eventType.replace('.', '') === 'loginsuccess' ? 'loginSuccess' : e.eventType.replace('.', '') === 'httprequest' ? 'httpRequest' : e.eventType}` as any, EVENT_TYPE_LABELS[e.eventType])}
                        </span>
                      </span>
                    </td>
                    <td className="px-4 py-2 font-mono text-[11px] text-zinc-500 truncate max-w-[200px]">
                      {eventDetails(e)}
                    </td>
                  </motion.tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </motion.div>
  );
}

/* ── Top Countries ── */
function TopCountries({ countries }: { countries: { country: string; countryName: string; count: number }[] }) {
  const { t } = useTranslation();
  const max = Math.max(1, countries[0]?.count ?? 1);
  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.35, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-xl glass-strong p-4 space-y-3"
    >
      <h3 className="text-sm font-semibold text-zinc-200 flex items-center gap-2">
        <Globe2 className="h-4 w-4 text-amber-500" />
        {t('dashboard.topCountries')}
      </h3>
      <div className="space-y-2">
        {countries.slice(0, 6).map((c, i) => (
          <div key={c.country} className="flex items-center gap-3 text-sm">
            <span className="w-5 text-right font-mono text-xs text-zinc-600">{i + 1}</span>
            <span className="w-8 font-mono text-xs text-zinc-500 uppercase">{c.country}</span>
            <div className="flex-1 h-1.5 rounded-full bg-zinc-800/50 overflow-hidden">
              <motion.div
                initial={{ width: 0 }}
                animate={{ width: `${(c.count / max) * 100}%` }}
                transition={{ delay: 0.5 + i * 0.05, duration: 0.6 }}
                className="h-full rounded-full bg-gradient-to-r from-amber-500/80 to-orange-500/60"
              />
            </div>
            <span className="font-mono text-xs text-zinc-400 tabular-nums w-12 text-right">
              {formatInt(c.count)}
            </span>
          </div>
        ))}
      </div>
    </motion.div>
  );
}

/* ── Shimmer Skeleton ── */
function ShimmerCard() {
  return <div className="rounded-xl h-24 shimmer" />;
}

/* ══════════════════════════════════════════════════════════════════════
   DASHBOARD PAGE
   ══════════════════════════════════════════════════════════════════════ */
export function DashboardPage() {
  const { t } = useTranslation();
  const { data, isPending } = useStatsOverview();
  const { events } = useLiveAttacks({ bufferSize: 30 });

  const sparkEvents = useMemo(() => generateSparkline(data?.eventsLast24h ?? 500, 200), [data?.eventsLast24h]);
  const sparkIps = useMemo(() => generateSparkline(data?.uniqueAttackers ?? 120, 50), [data?.uniqueAttackers]);

  return (
    <motion.section
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.3 }}
      className="space-y-5"
    >
      {/* Header */}
      <div>
        <h2 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
          <Shield className="h-5 w-5 text-amber-500" />
          {t('dashboard.title')}
        </h2>
        <p className="mt-1 text-sm text-zinc-500 max-w-xl">
          {t('dashboard.subtitle')}
        </p>
      </div>

      {/* KPI Row */}
      {isPending ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => <ShimmerCard key={i} />)}
        </div>
      ) : data ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard
            icon={Activity}
            label={t('dashboard.kpiEvents24h')}
            value={formatCompact(data.eventsLast24h)}
            hint={`${formatInt(data.totalEvents)} ${t('common.total')}`}
            color="bg-gradient-to-r from-amber-500 to-orange-500"
            sparkData={sparkEvents}
            delay={0}
          />
          <KpiCard
            icon={Users}
            label={t('dashboard.kpiUniqueAttackers')}
            value={formatCompact(data.uniqueAttackers)}
            color="bg-gradient-to-r from-rose-500 to-pink-500"
            sparkData={sparkIps}
            delay={0.05}
          />
          <KpiCard
            icon={ServerCog}
            label={t('dashboard.kpiActiveSessions')}
            value={formatInt(data.activeSessions)}
            color="bg-gradient-to-r from-emerald-500 to-teal-500"
            delay={0.1}
          />
          <KpiCard
            icon={TrendingUp}
            label={t('dashboard.kpiTopCountry')}
            value={data.topCountries[0]?.country ?? '—'}
            hint={data.topCountries[0]?.countryName}
            color="bg-gradient-to-r from-blue-500 to-indigo-500"
            delay={0.15}
          />
        </div>
      ) : null}

      {/* Middle row: Sensors + Event Types */}
      {data && (
        <div className="grid gap-4 lg:grid-cols-2">
          <SensorBreakdown data={data.eventsBySensorType} />
          <EventTypeBreakdown data={data.eventsByType} />
        </div>
      )}

      {/* Bottom row: Top countries + Events table */}
      <div className="grid gap-4 lg:grid-cols-[1fr_1.6fr]">
        {data && <TopCountries countries={data.topCountries} />}
        <RecentEventsTable events={events} />
      </div>
    </motion.section>
  );
}
