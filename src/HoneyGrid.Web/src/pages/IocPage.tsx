import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Check, Copy, Download, Search, ShieldAlert, FileText, Share2, UserX, Target, Zap, Activity } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useIocsStix } from '@/api/queries';
import type { StixBundle, StixObject } from '@/types/api';

const TYPE_FILTERS = ['all', 'indicator', 'attack-pattern', 'relationship', 'identity'] as const;
type TypeFilter = (typeof TYPE_FILTERS)[number];

/* ── Type Icons & Styles ── */
function typeIcon(type: string) {
  switch (type) {
    case 'indicator': return Target;
    case 'attack-pattern': return Zap;
    case 'threat-actor': return UserX;
    case 'relationship': return Share2;
    default: return FileText;
  }
}

function typeBadgeStyle(type: string): string {
  switch (type) {
    case 'indicator':
      return 'bg-rose-500/20 text-rose-400 border border-rose-500/30 shadow-[inset_0_0_8px_rgba(225,29,72,0.3)]';
    case 'attack-pattern':
      return 'bg-amber-500/20 text-amber-400 border border-amber-500/30 shadow-[inset_0_0_8px_rgba(245,158,11,0.3)]';
    case 'threat-actor':
      return 'bg-orange-500/20 text-orange-400 border border-orange-500/30 shadow-[inset_0_0_8px_rgba(249,115,22,0.3)]';
    case 'relationship':
      return 'bg-blue-500/20 text-blue-400 border border-blue-500/30 shadow-[inset_0_0_8px_rgba(59,130,246,0.3)]';
    default:
      return 'bg-zinc-500/20 text-zinc-400 border border-zinc-500/30 shadow-[inset_0_0_8px_rgba(113,113,122,0.3)]';
  }
}

/* ── Tag Styling ── */
function TagBadge({ label }: { label: string }) {
  // MITRE ATT&CK technique IDs (e.g., T1110, T1053.003)
  const isMitre = /^T\d{4}(?:\.\d{3})?$/.test(label);
  const isMalicious = label.includes('malicious') || label.includes('command-and-control');

  return (
    <span className={cn(
      "rounded px-1.5 py-0.5 font-mono text-[10px] tracking-wider border transition-colors",
      isMitre ? "bg-red-500/10 text-red-400 border-red-500/20" :
      isMalicious ? "bg-amber-500/10 text-amber-400 border-amber-500/20" :
      "bg-white/5 text-zinc-400 border-white/10"
    )}>
      {label}
    </span>
  );
}

/* ── STIX Syntax Highlighter ── */
function StixPattern({ pattern }: { pattern: string }) {
  // Split the STIX pattern string into tokens for syntax highlighting
  const tokens = pattern.split(/(['].*?[']|\[|\]|\b=\b|\bAND\b|\bOR\b|\bIN\b)/g).filter(Boolean);

  return (
    <code className="flex-1 font-mono text-[11px] leading-relaxed break-all bg-black/50 border border-white/5 p-2.5 rounded-lg shadow-inner block w-full group-hover:border-amber-500/30 transition-colors">
      {tokens.map((token, i) => {
        if (token === '[' || token === ']') return <span key={i} className="text-purple-400 font-bold">{token}</span>;
        if (token === '=' || token === 'AND' || token === 'OR' || token === 'IN') return <span key={i} className="text-rose-400 font-bold mx-1">{token}</span>;
        if (token.startsWith("'")) return <span key={i} className="text-emerald-400">{token}</span>;
        return <span key={i} className="text-blue-300">{token}</span>;
      })}
    </code>
  );
}

