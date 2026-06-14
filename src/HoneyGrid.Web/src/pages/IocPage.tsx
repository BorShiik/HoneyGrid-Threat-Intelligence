import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Check, Copy, Download, Search, ShieldAlert, FileText, Share2, UserX } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useIocsStix } from '@/api/queries';
import type { StixBundle, StixObject } from '@/types/api';

const TYPE_FILTERS = ['all', 'indicator', 'attack-pattern', 'relationship', 'identity'] as const;
type TypeFilter = (typeof TYPE_FILTERS)[number];

function typeIcon(type: string) {
  switch (type) {
    case 'indicator': return ShieldAlert;
    case 'attack-pattern': return FileText;
    case 'threat-actor': return UserX;
    case 'relationship': return Share2;
    default: return FileText;
  }
}

function typeBadgeStyle(type: string): string {
  switch (type) {
    case 'indicator':
      return 'bg-rose-500/10 text-rose-400 ring-rose-500/20';
    case 'attack-pattern':
      return 'bg-amber-500/10 text-amber-400 ring-amber-500/20';
    case 'threat-actor':
      return 'bg-orange-500/10 text-orange-400 ring-orange-500/20';
    case 'relationship':
      return 'bg-blue-500/10 text-blue-400 ring-blue-500/20';
    default:
      return 'bg-zinc-500/10 text-zinc-400 ring-zinc-500/20';
  }
}

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
          window.setTimeout(() => setCopied(false), 1200);
        } catch {}
      }}
      className="p-1 rounded-md hover:bg-white/10 text-zinc-500 hover:text-zinc-300 transition-colors"
      title="Copy"
    >
      {copied ? <Check className="h-3.5 w-3.5 text-emerald-400" /> : <Copy className="h-3.5 w-3.5" />}
    </button>
  );
}

function SummaryStat({ label, value, delay }: { label: string; value: number; delay: number }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-xl glass-strong p-4"
    >
      <div className="text-2xl font-bold tabular-nums text-white">{value}</div>
      <div className="text-xs text-zinc-500 mt-1">{label}</div>
    </motion.div>
  );
}

