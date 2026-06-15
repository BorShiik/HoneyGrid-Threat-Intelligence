import { useMemo, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ChevronDown, ChevronUp, Pause, Play, Radio, ShieldAlert, Terminal, Ban, Download } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { SENSOR_LABELS, SEVERITY_BG, eventDetails, eventSeverity, eventTypeKey } from '@/lib/format';
import type { HoneypotEvent, SensorType, Severity } from '@/types/api';

const SENSOR_FILTERS: readonly (SensorType | 'all')[] = ['all', 'ssh', 'web', 'rdp'];
const SEVERITY_FILTERS: readonly (Severity | 'all')[] = ['all', 'critical', 'high', 'medium', 'low'];

const SENSOR_BADGE: Record<SensorType, string> = {
  ssh: 'bg-rose-500/10 text-rose-400 ring-rose-500/20',
  web: 'bg-amber-500/10 text-amber-400 ring-amber-500/20',
  rdp: 'bg-blue-500/10 text-blue-400 ring-blue-500/20',
};

const SEVERITY_GLOW: Record<Severity, string> = {
  critical: 'shadow-[inset_0_0_0_1px_rgba(225,29,72,0.2)] hover:shadow-[inset_0_0_0_1px_rgba(225,29,72,0.4)]',
  high: 'shadow-[inset_0_0_0_1px_rgba(249,115,22,0.15)] hover:shadow-[inset_0_0_0_1px_rgba(249,115,22,0.3)]',
  medium: 'shadow-[inset_0_0_0_1px_rgba(245,158,11,0.1)] hover:shadow-[inset_0_0_0_1px_rgba(245,158,11,0.2)]',
  low: '',
};

/* ── Expanded Row Detail ── */
function EventDetail({ event }: { event: HoneypotEvent }) {
  const { t } = useTranslation();
  return (
    <motion.div
      initial={{ height: 0, opacity: 0 }}
      animate={{ height: 'auto', opacity: 1 }}
      exit={{ height: 0, opacity: 0 }}
      transition={{ type: 'spring', stiffness: 300, damping: 30 }}
      className="overflow-hidden"
    >
      <div className="px-5 pb-4 pt-1 grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Terminal dump */}
        <div className="lg:col-span-2 rounded-lg bg-black/60 border border-zinc-800/50 overflow-hidden">
          <div className="flex items-center gap-2 px-3 py-1.5 bg-zinc-900/80 border-b border-zinc-800/50">
            <Terminal className="h-3 w-3 text-zinc-500" />
            <span className="text-[10px] text-zinc-500 font-mono">session-dump</span>
            <div className="flex gap-1 ml-auto">
              <span className="h-2 w-2 rounded-full bg-rose-500/60" />
              <span className="h-2 w-2 rounded-full bg-amber-500/60" />
              <span className="h-2 w-2 rounded-full bg-emerald-500/60" />
            </div>
          </div>
          <pre className="p-3 text-xs font-mono text-zinc-300 leading-relaxed overflow-x-auto max-h-48">
            <span className="text-zinc-600"># {new Date(event.timestamp).toISOString()}</span>{'\n'}
            <span className="text-zinc-600"># Sensor: </span><span className="text-amber-400">{event.sensorId}</span>{'\n'}
            <span className="text-zinc-600"># Source: </span><span className="text-rose-400">{event.attackerIp}</span>
            {event.geo && <span className="text-zinc-600"> ({event.geo.countryName}, AS{event.geo.asn})</span>}{'\n'}
            {'\n'}
            {event.credentials && (
              <><span className="text-zinc-500">{'> '}</span><span className="text-blue-400">AUTH</span> {event.credentials.username}:<span className="text-zinc-500">{event.credentials.password}</span> → <span className={event.eventType === 'login.success' ? 'text-rose-400' : 'text-zinc-500'}>{event.eventType === 'login.success' ? 'SUCCESS ⚠' : 'FAILED'}</span>{'\n'}</>
            )}
            {event.command && (
              <><span className="text-emerald-500">$</span> {event.command}{'\n'}</>
            )}
            {event.http && (
              <><span className="text-blue-400">{event.http.method}</span> <span className="text-zinc-300">{event.http.path}</span>{'\n'}<span className="text-zinc-600">User-Agent: {event.http.userAgent}</span>{'\n'}</>
            )}
            {event.downloadHash && (
              <><span className="text-zinc-600">sha256:</span><span className="text-amber-400">{event.downloadHash}</span>{'\n'}</>
            )}
          </pre>
        </div>

        {/* Threat Intel sidebar */}
        <div className="space-y-3">
          <div className="rounded-lg glass p-3 space-y-2">
            <div className="flex items-center gap-2">
              <ShieldAlert className="h-3.5 w-3.5 text-amber-500" />
              <span className="text-xs font-semibold text-zinc-300">{t('liveFeed.threatIntel')}</span>
            </div>
            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between">
                <span className="text-zinc-500">{t('liveFeed.knownMalicious')}</span>
                <span className={event.threatIntel?.knownMalicious ? 'text-rose-400 font-medium' : 'text-zinc-400'}>
                  {event.threatIntel?.knownMalicious ? t('liveFeed.yes') : t('liveFeed.no')}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-zinc-500">{t('liveFeed.reputation')}</span>
                <span className="font-mono text-zinc-300">{event.threatIntel?.score ?? '—'}/100</span>
              </div>
              {event.threatIntel?.sources && event.threatIntel.sources.length > 0 && (
                <div className="flex justify-between">
                  <span className="text-zinc-500">{t('liveFeed.sources')}</span>
                  <span className="text-zinc-400">{event.threatIntel.sources.join(', ')}</span>
                </div>
              )}
              {event.classification && (
                <>
                  <div className="border-t border-zinc-800/50 my-1.5" />
                  <div className="flex justify-between">
                    <span className="text-zinc-500">Kill Chain</span>
                    <span className="text-amber-400 font-mono text-[10px] uppercase">
                      {t(`killChain.${event.classification.killChainPhase}` as any, event.classification.killChainPhase)}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-zinc-500">{t('liveFeed.category')}</span>
                    <span className="text-zinc-300">{event.classification.category}</span>
                  </div>
                </>
              )}
            </div>
          </div>

          {/* Action buttons */}
          <div className="flex gap-2">
            <button className="flex-1 flex items-center justify-center gap-1.5 rounded-lg bg-rose-500/10 border border-rose-500/20 px-3 py-2 text-xs text-rose-400 hover:bg-rose-500/20 transition-colors">
              <Ban className="h-3 w-3" /> {t('liveFeed.blockIp')}
            </button>
            <button className="flex-1 flex items-center justify-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-xs text-zinc-400 hover:bg-white/10 transition-colors">
              <Download className="h-3 w-3" /> {t('liveFeed.export')}
            </button>
          </div>
        </div>
      </div>
    </motion.div>
  );
}

