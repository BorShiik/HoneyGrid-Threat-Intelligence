import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Fingerprint,
  X,
  Activity,
  Globe,
  Crosshair,
  ShieldAlert,
  Terminal,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';
import { CountryFlag } from '@/components/ui/CountryFlag';
import { useActors } from '@/api/queries';
import type { Severity, ThreatActor } from '@/types/api';

/* ── Severity palette (colours only; labels come from i18n) ── */
const SEV: Record<Severity, { text: string; chip: string; accent: string; glow: string }> = {
  critical: {
    text: 'text-rose-400',
    chip: 'bg-rose-500/10 text-rose-400 ring-rose-500/20',
    accent: 'bg-rose-500',
    glow: 'shadow-[0_0_24px_-6px_rgba(225,29,72,0.55)]',
  },
  high: {
    text: 'text-orange-400',
    chip: 'bg-orange-500/10 text-orange-400 ring-orange-500/20',
    accent: 'bg-orange-500',
    glow: 'shadow-[0_0_22px_-7px_rgba(249,115,22,0.5)]',
  },
  medium: {
    text: 'text-amber-400',
    chip: 'bg-amber-500/10 text-amber-400 ring-amber-500/20',
    accent: 'bg-amber-500',
    glow: 'shadow-[0_0_20px_-8px_rgba(245,158,11,0.45)]',
  },
  low: {
    text: 'text-blue-400',
    chip: 'bg-blue-500/10 text-blue-400 ring-blue-500/20',
    accent: 'bg-blue-500',
    glow: '',
  },
};

/* ── Actor card ── */
function ActorCard({ actor, index, onClick }: { actor: ThreatActor; index: number; onClick: () => void }) {
  const { t } = useTranslation();
  const sev = SEV[actor.severity];
  return (
    <motion.button
      type="button"
      onClick={onClick}
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: index * 0.05, type: 'spring', stiffness: 300, damping: 30 }}
      whileHover={{ y: -3 }}
      className={cn('group relative overflow-hidden rounded-xl glass-strong p-4 text-left transition-shadow', sev.glow)}
    >
      <div className={cn('absolute top-0 left-0 right-0 h-[2px]', sev.accent)} />
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <Fingerprint className={cn('h-4 w-4 shrink-0', sev.text)} />
            <span className="truncate font-semibold text-white">{actor.name}</span>
          </div>
          <div className="mt-0.5 font-mono text-[11px] text-zinc-600">{actor.id}</div>
        </div>
        <span className={cn('shrink-0 rounded-md px-2 py-0.5 text-[10px] font-medium ring-1', sev.chip)}>
          {t(`severity.${actor.severity}`)}
        </span>
      </div>

      <div className="mt-3 flex items-baseline gap-1.5">
        <span className="font-mono text-2xl font-bold tabular-nums text-white">{formatInt(actor.eventCount)}</span>
        <span className="text-xs text-zinc-500">{t('actors.events')}</span>
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        {actor.countries.slice(0, 5).map((c) => (
          <span key={c} className="text-base leading-none" title={c}>
            <CountryFlag code={c} className="text-base" />
          </span>
        ))}
        <span className="ml-auto text-[11px] text-zinc-500">{t(`sophistication.${actor.sophistication}`)}</span>
      </div>
    </motion.button>
  );
}

