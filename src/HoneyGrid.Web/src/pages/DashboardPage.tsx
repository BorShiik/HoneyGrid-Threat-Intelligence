import { useMemo } from 'react';
import { motion } from 'framer-motion';
import { Activity, ServerCog, ShieldHalf, TrendingUp, Users } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useStatsOverview } from '@/api/queries';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { formatCompact, formatInt } from '@/lib/format';
import { DatascapeBackground } from '@/components/three/DatascapeBackground';
import { HolographicKpiCard } from '@/components/dashboard/HolographicKpiCard';
import { SensorFuelCell } from '@/components/dashboard/SensorFuelCell';
import { EventEqualizer } from '@/components/dashboard/EventEqualizer';
import { ThreatCountries } from '@/components/dashboard/ThreatCountries';
import { LiveFeedRain } from '@/components/dashboard/LiveFeedRain';

/* ── Mock sparkline series (24 points). Real metrics arrive via SignalR. ── */
const sparkline = (base: number, variance: number) =>
  Array.from({ length: 24 }, (_, i) => ({
    h: i,
    v: Math.max(0, Math.floor(base + (Math.random() - 0.5) * variance)),
  }));

function ShimmerCard() {
  return <div className="h-32 rounded-2xl shimmer" />;
}

/* ══════════════════════════════════════════════════════════════════════
   DASHBOARD — the "Datascape": extreme glass UI floating over a 3D backdrop.
   ══════════════════════════════════════════════════════════════════════ */
export function DashboardPage() {
  const { t } = useTranslation();
  const { data, isPending } = useStatsOverview();
  const { events, simulated } = useLiveAttacks({ bufferSize: 40 });

  const sparkEvents = useMemo(() => sparkline(data?.eventsLast24h ?? 500, 200), [data?.eventsLast24h]);
  const sparkIps = useMemo(() => sparkline(data?.uniqueAttackers ?? 120, 50), [data?.uniqueAttackers]);
  const sparkSessions = useMemo(() => sparkline(data?.activeSessions ?? 14, 8), [data?.activeSessions]);

  return (
    <>
      {/* Fixed 3D backdrop the glass panels float over (dashboard-only). */}
      <DatascapeBackground />

      <motion.section
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.4 }}
        className="relative z-10 space-y-5"
      >
        {/* Header */}
        <div className="flex items-end justify-between gap-4">
          <div>
            <h2 className="flex items-center gap-2.5 text-2xl font-bold tracking-tight text-white">
              <ShieldHalf className="h-6 w-6 text-amber-500" />
              {t('dashboard.title')}
            </h2>
            <p className="mt-1 max-w-xl text-sm text-zinc-500">{t('dashboard.subtitle')}</p>
          </div>
          <div className="hidden items-center gap-2 rounded-full border border-white/5 bg-white/[0.03] px-3 py-1.5 backdrop-blur md:flex">
            <span
              className={`h-2 w-2 rounded-full ${simulated ? 'bg-amber-500' : 'bg-emerald-500'} pulse-glow`}
            />
            <span className="font-mono text-[11px] uppercase tracking-widest text-zinc-400">
              {simulated ? 'sim stream' : 'signalr live'}
            </span>
          </div>
        </div>

        {/* ── A. Holographic KPI row ── */}
        {isPending ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {Array.from({ length: 4 }, (_, i) => (
              <ShimmerCard key={i} />
            ))}
          </div>
        ) : data ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <HolographicKpiCard
              icon={Activity}
              accent="amber"
              label={t('dashboard.kpiEvents24h')}
              value={formatCompact(data.eventsLast24h)}
              hint={`${formatInt(data.totalEvents)} ${t('common.total')}`}
              sparkData={sparkEvents}
              withSphere
              delay={0}
            />
            <HolographicKpiCard
              icon={Users}
              accent="rose"
              label={t('dashboard.kpiUniqueAttackers')}
              value={formatCompact(data.uniqueAttackers)}
              sparkData={sparkIps}
              delay={0.06}
            />
            <HolographicKpiCard
              icon={ServerCog}
              accent="emerald"
              label={t('dashboard.kpiActiveSessions')}
              value={formatInt(data.activeSessions)}
              sparkData={sparkSessions}
              delay={0.12}
            />
            <HolographicKpiCard
              icon={TrendingUp}
              accent="blue"
              label={t('dashboard.kpiTopCountry')}
              value={data.topCountries[0]?.country ?? '—'}
              hint={data.topCountries[0]?.countryName}
              delay={0.18}
            />
          </div>
        ) : null}

        {/* ── B + C. Fuel cell + equalizer ── */}
        {data && (
          <div className="grid gap-4 lg:grid-cols-2">
            <SensorFuelCell data={data.eventsBySensorType} delay={0.2} />
            <EventEqualizer data={data.eventsByType} delay={0.26} />
          </div>
        )}

        {/* ── D + E. Threat countries + live digital rain ── */}
        <div className="grid gap-4 lg:grid-cols-[1fr_1.5fr]">
          {data && <ThreatCountries countries={data.topCountries} delay={0.3} />}
          <LiveFeedRain events={events} delay={0.34} />
        </div>
      </motion.section>
    </>
  );
}