/* ── Feed Row ── */
function FeedRow({ event }: { event: HoneypotEvent }) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const severity = eventSeverity(event);

  return (
    <div className={cn('border-b border-white/[0.03] transition-all', SEVERITY_GLOW[severity])}>
      <button
        onClick={() => setExpanded((p) => !p)}
        className="w-full flex items-center gap-3 px-5 py-2.5 text-sm hover:bg-white/[0.03] transition-colors text-left"
      >
        <span className={cn('h-2 w-2 shrink-0 rounded-full', SEVERITY_BG[severity])} style={{
          boxShadow: severity === 'critical' ? '0 0 6px rgba(225,29,72,0.5)' : severity === 'high' ? '0 0 5px rgba(249,115,22,0.4)' : undefined,
        }} />
        <span className="w-20 shrink-0 font-mono text-[11px] text-zinc-500">
          {new Date(event.timestamp).toLocaleTimeString('en-GB')}
        </span>
        <span className={cn('inline-flex items-center rounded-md px-1.5 py-0.5 font-mono text-[10px] font-semibold ring-1 ring-inset w-11 justify-center shrink-0', SENSOR_BADGE[event.sensorType])}>
          {SENSOR_LABELS[event.sensorType]}
        </span>
        <span className="w-36 shrink-0 font-mono text-xs text-zinc-200">{event.attackerIp}</span>
        <span className="hidden lg:inline w-32 shrink-0 text-xs text-zinc-500">{event.geo?.countryName ?? '—'}</span>
        <span className="w-36 shrink-0 text-xs text-zinc-400">
          {t(`eventType.${eventTypeKey(event.eventType)}`)}
        </span>
        <span className="flex-1 truncate font-mono text-[11px] text-zinc-600">{eventDetails(event)}</span>
        <span className="shrink-0 text-zinc-600">
          {expanded ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
        </span>
      </button>
      <AnimatePresence>
        {expanded && <EventDetail event={event} />}
      </AnimatePresence>
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════
   LIVE FEED PAGE
   ══════════════════════════════════════════════════════════════════════ */
export function LiveFeedPage() {
  const { t } = useTranslation();
  const [paused, setPaused] = useState(false);
  const [sensor, setSensor] = useState<SensorType | 'all'>('all');
  const [severity, setSeverity] = useState<Severity | 'all'>('all');
  const { events, simulated } = useLiveAttacks({ bufferSize: 300, enabled: !paused });

  const visible = useMemo(() => {
    let filtered = events;
    if (sensor !== 'all') filtered = filtered.filter((e) => e.sensorType === sensor);
    if (severity !== 'all') filtered = filtered.filter((e) => eventSeverity(e) === severity);
    return filtered;
  }, [events, sensor, severity]);

  return (
    <motion.section
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className="space-y-4"
    >
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
            {t('liveFeed.title')}
            <Radio className={cn('h-4 w-4', simulated ? 'text-amber-500' : 'text-emerald-500')} />
          </h2>
          <p className="mt-1 text-sm text-zinc-500">
            {t('liveFeed.subtitle')}
            {simulated && ` · ${t('common.demoMode')}`}
          </p>
        </div>

        {/* Controls */}
        <div className="flex items-center gap-2 flex-wrap">
          {/* Sensor filter */}
          <div className="flex items-center gap-1 rounded-lg bg-white/5 p-0.5">
            {SENSOR_FILTERS.map((s) => (
              <button
                key={s}
                onClick={() => setSensor(s)}
                className={cn(
                  'rounded-md px-2.5 py-1 text-xs font-medium transition-colors',
                  sensor === s ? 'bg-white/10 text-white' : 'text-zinc-500 hover:text-zinc-300',
                )}
              >
                {s === 'all' ? t('common.all') : SENSOR_LABELS[s as SensorType]}
              </button>
            ))}
          </div>

          {/* Severity filter */}
          <div className="flex items-center gap-1 rounded-lg bg-white/5 p-0.5">
            {SEVERITY_FILTERS.map((s) => (
              <button
                key={s}
                onClick={() => setSeverity(s)}
                className={cn(
                  'rounded-md px-2 py-1 text-xs font-medium transition-colors flex items-center gap-1',
                  severity === s ? 'bg-white/10 text-white' : 'text-zinc-500 hover:text-zinc-300',
                )}
              >
                {s !== 'all' && <span className={cn('h-1.5 w-1.5 rounded-full', SEVERITY_BG[s as Severity])} />}
                {s === 'all' ? t('common.all') : t(`severity.${s}` as any, s.charAt(0).toUpperCase() + s.slice(1))}
              </button>
            ))}
          </div>

          {/* Pause/Resume */}
          <button
            onClick={() => setPaused((p) => !p)}
            className={cn(
              'flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-medium transition-colors',
              paused ? 'bg-emerald-500/10 text-emerald-400 hover:bg-emerald-500/20' : 'bg-white/5 text-zinc-400 hover:bg-white/10',
            )}
          >
            {paused ? <Play className="h-3.5 w-3.5" /> : <Pause className="h-3.5 w-3.5" />}
            {paused ? t('liveFeed.resume') : t('liveFeed.pause')}
          </button>
        </div>
      </div>

      {/* Stats bar */}
      <div className="flex items-center gap-5 text-xs text-zinc-500">
        <span>
          {t('liveFeed.buffer')}: <span className="font-mono text-zinc-300">{visible.length}</span>
        </span>
        <span className="flex items-center gap-1.5">
          <span className={cn('h-1.5 w-1.5 rounded-full', paused ? 'bg-amber-500' : 'bg-emerald-500')} />
          {paused ? t('liveFeed.paused') : t('liveFeed.live')}
        </span>
      </div>

      {/* Event List */}
      <div className="rounded-xl glass-strong overflow-hidden">
        <div className="overflow-x-auto">
        <div className="min-w-[680px]">
        {/* Header row */}
        <div className="flex items-center gap-3 px-5 py-2 text-[10px] font-semibold text-zinc-500 uppercase tracking-wider border-b border-white/[0.04] bg-zinc-900/30">
          <span className="w-2" />
          <span className="w-20">{t('feedCols.time')}</span>
          <span className="w-11">{t('feedCols.protocol')}</span>
          <span className="w-36">{t('feedCols.source')}</span>
          <span className="hidden lg:inline w-32">{t('feedCols.country')}</span>
          <span className="w-36">{t('feedCols.type')}</span>
          <span className="flex-1">{t('feedCols.payload')}</span>
          <span className="w-4" />
        </div>

        <div className="max-h-[calc(100vh-300px)] overflow-y-auto">
          {visible.length === 0 ? (
            <div className="p-8 text-center text-sm text-zinc-600">
              {paused ? t('liveFeed.paused') : t('liveFeed.waiting')}
            </div>
          ) : (
            visible.map((event) => <FeedRow key={event.id} event={event} />)
          )}
        </div>
        </div>
        </div>
      </div>
    </motion.section>
  );
}
