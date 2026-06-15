import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Fingerprint,
  Activity,
  Globe,
  Crosshair,
  ShieldAlert,
  Terminal,
  Search,
  BarChart2,
  AlertTriangle,
  Target
} from 'lucide-react';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  Cell,
  PieChart,
  Pie
} from 'recharts';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';
import { CountryFlag } from '@/components/ui/CountryFlag';
import { useActors } from '@/api/queries';
import type { Severity, ThreatActor } from '@/types/api';

/* ── Severity palette ── */
const SEV: Record<Severity, { text: string; chip: string; accent: string; glow: string; color: string }> = {
  critical: {
    text: 'text-rose-400',
    chip: 'bg-rose-500/10 text-rose-400 ring-rose-500/20',
    accent: 'bg-rose-500',
    glow: 'shadow-[0_0_24px_-6px_rgba(225,29,72,0.55)]',
    color: '#fb7185', // rose-400
  },
  high: {
    text: 'text-orange-400',
    chip: 'bg-orange-500/10 text-orange-400 ring-orange-500/20',
    accent: 'bg-orange-500',
    glow: 'shadow-[0_0_22px_-7px_rgba(249,115,22,0.5)]',
    color: '#fb923c', // orange-400
  },
  medium: {
    text: 'text-amber-400',
    chip: 'bg-amber-500/10 text-amber-400 ring-amber-500/20',
    accent: 'bg-amber-500',
    glow: 'shadow-[0_0_20px_-8px_rgba(245,158,11,0.45)]',
    color: '#fbbf24', // amber-400
  },
  low: {
    text: 'text-blue-400',
    chip: 'bg-blue-500/10 text-blue-400 ring-blue-500/20',
    accent: 'bg-blue-500',
    glow: '',
    color: '#60a5fa', // blue-400
  },
};

/* ── Compact Actor card (Left List) ── */
function ActorCard({ actor, selected, onClick }: { actor: ThreatActor; selected: boolean; onClick: () => void }) {
  const { t } = useTranslation();
  const sev = SEV[actor.severity];
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'group relative w-full overflow-hidden rounded-xl p-4 text-left transition-all border',
        selected 
          ? `bg-white/10 border-white/20 ${sev.glow}` 
          : 'glass-strong border-transparent hover:bg-white/[0.04]'
      )}
    >
      <div className={cn('absolute top-0 left-0 bottom-0 w-1 transition-all', selected ? sev.accent : 'bg-transparent group-hover:bg-white/10')} />
      <div className="flex items-start justify-between gap-2 pl-1">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <Fingerprint className={cn('h-4 w-4 shrink-0', sev.text)} />
            <span className="truncate font-semibold text-zinc-200 group-hover:text-white transition-colors">{actor.name}</span>
          </div>
          <div className="mt-1 flex items-baseline gap-1.5">
            <span className="font-mono text-sm font-bold tabular-nums text-zinc-300 group-hover:text-white transition-colors">{formatInt(actor.eventCount)}</span>
            <span className="text-[10px] text-zinc-500 uppercase tracking-wider">{t('actors.events')}</span>
          </div>
        </div>
        <div className="flex flex-col items-end gap-2">
          <span className={cn('shrink-0 rounded bg-white/5 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider shadow-sm ring-1 ring-inset', sev.chip)}>
            {t(`severity.${actor.severity}`)}
          </span>
          <div className="flex -space-x-1">
             {actor.countries.slice(0, 3).map((c) => (
                <div key={c} className="rounded-full ring-2 ring-[#09090b] overflow-hidden" title={c}>
                  <CountryFlag code={c} className="text-sm" />
                </div>
              ))}
              {actor.countries.length > 3 && (
                <div className="flex items-center justify-center rounded-full bg-zinc-800 ring-2 ring-[#09090b] h-[14px] w-[14px] text-[8px] font-bold text-zinc-400">
                  +{actor.countries.length - 3}
                </div>
              )}
          </div>
        </div>
      </div>
    </button>
  );
}