/* ── Dossier modal ── */
function DossierModal({ actor, onClose }: { actor: ThreatActor; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const sev = SEV[actor.severity];
  const fmtDate = (d: string) => new Date(d).toLocaleDateString(i18n.language);
  return (
    <motion.div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
    >
      <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />
      <motion.div
        initial={{ opacity: 0, scale: 0.96, y: 12 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.96, y: 12 }}
        transition={{ type: 'spring', stiffness: 320, damping: 30 }}
        className="relative w-full max-w-lg overflow-hidden rounded-2xl glass-strong"
      >
        <div className={cn('absolute top-0 left-0 right-0 h-[3px]', sev.accent)} />
        <div className="flex items-start justify-between gap-3 p-5 pb-3">
          <div>
            <h3 className="flex items-center gap-2 text-lg font-bold text-white">
              <Fingerprint className={cn('h-5 w-5', sev.text)} />
              {actor.name}
            </h3>
            <div className="mt-0.5 font-mono text-xs text-zinc-600">
              {t('actors.dossierTitle')} · {actor.id}
            </div>
          </div>
          <button
            onClick={onClose}
            className="rounded-lg p-1.5 text-zinc-500 hover:bg-white/5 hover:text-white transition-colors"
            aria-label={t('common.close')}
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-4 px-5 pb-5">
          <div className="flex flex-wrap gap-2">
            <span className={cn('rounded-md px-2 py-0.5 text-[11px] font-medium ring-1', sev.chip)}>
              {t(`severity.${actor.severity}`)}
            </span>
            <span className="rounded-md bg-white/5 px-2 py-0.5 text-[11px] text-zinc-300 ring-1 ring-white/10">
              {t(`sophistication.${actor.sophistication}`)}
            </span>
            <span className="rounded-md bg-white/5 px-2 py-0.5 text-[11px] text-zinc-300 ring-1 ring-white/10">
              {t(`intent.${actor.intent}`)}
            </span>
          </div>

          {actor.description && (
            <p className="rounded-lg bg-black/30 border border-white/5 p-3 text-sm leading-relaxed text-zinc-300">
              {actor.description}
            </p>
          )}

          <div className="grid grid-cols-2 gap-2.5">
            <Stat icon={Activity} label={t('actors.events')} value={formatInt(actor.eventCount)} />
            <Stat
              icon={Globe}
              label={t('actors.countries')}
              value={
                actor.countries.length > 0 ? (
                  <span className="flex flex-wrap items-center gap-1.5">
                    {actor.countries.map((c) => (
                      <CountryFlag key={c} code={c} className="text-base" />
                    ))}
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
            <div className="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-zinc-400">
              <Terminal className="h-3.5 w-3.5" /> {t('actors.knownIps')}
            </div>
            <div className="flex flex-wrap gap-1.5">
              {actor.knownIps.map((ip) => (
                <code key={ip} className="rounded-md bg-black/40 px-1.5 py-0.5 font-mono text-[11px] text-zinc-300 ring-1 ring-white/5">
                  {ip}
                </code>
              ))}
            </div>
          </div>
        </div>
      </motion.div>
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
  value: string;
}) {
  return (
    <div className="rounded-lg bg-black/30 border border-white/5 px-3 py-2">
      <div className="flex items-center gap-1.5 text-[11px] text-zinc-500">
        <Icon className="h-3 w-3" /> {label}
      </div>
      <div className="mt-0.5 truncate text-sm font-medium text-zinc-200">{value}</div>
    </div>
  );
}

function CardSkeleton() {
  return <div className="h-32 rounded-xl shimmer" />;
}

export function ThreatActorsPage() {
  const { t } = useTranslation();
  const { data, isPending, isError } = useActors();
  const [selected, setSelected] = useState<ThreatActor | null>(null);

  return (
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-5">
      <div>
        <h2 className="flex items-center gap-2 text-xl font-bold tracking-tight text-white">
          <Fingerprint className="h-5 w-5 text-amber-500" />
          {t('actors.title')}
        </h2>
        <p className="mt-1 text-sm text-zinc-500">{t('actors.subtitle')}</p>
      </div>

      {isError && (
        <div className="rounded-xl border border-rose-500/20 bg-rose-500/5 p-4 text-sm text-rose-400">
          {t('actors.loadError')}
        </div>
      )}

      {isPending ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <CardSkeleton key={i} />
          ))}
        </div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {(data ?? []).map((actor, i) => (
            <ActorCard key={actor.id} actor={actor} index={i} onClick={() => setSelected(actor)} />
          ))}
          {data && data.length === 0 && (
            <p className="col-span-full rounded-xl glass p-8 text-center text-sm text-zinc-500">{t('actors.empty')}</p>
          )}
        </div>
      )}

      <AnimatePresence>
        {selected && <DossierModal actor={selected} onClose={() => setSelected(null)} />}
      </AnimatePresence>
    </motion.section>
  );
}
