import { AnimatePresence, motion } from 'framer-motion';
import { Terminal } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { eventSeverity, eventTypeKey, SENSOR_LABELS } from '@/lib/format';
import { scoreColor } from './globeMath';
import type { HoneypotEvent } from '@/types/api';

/**
 * Bottom-left HUD — a terminal-style console of inbound attacks. New lines
 * slide up from the bottom (newest at the top of the stack), critical hits
 * flagged red. Strictly mono.
 */
export function ThreatTicker({ events }: { events: HoneypotEvent[] }) {
  const { t } = useTranslation();
  const rows = events.slice(0, 6);

  return (
    <motion.div
      initial={{ opacity: 0, y: 24, filter: 'blur(6px)' }}
      animate={{ opacity: 1, y: 0, filter: 'blur(0px)' }}
      transition={{ type: 'spring', stiffness: 260, damping: 26, delay: 0.1 }}
      className="w-[22rem] max-w-[80vw] overflow-hidden rounded-2xl border border-white/10 bg-zinc-900/20 shadow-2xl backdrop-blur-2xl"
    >
      <div className="flex items-center gap-2 border-b border-white/5 px-3 py-2">
        <Terminal className="h-3.5 w-3.5 text-emerald-400" />
        <span className="font-mono text-[10px] uppercase tracking-widest text-zinc-400">
          honeygrid@core:~$ tail -f /var/log/threats
        </span>
      </div>

      <div className="h-[148px] space-y-0.5 overflow-hidden p-2">
        <AnimatePresence initial={false} mode="popLayout">
          {rows.map((e) => {
            const critical = eventSeverity(e) === 'critical';
            const color = scoreColor(e.threatIntel?.score ?? 0);
            return (
              <motion.div
                key={e.id}
                layout
                initial={{ opacity: 0, y: 18 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, height: 0 }}
                transition={{ type: 'spring', stiffness: 420, damping: 32 }}
                className="flex items-center gap-2 font-mono text-[11px] leading-tight"
              >
                <span className="text-zinc-600">
                  {new Date(e.timestamp).toLocaleTimeString('en-GB', { hour12: false })}
                </span>
                <span className="shrink-0" style={{ color }}>
                  ▸
                </span>
                <span className="w-9 shrink-0 text-zinc-500">{SENSOR_LABELS[e.sensorType]}</span>
                <span className="truncate text-zinc-200">{e.attackerIp}</span>
                <span className={`ml-auto shrink-0 ${critical ? 'text-rose-400' : 'text-zinc-500'}`}>
                  {t(`eventType.${eventTypeKey(e.eventType)}`)}
                </span>
              </motion.div>
            );
          })}
        </AnimatePresence>
        {rows.length === 0 && (
          <div className="py-8 text-center font-mono text-xs text-zinc-600">{t('common.waiting')}</div>
        )}
      </div>
    </motion.div>
  );
}
