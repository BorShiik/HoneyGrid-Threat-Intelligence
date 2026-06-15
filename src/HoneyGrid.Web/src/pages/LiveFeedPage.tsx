import { useMemo, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ChevronDown, ChevronUp, Pause, Play, Radio, ShieldAlert, Terminal, Ban, Download, Skull, Crosshair, Network, FileText, Activity } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { SENSOR_LABELS, SEVERITY_BG, eventDetails, eventSeverity, eventTypeKey } from '@/lib/format';
import { CountryFlag } from '@/components/ui/CountryFlag';
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
      <div className="px-5 pb-5 pt-1 grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Terminal dump */}
        <div className="lg:col-span-2 rounded-xl bg-black/80 border border-zinc-800/80 overflow-hidden shadow-inner relative group">
          {/* Scanline overlay */}
          <div className="pointer-events-none absolute inset-0 bg-[linear-gradient(rgba(18,16,16,0)_50%,rgba(0,0,0,0.25)_50%),linear-gradient(90deg,rgba(255,0,0,0.06),rgba(0,255,0,0.02),rgba(0,0,255,0.06))] bg-[length:100%_4px,3px_100%] opacity-20 group-hover:opacity-10 transition-opacity" />
          
          <div className="flex items-center gap-3 px-4 py-2 bg-zinc-900 border-b border-zinc-800">
            <div className="flex gap-1.5">
              <span className="h-2.5 w-2.5 rounded-full bg-rose-500/80" />
              <span className="h-2.5 w-2.5 rounded-full bg-amber-500/80" />
              <span className="h-2.5 w-2.5 rounded-full bg-emerald-500/80" />
            </div>
            <div className="flex items-center gap-1.5 ml-2">
              <Terminal className="h-3.5 w-3.5 text-zinc-500" />
              <span className="text-[11px] text-zinc-400 font-mono tracking-wider">session-dump</span>
            </div>
          </div>
          <pre className="p-4 text-[13px] font-mono text-zinc-300 leading-relaxed overflow-x-auto max-h-64 relative z-10">
            <span className="text-zinc-600"># {new Date(event.timestamp).toISOString()}</span>{'\n'}
            <span className="text-zinc-600"># Sensor: </span><span className="text-amber-400">{event.sensorId}</span>{'\n'}
            <span className="text-zinc-600"># Source: </span><span className="text-rose-400">{event.attackerIp}</span>
            {event.geo && <span className="text-zinc-600"> ({event.geo.countryName}, AS{event.geo.asn})</span>}{'\n'}
            {'\n'}
            {event.credentials && (
              <><span className="text-zinc-500">{'> '}</span><span className="text-blue-400">AUTH</span> {event.credentials.username}:<span className="text-zinc-500">{event.credentials.password}</span> → <span className={event.eventType === 'login.success' ? 'text-emerald-400 bg-emerald-500/10 px-1 rounded' : 'text-rose-400 bg-rose-500/10 px-1 rounded'}>{event.eventType === 'login.success' ? 'SUCCESS' : 'FAILED'}</span>{'\n'}</>
            )}
            {event.command && (
              <><span className="text-emerald-500">$</span> <span className="text-zinc-200">{event.command.split(' ').map((word, i) => ['curl', 'wget', 'sh', 'bash', 'crontab'].includes(word) ? `<span class="text-rose-400 font-bold">${word}</span>` : word).join(' ').replace(/<span class="text-rose-400 font-bold">([^<]+)<\/span>/g, '<span class="text-rose-400 font-bold">$1</span>')}</span>{'\n'}</>
            )}
            {event.http && (
              <><span className="text-blue-400 font-bold">{event.http.method}</span> <span className="text-zinc-300">{event.http.path}</span>{'\n'}<span className="text-zinc-600">User-Agent: {event.http.userAgent}</span>{'\n'}</>
            )}
            {event.downloadHash && (
              <><span className="text-zinc-600">sha256:</span><span className="text-amber-400">{event.downloadHash}</span>{'\n'}</>
            )}
            {!event.command && !event.http && !event.credentials && (
              <span className="text-zinc-600 italic">No additional payload data.</span>
            )}
          </pre>
        </div>

        {/* Threat Intel sidebar */}
        <div className="space-y-3">
          <div className="rounded-xl glass border border-amber-500/10 p-4 space-y-4">
            <div className="flex items-center gap-2 border-b border-white/5 pb-2">
              <ShieldAlert className="h-4 w-4 text-amber-500" />
              <span className="text-sm font-semibold text-zinc-200">{t('liveFeed.threatIntel')}</span>
            </div>
            
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1">
                <span className="text-[10px] uppercase tracking-wider text-zinc-500 flex items-center gap-1"><Skull className="h-3 w-3" /> {t('liveFeed.knownMalicious')}</span>
                <div className={cn("text-xs font-medium inline-flex px-1.5 py-0.5 rounded", event.threatIntel?.knownMalicious ? 'bg-rose-500/10 text-rose-400 border border-rose-500/20' : 'bg-zinc-800 text-zinc-400')}>
                  {event.threatIntel?.knownMalicious ? t('liveFeed.yes') : t('liveFeed.no')}
                </div>
              </div>
              <div className="space-y-1">
                <span className="text-[10px] uppercase tracking-wider text-zinc-500 flex items-center gap-1"><Activity className="h-3 w-3" /> {t('liveFeed.reputation')}</span>
                <div className="font-mono text-sm text-zinc-200">{event.threatIntel?.score ?? '—'}<span className="text-zinc-600 text-xs">/100</span></div>
              </div>
              
              {event.classification && (
                <>
                  <div className="space-y-1 col-span-2">
                    <span className="text-[10px] uppercase tracking-wider text-zinc-500 flex items-center gap-1"><Crosshair className="h-3 w-3" /> Kill Chain</span>
                    <div className="text-xs text-amber-400 font-mono bg-amber-500/10 border border-amber-500/20 px-2 py-1 rounded-md inline-block">
                      {t(`killChain.${event.classification.killChainPhase}` as any, event.classification.killChainPhase)}
                    </div>
                  </div>
                  <div className="space-y-1 col-span-2">
                    <span className="text-[10px] uppercase tracking-wider text-zinc-500 flex items-center gap-1"><Network className="h-3 w-3" /> {t('liveFeed.category')}</span>
                    <div className="text-xs text-zinc-300">
                      {event.classification.category}
                    </div>
                  </div>
                </>
              )}
            </div>
          </div>

          {/* Action buttons */}
          <div className="flex gap-2">
            <button className="flex-1 flex items-center justify-center gap-2 rounded-xl bg-rose-500/10 border border-rose-500/30 px-4 py-2.5 text-xs font-medium text-rose-400 hover:bg-rose-500 hover:text-white transition-all shadow-[0_0_10px_rgba(225,29,72,0.1)] hover:shadow-[0_0_15px_rgba(225,29,72,0.4)]">
              <Ban className="h-3.5 w-3.5" /> {t('liveFeed.blockIp')}
            </button>
            <button className="flex-1 flex items-center justify-center gap-2 rounded-xl bg-white/5 border border-white/10 px-4 py-2.5 text-xs font-medium text-zinc-300 hover:bg-white/10 hover:text-white transition-all">
              <Download className="h-3.5 w-3.5" /> {t('liveFeed.export')}
            </button>
          </div>
        </div>
      </div>
    </motion.div>
  );
}

