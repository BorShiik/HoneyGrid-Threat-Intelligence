import { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { eventTypeKey, formatInt } from '@/lib/format';
import type { HoneypotEventType } from '@/types/api';

/**
 * Live "frequency equalizer" for event types — replaces horizontal bars.
 *
 * Each category is a stack of vertical blocks whose heights jitter on a timer,
 * weighted by that category's real share of traffic, so a busy channel visibly
 * "sings" louder. Purely cosmetic motion layered over real proportions.
 */

const BARS = 7;
const TICK_MS = 550;

const TYPE_HEX: Record<string, string> = {
  loginFailed: '#f43f5e',
  loginSuccess: '#e11d48',
  httpRequest: '#f59e0b',
  command: '#a855f7',
  connect: '#3b82f6',
};

function EqualizerColumn({ intensity, hex }: { intensity: number; hex: string }) {
  // `intensity` in [0,1] sets the mean energy; bars wobble around it.
  const [levels, setLevels] = useState<number[]>(() => Array.from({ length: BARS }, () => intensity));

  useEffect(() => {
    const id = setInterval(() => {
      setLevels(
        Array.from({ length: BARS }, () => {
          const wobble = (Math.random() - 0.45) * 0.5;
          return Math.min(1, Math.max(0.08, intensity + wobble));
        }),
      );
    }, TICK_MS + Math.random() * 200);
    return () => clearInterval(id);
  }, [intensity]);

  return (
    <div className="flex h-24 items-end justify-center gap-[3px]">
      {levels.map((lvl, i) => (
        <motion.span
          key={i}
          className="w-1.5 rounded-sm"
          animate={{ height: `${lvl * 100}%` }}
          transition={{ type: 'spring', stiffness: 200, damping: 18 }}
          style={{
            background: `linear-gradient(to top, ${hex}, ${hex}55)`,
            boxShadow: `0 0 6px ${hex}55`,
          }}
        />
      ))}
    </div>
  );
}

export function EventEqualizer({
  data,
  delay = 0,
}: {
  data: Record<HoneypotEventType, number>;
  delay?: number;
}) {
  const { t } = useTranslation();
  const types = (Object.keys(data) as HoneypotEventType[])
    .filter((k) => data[k] > 0)
    .sort((a, b) => data[b] - data[a])
    .slice(0, 4);
  const max = Math.max(1, ...types.map((k) => data[k]));

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 280, damping: 28 }}
      className="rounded-2xl glass-strong p-5"
    >
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-zinc-200">{t('dashboard.eventsByType')}</h3>
        <span className="font-mono text-[10px] uppercase tracking-widest text-emerald-400">
          ● {t('liveFeed.live')}
        </span>
      </div>

      <div className="grid grid-cols-4 gap-3">
        {types.map((type) => {
          const key = eventTypeKey(type);
          const hex = TYPE_HEX[key] ?? '#f59e0b';
          // Floor the visual energy so even quiet channels show signal.
          const intensity = 0.35 + (data[type] / max) * 0.6;
          return (
            <div key={type} className="flex flex-col items-center gap-2">
              <EqualizerColumn intensity={intensity} hex={hex} />
              <div className="w-full text-center">
                <div className="font-mono text-sm font-bold tabular-nums text-white">
                  {formatInt(data[type])}
                </div>
                <div className="mt-0.5 truncate text-[10px] text-zinc-500" title={t(`eventType.${key}`)}>
                  {t(`eventType.${key}`)}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </motion.div>
  );
}
