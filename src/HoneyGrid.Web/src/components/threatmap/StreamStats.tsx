import { motion } from 'framer-motion';
import { Activity } from 'lucide-react';
import { useTranslation } from 'react-i18next';

/**
 * Top-left HUD panel — live stream counters. Glassmorphic, JetBrains Mono
 * figures, springs in from the left.
 */
export function StreamStats({
  total,
  critical,
  countries,
  live,
}: {
  total: number;
  critical: number;
  countries: number;
  live: boolean;
}) {
  const { t } = useTranslation();

  const items = [
    { value: total, label: t('threatMap.eventsShort'), tone: 'text-white' },
    { value: critical, label: t('threatMap.criticalShort'), tone: 'text-rose-400' },
    { value: countries, label: t('threatMap.countriesShort'), tone: 'text-amber-400' },
  ];

  return (
    <motion.div
      initial={{ opacity: 0, x: -24, filter: 'blur(6px)' }}
      animate={{ opacity: 1, x: 0, filter: 'blur(0px)' }}
      transition={{ type: 'spring', stiffness: 260, damping: 26 }}
      className="w-56 rounded-2xl border border-white/10 bg-zinc-900/20 p-4 shadow-2xl backdrop-blur-2xl"
    >
      <div className="mb-3 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Activity className="h-3.5 w-3.5 text-amber-500" />
          <span className="text-xs font-semibold uppercase tracking-wider text-zinc-300">
            {t('threatMap.streamStats')}
          </span>
        </div>
        <span className="relative flex h-2 w-2">
          {live && (
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
          )}
          <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
        </span>
      </div>

      <div className="grid grid-cols-3 gap-2">
        {items.map((it) => (
          <div key={it.label} className="rounded-lg bg-white/[0.03] py-2 text-center">
            <div className={`font-mono text-xl font-bold tabular-nums ${it.tone}`}>{it.value}</div>
            <div className="mt-0.5 text-[10px] uppercase tracking-wide text-zinc-500">{it.label}</div>
          </div>
        ))}
      </div>
    </motion.div>
  );
}