const ROW_GRID_CLASS = "grid grid-cols-[16px_75px_70px_130px_minmax(110px,160px)_minmax(120px,150px)_1fr_24px] gap-3 items-center";

const SEVERITY_BORDER: Record<Severity, string> = {
  critical: 'border-l-rose-500',
  high: 'border-l-amber-500',
  medium: 'border-l-emerald-500/50',
  low: 'border-l-transparent',
};

/* ── Feed Row ── */
function FeedRow({ event }: { event: HoneypotEvent }) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const severity = eventSeverity(event);

  return (
    <div className={cn('border-b border-white/[0.03] transition-all relative', SEVERITY_GLOW[severity])}>
      {/* Animated accent line when row is added */}
      <motion.div 
        initial={{ opacity: 1, backgroundColor: 'rgba(255,255,255,0.1)' }}
        animate={{ opacity: 0 }}
        transition={{ duration: 1.5 }}
        className="pointer-events-none absolute inset-0 z-0" 
      />
      <button
        onClick={() => setExpanded((p) => !p)}
        className={cn(
          "w-full text-left px-5 py-2.5 text-sm hover:bg-white/[0.03] transition-colors relative z-10 border-l-[3px]",
          SEVERITY_BORDER[severity],
          expanded ? 'bg-white/[0.02]' : ''
        )}
      >
        <div className={ROW_GRID_CLASS}>
          <span className={cn('h-2 w-2 rounded-full justify-self-center', SEVERITY_BG[severity])} style={{
            boxShadow: severity === 'critical' ? '0 0 8px rgba(225,29,72,0.6)' : severity === 'high' ? '0 0 6px rgba(249,115,22,0.5)' : undefined,
          }} />
          <span className="font-mono text-[11px] text-zinc-500">
            {new Date(event.timestamp).toLocaleTimeString('en-GB')}
          </span>
          <span className={cn('inline-flex items-center rounded bg-white/5 px-1.5 py-0.5 font-mono text-[10px] font-bold tracking-wider justify-center shadow-sm', SENSOR_BADGE[event.sensorType])}>
            {SENSOR_LABELS[event.sensorType]}
          </span>
          <span className="font-mono text-xs text-zinc-200 truncate">{event.attackerIp}</span>
          <span className="hidden lg:flex items-center gap-2 text-xs text-zinc-400 truncate">
            {event.geo?.country && <CountryFlag code={event.geo.country} className="text-sm shadow-sm" />}
            <span className="truncate">{event.geo?.countryName ?? '—'}</span>
          </span>
          <span className="text-xs text-zinc-400 truncate">
            {t(`eventType.${eventTypeKey(event.eventType)}`)}
          </span>
          <span className="truncate font-mono text-[11px] text-zinc-500">{eventDetails(event)}</span>
          <span className="text-zinc-600 justify-self-end">
            {expanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
          </span>
        </div>
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
          {/* Sensor filter (Segmented Control) */}
          <div className="flex items-center rounded-lg bg-black/40 p-1 border border-white/5 shadow-inner">
            {SENSOR_FILTERS.map((s) => (
              <button
                key={s}
                onClick={() => setSensor(s)}
                className={cn(
                  'relative rounded-md px-3 py-1.5 text-xs font-medium transition-colors',
                  sensor === s ? 'text-white' : 'text-zinc-500 hover:text-zinc-300',
                )}
              >
                {sensor === s && (
                  <motion.div
                    layoutId="sensor-pill"
                    className="absolute inset-0 rounded-md bg-white/10 shadow-sm"
                    transition={{ type: 'spring', stiffness: 400, damping: 30 }}
                  />
                )}
                <span className="relative z-10">{s === 'all' ? t('common.all') : SENSOR_LABELS[s as SensorType]}</span>
              </button>
            ))}
          </div>

          {/* Severity filter (Segmented Control) */}
          <div className="flex items-center rounded-lg bg-black/40 p-1 border border-white/5 shadow-inner">
            {SEVERITY_FILTERS.map((s) => (
              <button
                key={s}
                onClick={() => setSeverity(s)}
                className={cn(
                  'relative rounded-md px-3 py-1.5 text-xs font-medium transition-colors flex items-center gap-1.5',
                  severity === s ? 'text-white' : 'text-zinc-500 hover:text-zinc-300',
                )}
              >
                {severity === s && (
                  <motion.div
                    layoutId="severity-pill"
                    className="absolute inset-0 rounded-md bg-white/10 shadow-sm"
                    transition={{ type: 'spring', stiffness: 400, damping: 30 }}
                  />
                )}
                <span className="relative z-10 flex items-center gap-1.5">
                  {s !== 'all' && <span className={cn('h-1.5 w-1.5 rounded-full', SEVERITY_BG[s as Severity])} />}
                  {s === 'all' ? t('common.all') : t(`severity.${s}` as any, s.charAt(0).toUpperCase() + s.slice(1))}
                </span>
              </button>
            ))}
          </div>

          {/* Pause/Resume */}
          <button
            onClick={() => setPaused((p) => !p)}
            className={cn(
              'flex items-center gap-1.5 rounded-lg px-4 py-2 text-xs font-semibold transition-all border shadow-sm',
              paused 
                ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20 hover:bg-emerald-500/20 shadow-[0_0_10px_rgba(16,185,129,0.1)]' 
                : 'bg-zinc-800/50 text-zinc-300 border-white/5 hover:bg-zinc-800 hover:text-white',
            )}
          >
            {paused ? <Play className="h-3.5 w-3.5" /> : <Pause className="h-3.5 w-3.5" />}
            {paused ? t('liveFeed.resume') : t('liveFeed.pause')}
          </button>
        </div>
      </div>

      {/* Stats bar */}
      <div className="flex items-center gap-5 text-xs text-zinc-500 bg-black/20 rounded-lg px-3 py-2 border border-white/[0.02] w-fit">
        <span className="flex items-center gap-1.5">
          <FileText className="h-3.5 w-3.5 text-zinc-600" />
          {t('liveFeed.buffer')}: <span className="font-mono text-zinc-300 ml-1">{visible.length}</span>
        </span>
        <span className="flex items-center gap-1.5 border-l border-zinc-800 pl-4">
          <span className={cn('h-2 w-2 rounded-full', paused ? 'bg-amber-500 shadow-[0_0_5px_rgba(245,158,11,0.5)]' : 'bg-emerald-500 animate-pulse shadow-[0_0_5px_rgba(16,185,129,0.5)]')} />
          <span className={paused ? 'text-amber-500/80' : 'text-emerald-500/80'}>{paused ? t('liveFeed.paused') : t('liveFeed.live')}</span>
        </span>
      </div>

      {/* Event List */}
      <div className="rounded-xl glass-strong overflow-hidden border border-white/[0.05] shadow-xl">
        <div className="overflow-x-auto">
          <div className="min-w-[780px]">
            {/* Header row */}
            <div className="px-5 py-3 text-[10px] font-bold text-zinc-400 uppercase tracking-widest border-b border-white/[0.06] bg-black/40">
              <div className={ROW_GRID_CLASS}>
                <span />
                <span>{t('feedCols.time')}</span>
                <span>{t('feedCols.protocol')}</span>
                <span>{t('feedCols.source')}</span>
                <span className="hidden lg:block">{t('feedCols.country')}</span>
                <span>{t('feedCols.type')}</span>
                <span>{t('feedCols.payload')}</span>
                <span />
              </div>
            </div>

            <div className="max-h-[calc(100vh-320px)] overflow-y-auto overflow-x-hidden">
              {visible.length === 0 ? (
                <div className="p-12 text-center text-sm text-zinc-500 flex flex-col items-center gap-3">
                  <Activity className="h-8 w-8 text-zinc-700 animate-pulse" />
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
