import React from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import {
  SENSOR_LABELS,
  eventDetails,
  eventSeverity,
  eventTypeKey,
} from '@/lib/format';
import type { HoneypotEvent } from '@/types/api';

/**
 * Live "digital rain" feed. Borderless — every row is a discrete glass plate.
 * New events slide in from the top with a one-shot RGB glitch; critical events
 * (successful logins / high-score malicious IPs) breathe red. Strictly mono.
 *
 * Structured for streaming: pass the newest-first rolling buffer from
 * `useLiveAttacks` (SignalR-backed in prod, simulator in dev). `key={e.id}`
 * drives the enter animation as the buffer head changes.
 */

const PROTO_TONE: Record<string, string> = {
  ssh: 'bg-pink-500/10 text-pink-300 ring-pink-500/25',
  web: 'bg-amber-500/10 text-amber-300 ring-amber-500/25',
  rdp: 'bg-blue-500/10 text-blue-300 ring-blue-500/25',
};

const SEV_DOT: Record<string, string> = {
  critical: 'bg-rose-500 shadow-[0_0_8px_#f43f5e]',
  high: 'bg-orange-500',
  medium: 'bg-amber-500',
  low: 'bg-blue-500',
};

function fmtTime(ts: string): string {
  return new Date(ts).toLocaleTimeString('en-GB', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

const FeedRow = React.forwardRef<HTMLDivElement, { event: HoneypotEvent }>(({ event }, ref) => {
  const { t } = useTranslation();
  const sev = eventSeverity(event);
  const critical = sev === 'critical';

  return (
    <motion.div
      ref={ref}
      initial={{ opacity: 0, y: -8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, height: 0, marginBottom: 0, overflow: 'hidden' }}
      transition={{ type: 'spring', stiffness: 400, damping: 30 }}
      className={cn(
        'grid grid-cols-[64px_52px_1fr] items-center gap-x-3 gap-y-1 rounded-lg border border-white/5 bg-white/[0.02] px-3 py-2 sm:grid-cols-[72px_56px_130px_1fr_1.4fr]',
        critical && 'critical-pulse border-rose-500/30',
      )}
    >
      {/* Time */}
      <span className="font-mono text-[11px] tabular-nums text-zinc-500">{fmtTime(event.timestamp)}</span>

      {/* Protocol */}
      <span
        className={cn(
          'inline-flex w-fit items-center rounded px-1.5 py-0.5 font-mono text-[10px] font-bold uppercase ring-1 ring-inset',
          PROTO_TONE[event.sensorType],
        )}
      >
        {SENSOR_LABELS[event.sensorType]}
      </span>

      {/* Source IP */}
      <span className="hidden truncate font-mono text-xs text-zinc-200 sm:block">{event.attackerIp}</span>

      {/* Type */}
      <span className="col-span-2 flex items-center gap-1.5 sm:col-span-1">
        <span className={cn('h-1.5 w-1.5 shrink-0 rounded-full', SEV_DOT[sev])} />
        <span className={cn('font-mono text-[11px]', critical ? 'text-rose-300' : 'text-zinc-300')}>
          {t(`eventType.${eventTypeKey(event.eventType)}`)}
        </span>
      </span>

      {/* Payload */}
      <span className="col-span-3 truncate font-mono text-[11px] text-zinc-500 sm:col-span-1">
        <span className="text-zinc-700">›</span> {eventDetails(event)}
      </span>
    </motion.div>
  );
});

export function LiveFeedRain({
  events,
  max = 12,
  delay = 0,
}: {
  events: HoneypotEvent[];
  max?: number;
  delay?: number;
}) {
  const { t } = useTranslation();
  const rows = events.slice(0, max);

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 280, damping: 28 }}
      className="flex h-full flex-col overflow-hidden rounded-2xl glass-strong"
    >
      <div className="flex items-center justify-between border-b border-white/5 px-5 py-3">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-zinc-200">
          <span className="relative flex h-2 w-2">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
          </span>
          {t('dashboard.recentEvents')}
        </h3>
        <span className="font-mono text-[10px] uppercase tracking-widest text-zinc-500">
          {t('liveFeed.live')} · {rows.length}
        </span>
      </div>

      <div className="min-h-[280px] flex-1 space-y-1.5 overflow-y-auto p-3">
        {rows.length === 0 ? (
          <div className="flex h-full items-center justify-center py-12 font-mono text-sm text-zinc-600">
            {t('common.waiting')}
          </div>
        ) : (
          <AnimatePresence initial={false} mode="popLayout">
            {rows.map((e) => (
              <FeedRow key={e.id} event={e} />
            ))}
          </AnimatePresence>
        )}
      </div>
    </motion.div>
  );
}