/* ── Utilities ── */
function downloadBundle(bundle: StixBundle) {
  const blob = new Blob([JSON.stringify(bundle, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const date = new Date().toISOString().slice(0, 10);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `honeygrid-stix-${date}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      onClick={async () => {
        try {
          await navigator.clipboard?.writeText(value);
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1500);
        } catch {}
      }}
      className={cn(
        "p-1.5 rounded-md border transition-all duration-300 shadow-sm outline-none",
        copied 
          ? "bg-emerald-500/10 text-emerald-400 border-emerald-500/30" 
          : "bg-black/40 text-zinc-500 hover:text-amber-400 hover:bg-amber-500/10 hover:border-amber-500/30 border-white/5 opacity-0 group-hover:opacity-100 focus:opacity-100"
      )}
      title="Copy pattern"
    >
      {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
    </button>
  );
}

/* ── Holographic KPI ── */
function SummaryStat({ label, value, icon: Icon, color, delay }: { label: string; value: number; icon: any; color: string; delay: number }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      className="relative rounded-2xl glass-strong p-5 overflow-hidden group border border-white/5 hover:border-white/10 transition-colors"
    >
      <div className={cn("absolute -top-10 -right-10 w-24 h-24 rounded-full blur-2xl opacity-20 group-hover:opacity-40 transition-opacity duration-500", color)} />
      <div className="flex items-center gap-3">
        <div className={cn("p-2 rounded-xl bg-black/40 border border-white/5 shadow-inner", color.replace('bg-', 'text-'))}>
           <Icon className="h-5 w-5" />
        </div>
        <div>
          <div className="text-[10px] font-bold uppercase tracking-widest text-zinc-500 group-hover:text-zinc-400 transition-colors">{label}</div>
          <div className="text-3xl font-bold tabular-nums font-mono text-white mt-0.5">{value}</div>
        </div>
      </div>
    </motion.div>
  );
}

export function IocPage() {
  const { t, i18n } = useTranslation();
  const { data, isPending, isError } = useIocsStix();
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('all');
  const [search, setSearch] = useState('');

  const TYPE_LABELS: Record<string, string> = {
    indicator: t('ioc.typeIndicator', 'Индикатор'),
    'attack-pattern': t('ioc.typePattern', 'Шаблон атаки'),
    'threat-actor': t('ioc.typeActor', 'Актор угроз'),
    relationship: t('ioc.typeRel', 'Связь'),
    identity: t('ioc.typeId', 'Идентичность'),
  };

  const objects = useMemo<StixObject[]>(() => data?.objects ?? [], [data]);

  const counts = useMemo(() => {
    const byType = (typ: string) => objects.filter((o) => o.type === typ).length;
    return {
      indicators: byType('indicator'),
      patterns: byType('attack-pattern'),
      actors: byType('threat-actor'),
      total: objects.length,
    };
  }, [objects]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return objects.filter((o) => {
      if (typeFilter !== 'all' && o.type !== typeFilter) return false;
      if (q) {
        const haystack = `${o.pattern ?? ''} ${String(o.name ?? '')} ${(o.labels ?? []).join(' ')}`;
        if (!haystack.toLowerCase().includes(q)) return false;
      }
      return true;
    });
  }, [objects, typeFilter, search]);

  return (
    <motion.section
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className="space-y-6 pb-10"
    >
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-white flex items-center gap-3">
            <ShieldAlert className="h-6 w-6 text-amber-500 drop-shadow-[0_0_8px_rgba(245,158,11,0.5)]" />
            {t('ioc.title', 'Индикаторы компрометации (STIX)')}
          </h2>
          <p className="mt-1 text-sm text-zinc-400 max-w-2xl">
            {t('ioc.subtitle', 'Индикаторы компрометации в формате STIX 2.1: вредоносные IP-адреса, хеши файлов, шаблоны атак и связи — готовы к импорту в системы SIEM.')}
          </p>
        </div>
        
        {/* Glowing Action Button */}
        <div className="relative group">
          <div className="absolute -inset-0.5 bg-gradient-to-r from-amber-500 to-orange-600 rounded-lg blur opacity-40 group-hover:opacity-75 transition duration-500 group-disabled:opacity-0" />
          <button
            data-testid="export-stix"
            onClick={() => data && downloadBundle(data)}
            disabled={!data}
            className="relative flex items-center gap-2 rounded-lg bg-black border border-white/10 hover:border-amber-500/50 text-amber-400 hover:text-amber-300 hover:bg-black/80 px-5 py-2.5 text-sm font-bold tracking-wide transition-all duration-300 disabled:opacity-50 disabled:pointer-events-none disabled:border-white/5"
          >
            <Download className="h-4 w-4" /> {t('ioc.export', 'Экспорт STIX bundle')}
          </button>
        </div>
      </div>

      {isPending && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            {Array.from({ length: 4 }, (_, i) => (
              <div key={i} className="h-24 rounded-2xl shimmer" />
            ))}
          </div>
          <div className="h-[500px] rounded-2xl shimmer" />
        </div>
      )}

      {isError && (
        <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-400 shadow-inner">
          {t('ioc.loadError', 'Ошибка загрузки данных')}
        </div>
      )}

      {data && (
        <>
          {/* KPIs */}
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <SummaryStat label={t('ioc.statIndicators', 'Индикаторы')} value={counts.indicators} icon={Target} color="bg-rose-500" delay={0.0} />
            <SummaryStat label={t('ioc.statPatterns', 'Шаблоны атак')} value={counts.patterns} icon={Zap} color="bg-amber-500" delay={0.05} />
            <SummaryStat label={t('ioc.statActors', 'Акторы угроз')} value={counts.actors} icon={UserX} color="bg-orange-500" delay={0.1} />
            <SummaryStat label={t('ioc.statTotal', 'Всего объектов')} value={counts.total} icon={Activity} color="bg-blue-500" delay={0.15} />
          </div>

          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2, type: 'spring', stiffness: 300, damping: 30 }}
            className="rounded-2xl glass-strong border border-white/5 overflow-hidden flex flex-col shadow-2xl relative"
          >
            {/* Header / Filters */}
            <div className="p-5 border-b border-white/5 bg-black/20 space-y-4 lg:space-y-0 lg:flex lg:items-center lg:justify-between">
              <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                 <FileText className="h-4 w-4 text-amber-500" />
                 {t('ioc.cardTitle', 'Объекты STIX')}
              </h3>
              
              <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-3">
                {/* Segmented Control */}
                <div className="flex bg-black/50 p-1 rounded-lg border border-white/5 shadow-inner overflow-x-auto custom-scrollbar">
                  {TYPE_FILTERS.map((t_filter) => (
                    <button
                      key={t_filter}
                      onClick={() => setTypeFilter(t_filter)}
                      className={cn(
                        'px-3 py-1.5 text-[11px] font-bold uppercase tracking-wider rounded-md transition-all duration-300 whitespace-nowrap',
                        typeFilter === t_filter ? 'bg-amber-500/20 text-amber-400 shadow-sm border-amber-500/30' : 'text-zinc-500 hover:text-zinc-300 hover:bg-white/5 border-transparent'
                      )}
                      style={{ borderWidth: '1px' }}
                    >
                      {t_filter === 'all' ? t('ioc.typeAll', 'Все') : TYPE_LABELS[t_filter]}
                    </button>
                  ))}
                </div>
                
                {/* Search */}
                <div className="relative group/search shrink-0 sm:w-64">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500 group-focus-within/search:text-amber-500 transition-colors" />
                  <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder={t('ioc.searchPlaceholder', 'Поиск по шаблону / названию...')}
                    className="h-9 w-full rounded-lg border border-white/10 bg-black/40 pl-9 pr-4 text-xs text-white placeholder-zinc-500 focus:outline-none focus:ring-1 focus:ring-amber-500/50 focus:border-amber-500/50 transition-all shadow-inner"
                  />
                </div>
              </div>
            </div>

            {/* Table */}
            <div className="overflow-x-auto">
              <div className="min-w-[800px]">
                <div className="grid grid-cols-[160px_1fr_200px_100px] gap-4 border-b border-white/[0.05] px-6 py-3 text-[10px] font-bold uppercase tracking-widest text-zinc-500 bg-black/40">
                  <span>{t('ioc.colType', 'ТИП')}</span>
                  <span>{t('ioc.colPattern', 'ШАБЛОН / НАЗВАНИЕ')}</span>
                  <span>{t('ioc.colLabels', 'МЕТКИ')}</span>
                  <span className="text-right">{t('ioc.colCreated', 'СОЗДАНО')}</span>
                </div>
                
                <div className="max-h-[600px] overflow-y-auto custom-scrollbar bg-black/10">
                  {filtered.length === 0 ? (
                    <div className="p-12 text-center text-zinc-500 font-mono text-sm">
                      {t('ioc.empty', 'Нет объектов, соответствующих фильтрам')}
                    </div>
                  ) : (
                    filtered.map((o) => {
                      const Icon = typeIcon(o.type);
                      return (
                        <div
                          key={o.id}
                          className="group grid grid-cols-[160px_1fr_200px_100px] items-start gap-4 border-b border-white/[0.03] hover:bg-white/[0.03] transition-colors last:border-0 px-6 py-4 relative"
                        >
                          {/* Hover left accent line */}
                          <div className="absolute left-0 top-0 bottom-0 w-0.5 bg-amber-500 opacity-0 group-hover:opacity-100 transition-opacity" />

                          {/* Type */}
                          <div className="pt-1">
                            <span className={cn('inline-flex items-center gap-1.5 rounded-md px-2 py-1 font-mono text-[10px] font-bold tracking-widest uppercase', typeBadgeStyle(o.type))}>
                              <Icon className="h-3 w-3" />
                              {TYPE_LABELS[o.type] ?? o.type}
                            </span>
                          </div>

                          {/* Pattern / Name */}
                          <div className="flex items-start gap-3 min-w-0 pr-4">
                            {o.pattern ? (
                              <StixPattern pattern={o.pattern} />
                            ) : (
                              <span className="text-sm font-semibold text-zinc-200 mt-0.5">{String(o.name ?? '—')}</span>
                            )}
                            {o.pattern && <div className="mt-1 shrink-0"><CopyButton value={o.pattern} /></div>}
                          </div>

                          {/* Labels */}
                          <div className="flex flex-wrap gap-1.5 pt-1">
                            {(o.labels ?? []).map((l) => (
                              <TagBadge key={l} label={l} />
                            ))}
                          </div>

                          {/* Created */}
                          <div className="font-mono text-[11px] font-bold text-zinc-500 text-right pt-1 group-hover:text-amber-500/80 transition-colors">
                            {new Date(o.created).toLocaleDateString(i18n.language)}
                          </div>
                        </div>
                      );
                    })
                  )}
                </div>
              </div>
            </div>
          </motion.div>
        </>
      )}
    </motion.section>
  );
}