export function IocPage() {
  const { t } = useTranslation();
  const { data, isPending, isError } = useIocsStix();
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('all');
  const [search, setSearch] = useState('');

  const TYPE_LABELS: Record<string, string> = {
    indicator: t('ioc.typeIndicator'),
    'attack-pattern': t('ioc.typePattern'),
    'threat-actor': t('ioc.typeActor'),
    relationship: t('ioc.typeRel'),
    identity: t('ioc.typeId'),
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
      className="space-y-5"
    >
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
            <ShieldAlert className="h-5 w-5 text-amber-500" />
            {t('ioc.title')}
          </h2>
          <p className="mt-1 text-sm text-zinc-500 max-w-2xl">
            {t('ioc.subtitle')}
          </p>
        </div>
        <button
          onClick={() => data && downloadBundle(data)}
          disabled={!data}
          className="flex items-center gap-2 rounded-lg bg-amber-500 hover:bg-amber-400 text-amber-950 px-4 py-2 text-sm font-medium transition-colors disabled:opacity-50 disabled:pointer-events-none"
        >
          <Download className="h-4 w-4" /> {t('ioc.export')}
        </button>
      </div>

      {isPending && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {Array.from({ length: 4 }, (_, i) => (
              <div key={i} className="h-20 rounded-xl shimmer" />
            ))}
          </div>
          <div className="h-[500px] rounded-xl shimmer" />
        </div>
      )}

      {isError && (
        <div className="rounded-xl border border-rose-500/20 bg-rose-500/5 p-4 text-sm text-rose-400">
          {t('ioc.loadError')}
        </div>
      )}

      {data && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <SummaryStat label={t('ioc.statIndicators')} value={counts.indicators} delay={0.0} />
            <SummaryStat label={t('ioc.statPatterns')} value={counts.patterns} delay={0.05} />
            <SummaryStat label={t('ioc.statActors')} value={counts.actors} delay={0.1} />
            <SummaryStat label={t('ioc.statTotal')} value={counts.total} delay={0.15} />
          </div>

          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2, type: 'spring', stiffness: 300, damping: 30 }}
            className="rounded-xl glass-strong overflow-hidden flex flex-col"
          >
            {/* Header / Filters */}
            <div className="p-4 border-b border-white/5 space-y-4 sm:space-y-0 sm:flex sm:items-center sm:justify-between">
              <h3 className="text-sm font-semibold text-zinc-200">{t('ioc.cardTitle')}</h3>
              
              <div className="flex flex-wrap items-center gap-3">
                <div className="flex bg-black/40 rounded-lg p-0.5">
                  {TYPE_FILTERS.map((t_filter) => (
                    <button
                      key={t_filter}
                      onClick={() => setTypeFilter(t_filter)}
                      className={cn(
                        'px-2.5 py-1 text-xs font-medium rounded-md transition-colors',
                        typeFilter === t_filter ? 'bg-white/10 text-white' : 'text-zinc-500 hover:text-zinc-300'
                      )}
                    >
                      {t_filter === 'all' ? t('ioc.typeAll') : TYPE_LABELS[t_filter]}
                    </button>
                  ))}
                </div>
                
                <div className="relative">
                  <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
                  <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder={t('ioc.searchPlaceholder')}
                    className="h-8 w-full sm:w-64 rounded-md border border-white/10 bg-black/30 pl-8 pr-3 text-xs text-white placeholder-zinc-500 focus:outline-none focus:ring-1 focus:ring-amber-500/50"
                  />
                </div>
              </div>
            </div>

            {/* Table */}
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead>
                  <tr className="border-b border-white/[0.04] bg-zinc-900/30 text-[10px] uppercase tracking-wider text-zinc-500">
                    <th className="px-4 py-2 font-semibold w-32">{t('ioc.colType')}</th>
                    <th className="px-4 py-2 font-semibold">{t('ioc.colPattern')}</th>
                    <th className="px-4 py-2 font-semibold w-48">{t('ioc.colLabels')}</th>
                    <th className="px-4 py-2 font-semibold w-32">{t('ioc.colCreated')}</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="p-8 text-center text-zinc-500">
                        {t('ioc.empty')}
                      </td>
                    </tr>
                  ) : (
                    filtered.map((o, i) => {
                      const Icon = typeIcon(o.type);
                      return (
                        <motion.tr
                          initial={{ opacity: 0, x: -8 }}
                          animate={{ opacity: 1, x: 0 }}
                          transition={{ delay: 0.1 + Math.min(i, 20) * 0.02 }}
                          key={o.id}
                          className="border-b border-white/[0.03] hover:bg-white/[0.02] transition-colors last:border-0"
                        >
                          <td className="px-4 py-3 align-top">
                            <span className={cn('inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 font-mono text-[10px] font-semibold ring-1 ring-inset', typeBadgeStyle(o.type))}>
                              <Icon className="h-3 w-3" />
                              {TYPE_LABELS[o.type] ?? o.type}
                            </span>
                          </td>
                          <td className="px-4 py-3 align-top">
                            {o.pattern ? (
                              <div className="flex items-start gap-2">
                                <code className="flex-1 font-mono text-xs text-zinc-300 whitespace-pre-wrap break-all leading-relaxed">
                                  {o.pattern}
                                </code>
                                <div className="mt-0.5"><CopyButton value={o.pattern} /></div>
                              </div>
                            ) : (
                              <span className="text-xs text-zinc-300">{String(o.name ?? '—')}</span>
                            )}
                          </td>
                          <td className="px-4 py-3 align-top">
                            <div className="flex flex-wrap gap-1">
                              {(o.labels ?? []).map((l) => (
                                <span key={l} className="rounded bg-white/5 px-1.5 py-0.5 font-mono text-[10px] text-zinc-400">
                                  {l}
                                </span>
                              ))}
                            </div>
                          </td>
                          <td className="px-4 py-3 align-top font-mono text-xs text-zinc-500">
                            {new Date(o.created).toLocaleDateString()}
                          </td>
                        </motion.tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </motion.div>
        </>
      )}
    </motion.section>
  );
}
