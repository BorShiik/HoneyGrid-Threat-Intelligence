import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { ShieldAlert, X } from 'lucide-react';
import { cn } from '@/lib/utils';
import { attackBus, ATTACK_BUS_EVENT } from '@/lib/liveAttacks';
import { eventSeverity, eventTypeKey } from '@/lib/format';
import { CountryFlag } from '@/components/ui/CountryFlag';
import { useSettingsStore } from '@/lib/settings';
import type { HoneypotEvent } from '@/types/api';

type HotSeverity = 'critical' | 'high';

const SEV: Record<HotSeverity, { accent: string; chip: string; glow: string }> = {
  critical: { accent: 'bg-rose-500', chip: 'text-rose-400', glow: 'shadow-[0_0_24px_-6px_rgba(225,29,72,0.6)]' },
  high: { accent: 'bg-orange-500', chip: 'text-orange-400', glow: 'shadow-[0_0_22px_-7px_rgba(249,115,22,0.5)]' },
};

interface Toast {
  id: string;
  event: HoneypotEvent;
  severity: HotSeverity;
}

const MAX_TOASTS = 4;
const TOAST_TTL_MS = 6000;

/**
 * Critical-attack toaster. Subscribes to the shared {@link attackBus} (driven by
 * the live-attacks stream) and pops a glass toast for every critical/high event,
 * so analysts get a passive heads-up without watching the feed. Auto-dismisses,
 * stacks (max 4), de-duplicates, and is fully localized.
 */
export function AttackToaster() {
  const { t } = useTranslation();
  const [toasts, setToasts] = useState<Toast[]>([]);
  const seen = useRef<Set<string>>(new Set());

  useEffect(() => {
    const onAttack = (e: Event) => {
      // If notifications are muted, do not add new toasts
      if (useSettingsStore.getState().muteToasts) return;

      const event = (e as CustomEvent<HoneypotEvent>).detail;
      if (!event || seen.current.has(event.id)) return;

      const sev = eventSeverity(event);
      if (sev !== 'critical' && sev !== 'high') return;

      seen.current.add(event.id);
      if (seen.current.size > 500) {
        seen.current = new Set([...seen.current].slice(-200));
      }

      const toast: Toast = { id: event.id, event, severity: sev };
      setToasts((prev) => [toast, ...prev].slice(0, MAX_TOASTS));
      window.setTimeout(() => {
        setToasts((prev) => prev.filter((x) => x.id !== toast.id));
      }, TOAST_TTL_MS);
    };

    attackBus.addEventListener(ATTACK_BUS_EVENT, onAttack);
    return () => attackBus.removeEventListener(ATTACK_BUS_EVENT, onAttack);
  }, []);

  const dismiss = (id: string) => setToasts((prev) => prev.filter((x) => x.id !== id));

  return (
    <div className="pointer-events-none fixed bottom-4 right-4 left-4 z-[55] flex flex-col gap-2 sm:left-auto sm:w-80">
      <AnimatePresence>
        {toasts.map((toast) => {
          const sev = SEV[toast.severity];
          const ev = toast.event;
          return (
            <motion.div
              key={toast.id}
              layout
              initial={{ opacity: 0, x: 40, scale: 0.95 }}
              animate={{ opacity: 1, x: 0, scale: 1 }}
              exit={{ opacity: 0, x: 40, scale: 0.95 }}
              transition={{ type: 'spring', stiffness: 380, damping: 30 }}
              className={cn('pointer-events-auto relative overflow-hidden rounded-xl glass-strong p-3', sev.glow)}
            >
              <div className={cn('absolute left-0 top-0 bottom-0 w-[3px]', sev.accent)} />
              <div className="flex items-start gap-2.5 pl-1.5">
                <ShieldAlert className={cn('mt-0.5 h-4 w-4 shrink-0', sev.chip)} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className={cn('text-xs font-semibold uppercase tracking-wider', sev.chip)}>
                      {t(`severity.${toast.severity}`)}
                    </span>
                    <span className="text-[11px] text-zinc-500">{t(`eventType.${eventTypeKey(ev.eventType)}`)}</span>
                  </div>
                  <div className="mt-1 flex items-center gap-1.5 font-mono text-sm text-zinc-200">
                    {ev.geo?.country && <CountryFlag code={ev.geo.country} className="text-base" />}
                    <span className="truncate">{ev.attackerIp}</span>
                  </div>
                  {ev.geo?.countryName && <div className="truncate text-[11px] text-zinc-500">{ev.geo.countryName}</div>}
                </div>
                <button
                  onClick={() => dismiss(toast.id)}
                  aria-label={t('common.close')}
                  className="rounded p-0.5 text-zinc-600 transition-colors hover:text-zinc-300"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              </div>
            </motion.div>
          );
        })}
      </AnimatePresence>
    </div>
  );
}