/* ── Global Stats Dashboard (Right Pane Empty State) ── */
function GlobalStatsDashboard({ actors }: { actors: ThreatActor[] }) {
  const { t } = useTranslation();
  
  const stats = useMemo(() => {
    let totalEvents = 0;
    const bySev: Record<Severity, number> = { critical: 0, high: 0, medium: 0, low: 0 };
    const countryCount: Record<string, number> = {};

    actors.forEach(a => {
      totalEvents += a.eventCount;
      bySev[a.severity] = (bySev[a.severity] || 0) + 1;
      a.countries.forEach(c => {
        countryCount[c] = (countryCount[c] || 0) + 1;
      });
    });

    const severityChartData = Object.entries(bySev)
      .filter(([_, count]) => count > 0)
      .map(([sev, count]) => ({
        name: t(`severity.${sev}`),
        value: count,
        color: SEV[sev as Severity].color,
      }));

    const topCountries = Object.entries(countryCount)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5)
      .map(([country, count]) => ({ country, count }));

    return { totalEvents, severityChartData, topCountries };
  }, [actors, t]);

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.98 }}
      animate={{ opacity: 1, scale: 1 }}
      className="h-full flex flex-col p-6 lg:p-8 space-y-8 glass-strong rounded-2xl border border-white/5"
    >
      <div>
        <h3 className="text-2xl font-bold text-white tracking-tight flex items-center gap-3">
          <Globe className="h-6 w-6 text-amber-500" />
          {t('actors.globalAnalytics', 'Глобальная Аналитика Угроз')}
        </h3>
        <p className="text-zinc-500 mt-2 text-sm max-w-xl leading-relaxed">
          {t('actors.globalSubtitle', 'Обзор всех отслеживаемых кампаний и группировок. Анализ активности ИИ-моделями на основе паттернов поведения, известных инфраструктур и цепочек атак.')}
        </p>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 rounded-xl bg-black/40 border border-white/5 shadow-inner">
          <div className="text-zinc-500 text-xs font-semibold uppercase tracking-widest mb-1">{t('actors.tracked', 'Отслеживается')}</div>
          <div className="text-3xl font-mono text-white font-bold">{actors.length}</div>
        </div>
        <div className="p-4 rounded-xl bg-black/40 border border-white/5 shadow-inner">
          <div className="text-zinc-500 text-xs font-semibold uppercase tracking-widest mb-1">{t('actors.totalEvents', 'Всего Событий')}</div>
          <div className="text-3xl font-mono text-white font-bold">{formatInt(stats.totalEvents)}</div>
        </div>
        <div className="col-span-2 p-4 rounded-xl bg-black/40 border border-white/5 shadow-inner flex items-center justify-between">
            <div className="space-y-1">
              <div className="text-zinc-500 text-xs font-semibold uppercase tracking-widest flex items-center gap-1.5"><AlertTriangle className="h-3.5 w-3.5 text-rose-500"/> AI Threat Insights</div>
              <div className="text-sm text-zinc-300">{t('actors.anomaliesDetected', 'Обнаружено аномальных паттернов: ')}<span className="text-amber-400 font-mono font-bold">12</span></div>
            </div>
            <Activity className="h-8 w-8 text-zinc-800" />
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 flex-1 min-h-[300px]">
        {/* Severity Pie Chart */}
        <div className="rounded-xl bg-black/20 border border-white/5 p-5 flex flex-col">
          <h4 className="text-sm font-semibold text-zinc-300 mb-4 flex items-center gap-2">
            <BarChart2 className="h-4 w-4 text-zinc-500"/>
            {t('actors.severityDistribution', 'Распределение по уровню угрозы')}
          </h4>
          <div className="flex-1 min-h-[200px]">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={stats.severityChartData}
                  cx="50%"
                  cy="50%"
                  innerRadius={60}
                  outerRadius={80}
                  paddingAngle={5}
                  dataKey="value"
                  stroke="none"
                >
                  {stats.severityChartData.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={entry.color} />
                  ))}
                </Pie>
                <Tooltip 
                  contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }}
                  itemStyle={{ color: '#e4e4e7', fontSize: '12px' }}
                />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Top Target Countries */}
        <div className="rounded-xl bg-black/20 border border-white/5 p-5 flex flex-col">
           <h4 className="text-sm font-semibold text-zinc-300 mb-4 flex items-center gap-2">
            <Target className="h-4 w-4 text-zinc-500"/>
            {t('actors.topCountries', 'Топ атакующих/жертв (Страны)')}
          </h4>
          <div className="flex-1 min-h-[200px]">
             <ResponsiveContainer width="100%" height="100%">
              <BarChart data={stats.topCountries} layout="vertical" margin={{ top: 0, right: 0, left: 0, bottom: 0 }}>
                <XAxis type="number" hide />
                <YAxis dataKey="country" type="category" hide />
                <Tooltip 
                  cursor={{fill: 'rgba(255,255,255,0.05)'}}
                  contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }}
                  labelStyle={{ display: 'none' }}
                />
                <Bar dataKey="count" fill="#fbbf24" radius={[0, 4, 4, 0]}>
                   {stats.topCountries.map((_entry, index) => (
                    <Cell key={`cell-${index}`} fill={`rgba(251, 191, 36, ${1 - index * 0.15})`} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
           {/* Custom Labels overlay */}
           <div className="mt-2 space-y-2">
              {stats.topCountries.map((c) => (
                <div key={c.country} className="flex justify-between items-center text-xs">
                  <span className="flex items-center gap-2"><CountryFlag code={c.country} className="text-sm"/> <span className="text-zinc-400">{c.country}</span></span>
                  <span className="font-mono text-zinc-500">{c.count} {t('actors.groups', 'гр.')}</span>
                </div>
              ))}
           </div>
        </div>
      </div>
    </motion.div>
  );
}

/* ── Dossier Panel (Right Pane Selected) ── */
function DossierPanel({ actor }: { actor: ThreatActor }) {
  const { t, i18n } = useTranslation();
  const sev = SEV[actor.severity];
  const fmtDate = (d: string) => new Date(d).toLocaleDateString(i18n.language, { day: 'numeric', month: 'short', year: 'numeric' });
  
  return (
    <motion.div
      key={actor.id}
      initial={{ opacity: 0, x: 20 }}
      animate={{ opacity: 1, x: 0 }}
      exit={{ opacity: 0, x: -20 }}
      transition={{ type: 'spring', stiffness: 350, damping: 30 }}
      className="h-full flex flex-col relative overflow-hidden rounded-2xl glass-strong border border-white/5"
    >
      <div className={cn('absolute top-0 left-0 right-0 h-1', sev.accent)} />
      
      <div className="flex-1 overflow-y-auto p-6 lg:p-8">
        {/* Header */}
        <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-4 mb-8">
          <div>
            <h3 className="flex items-center gap-3 text-2xl font-bold text-white tracking-tight">
              <Fingerprint className={cn('h-8 w-8 drop-shadow-lg', sev.text)} />
              {actor.name}
            </h3>
            <div className="mt-2 font-mono text-sm text-zinc-500 flex items-center gap-2">
              <span className="uppercase tracking-widest text-[10px] bg-zinc-800 px-1.5 py-0.5 rounded text-zinc-400">ID</span>
              {actor.id}
            </div>
          </div>
          
          <div className="flex flex-row sm:flex-col items-center sm:items-end gap-2">
            <span className={cn('rounded bg-white/5 px-2.5 py-1 text-xs font-bold uppercase tracking-wider ring-1 ring-inset shadow-sm', sev.chip)}>
                {t(`severity.${actor.severity}`)}
            </span>
             <div className="flex items-center gap-2 text-[11px] text-zinc-500 font-medium">
               <span>{t(`sophistication.${actor.sophistication}`)}</span>
               <span className="w-1 h-1 rounded-full bg-zinc-700"/>
               <span>{t(`intent.${actor.intent}`)}</span>
             </div>
          </div>
        </div>

        {/* Content */}
        <div className="space-y-8">
          {actor.description && (
            <div className="relative group">
               <div className="absolute -inset-0.5 bg-gradient-to-r from-amber-500/20 to-rose-500/20 rounded-xl blur opacity-20 group-hover:opacity-40 transition duration-500"></div>
               <div className="relative rounded-xl bg-black/60 border border-white/10 p-5 text-sm leading-relaxed text-zinc-300 shadow-inner">
                  <div className="flex items-center gap-2 text-[10px] uppercase tracking-widest text-amber-500/80 font-bold mb-3">
                    <Activity className="h-3.5 w-3.5"/> AI Analysis Report
                  </div>
                  {actor.description}
               </div>
            </div>
          )}

          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <Stat icon={Activity} label={t('actors.events')} value={formatInt(actor.eventCount)} />
            <Stat
              icon={Globe}
              label={t('actors.countries')}
              value={
                actor.countries.length > 0 ? (
                  <span className="flex flex-wrap items-center gap-1.5">
                    {actor.countries.slice(0, 3).map((c) => (
                      <CountryFlag key={c} code={c} className="text-base" />
                    ))}
                    {actor.countries.length > 3 && <span className="text-xs text-zinc-500">+{actor.countries.length - 3}</span>}
                  </span>
                ) : (
                  '—'
                )
              }
            />
            <Stat icon={Crosshair} label={t('actors.firstSeen')} value={fmtDate(actor.firstSeen)} />
            <Stat icon={ShieldAlert} label={t('actors.lastSeen')} value={fmtDate(actor.lastSeen)} />
          </div>

          <div>
            <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-zinc-300 border-b border-white/5 pb-2">
              <Terminal className="h-4 w-4 text-zinc-500" /> {t('actors.knownIps', 'Известная инфраструктура (IP)')}
            </div>
            <div className="rounded-xl bg-[#0a0a0c] border border-zinc-800/80 p-4 font-mono text-xs shadow-inner max-h-[200px] overflow-y-auto">
               {actor.knownIps.map((ip, idx) => (
                <div key={ip} className="flex items-center gap-3 py-1.5 hover:bg-white/5 px-2 rounded group transition-colors cursor-pointer" onClick={() => navigator.clipboard.writeText(ip)}>
                  <span className="text-zinc-600 select-none">{(idx + 1).toString().padStart(2, '0')}</span>
                  <span className="text-amber-400/80 group-hover:text-amber-400 transition-colors">{ip}</span>
                  <span className="text-zinc-600 text-[10px] opacity-0 group-hover:opacity-100 transition-opacity ml-auto tracking-widest">COPY</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </motion.div>
  );
}

function Stat({
  icon: Icon,
  label,
  value,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="rounded-xl bg-black/30 border border-white/5 p-4 shadow-inner">
      <div className="flex items-center gap-2 text-[10px] font-bold uppercase tracking-widest text-zinc-500 mb-2">
        <Icon className="h-3.5 w-3.5" /> {label}
      </div>
      <div className="truncate text-lg font-medium text-zinc-200">{value}</div>
    </div>
  );
}

function ListSkeleton() {
  return (
    <div className="space-y-3">
      {Array.from({ length: 5 }, (_, i) => (
        <div key={i} className="h-24 rounded-xl shimmer" />
      ))}
    </div>
  );
}

export function ThreatActorsPage() {
  const { t } = useTranslation();
  const { data, isPending, isError } = useActors();
  const [selectedActorId, setSelectedActorId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');

  const filteredActors = useMemo(() => {
    if (!data) return [];
    const q = searchQuery.toLowerCase();
    return data.filter(a => a.name.toLowerCase().includes(q) || a.id.toLowerCase().includes(q));
  }, [data, searchQuery]);

  const selectedActor = useMemo(() => {
    return data?.find(a => a.id === selectedActorId) || null;
  }, [data, selectedActorId]);

  return (
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="h-full flex flex-col space-y-4">
      {/* Header */}
      <div className="shrink-0">
        <h2 className="flex items-center gap-2 text-xl font-bold tracking-tight text-white">
          <Fingerprint className="h-5 w-5 text-amber-500" />
          {t('actors.title')}
        </h2>
        <p className="mt-1 text-sm text-zinc-500">{t('actors.subtitle')}</p>
      </div>

      {isError && (
        <div className="shrink-0 rounded-xl border border-rose-500/20 bg-rose-500/5 p-4 text-sm text-rose-400">
          {t('actors.loadError')}
        </div>
      )}

      {/* Main Two-Column Layout */}
      <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 gap-6">
        
        {/* Left Column: Actor List */}
        <div className="lg:col-span-4 xl:col-span-3 flex flex-col gap-4 h-[calc(100vh-140px)]">
          {/* Search Box */}
          <div className="relative shrink-0">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500" />
            <input 
              type="text" 
              placeholder={t('actors.searchPlaceholder', 'Поиск акторов...')}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full bg-black/40 border border-white/10 rounded-xl pl-9 pr-4 py-2.5 text-sm text-white placeholder:text-zinc-600 focus:outline-none focus:border-amber-500/50 focus:ring-1 focus:ring-amber-500/50 transition-all shadow-inner"
            />
          </div>

          {/* List */}
          <div className="flex-1 overflow-y-auto pr-2 pb-10 space-y-3">
             {isPending ? (
                <ListSkeleton />
              ) : (
                <>
                  {filteredActors.map((actor) => (
                    <ActorCard 
                      key={actor.id} 
                      actor={actor} 
                      selected={selectedActorId === actor.id}
                      onClick={() => setSelectedActorId(actor.id === selectedActorId ? null : actor.id)} 
                    />
                  ))}
                  {filteredActors.length === 0 && (
                    <div className="p-8 text-center text-sm text-zinc-500 border border-dashed border-white/10 rounded-xl">
                      {t('actors.empty', 'Ничего не найдено')}
                    </div>
                  )}
                </>
              )}
          </div>
        </div>

        {/* Right Column: Dossier / Global Stats */}
        <div className="hidden lg:block lg:col-span-8 xl:col-span-9 h-[calc(100vh-140px)] pb-10">
           <AnimatePresence mode="wait">
             {selectedActor ? (
               <DossierPanel key="dossier" actor={selectedActor} />
             ) : (
               data && <GlobalStatsDashboard key="global" actors={data} />
             )}
           </AnimatePresence>
        </div>

        {/* Mobile Dossier View (Overlay) */}
        <AnimatePresence>
          {selectedActor && (
             <motion.div
               className="fixed inset-0 z-50 flex flex-col bg-[#09090b] lg:hidden"
               initial={{ opacity: 0, y: '100%' }}
               animate={{ opacity: 1, y: 0 }}
               exit={{ opacity: 0, y: '100%' }}
               transition={{ type: 'spring', damping: 25, stiffness: 200 }}
             >
               <div className="p-4 bg-black/50 border-b border-white/5 flex items-center justify-between">
                 <span className="font-bold text-white">{t('actors.dossierTitle', 'Досье')}</span>
                 <button onClick={() => setSelectedActorId(null)} className="p-2 rounded-lg bg-white/5 text-zinc-400 hover:text-white transition-colors">{t('common.close', 'Закрыть')}</button>
               </div>
               <div className="flex-1 overflow-y-auto">
                 <DossierPanel actor={selectedActor} />
               </div>
             </motion.div>
          )}
        </AnimatePresence>
      </div>
    </motion.section>
  );
}
