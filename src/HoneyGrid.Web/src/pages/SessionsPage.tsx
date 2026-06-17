import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Clapperboard, Terminal, Clock, Server, Command as CommandIcon, Play } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useSessions } from '@/api/queries';
import type { SessionSummary } from '@/types/api';
import SessionReplayPlayer from '@/components/replay/SessionReplayPlayer';

function formatDuration(ms: number): string {
  if (ms <= 0) return '0s';
  const s = Math.round(ms / 1000);
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  return `${m}m ${s % 60}s`;
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString();
}

export function SessionsPage() {
  const { t } = useTranslation();
  const { data, isPending, isError } = useSessions();
  const [selected, setSelected] = useState<SessionSummary | null>(null);

  const sessions = data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <header>
        <h1 className="text-xl font-semibold text-zinc-100">{t('sessions.title')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-zinc-400">{t('sessions.subtitle')}</p>
      </header>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,420px)_1fr]">
        {/* ── Lista sesji ── */}
        <div className="rounded-xl border border-white/5 bg-zinc-900/40 p-3">
          <div className="mb-2 flex items-center gap-2 px-1">
            <Clapperboard className="h-4 w-4 text-amber-500" aria-hidden />
            <h2 className="text-[11px] font-mono font-bold uppercase tracking-widest text-zinc-300">
              {t('sessions.listTitle')}
            </h2>
            <span className="ml-auto text-[11px] text-zinc-500">{sessions.length}</span>
          </div>

          {isPending && <p className="px-2 py-6 text-sm text-zinc-500">{t('common.loading')}</p>}
          {isError && <p className="px-2 py-6 text-sm text-rose-400">{t('sessions.loadError')}</p>}
          {!isPending && !isError && sessions.length === 0 && (
            <p className="px-2 py-6 text-sm text-zinc-500">{t('sessions.empty')}</p>
          )}

          <ul className="flex flex-col gap-1">
            {sessions.map((s) => {
              const isSel = selected?.sessionId === s.sessionId;
              return (
                <li key={s.sessionId}>
                  <button
                    type="button"
                    onClick={() => s.hasTty && setSelected(s)}
                    disabled={!s.hasTty}
                    className={cn(
                      'w-full rounded-lg border px-3 py-2 text-left transition-colors',
                      isSel
                        ? 'border-amber-500/40 bg-amber-500/10'
                        : 'border-white/5 bg-white/[0.02] hover:bg-white/[0.05]',
                      !s.hasTty && 'cursor-not-allowed opacity-50',
                    )}
                  >
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-sm text-zinc-200">{s.attackerIp}</span>
                      {s.hasTty ? (
                        <span className="ml-auto flex items-center gap-1 text-[10px] font-mono uppercase tracking-wider text-emerald-400">
                          <Play className="h-3 w-3" aria-hidden /> {t('sessions.hasTty')}
                        </span>
                      ) : (
                        <span className="ml-auto text-[10px] font-mono uppercase tracking-wider text-zinc-500">
                          {t('sessions.noTty')}
                        </span>
                      )}
                    </div>
                    <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[11px] text-zinc-500">
                      <span className="inline-flex items-center gap-1">
                        <Clock className="h-3 w-3" aria-hidden /> {formatTime(s.startedAt)}
                      </span>
                      <span className="inline-flex items-center gap-1">
                        <Server className="h-3 w-3" aria-hidden /> {s.sensorId}
                      </span>
                      <span className="inline-flex items-center gap-1">
                        <CommandIcon className="h-3 w-3" aria-hidden /> {s.commandCount}
                      </span>
                      <span>{formatDuration(s.durationMs)}</span>
                    </div>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>

        {/* ── Odtwarzacz ── */}
        <div className="min-h-[420px] rounded-xl border border-white/5 bg-zinc-900/40 p-3">
          {selected ? (
            <motion.div
              key={selected.sessionId}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
            >
              <div className="mb-3 flex items-center gap-2 px-1">
                <Terminal className="h-4 w-4 text-amber-500" aria-hidden />
                <h2 className="text-[11px] font-mono font-bold uppercase tracking-widest text-zinc-300">
                  {t('sessions.replayTitle')}
                </h2>
                <span className="ml-2 font-mono text-xs text-zinc-500">{selected.attackerIp}</span>
              </div>
              <SessionReplayPlayer key={selected.sessionId} sessionId={selected.sessionId} />
            </motion.div>
          ) : (
            <div className="flex h-full min-h-[380px] flex-col items-center justify-center gap-3 text-zinc-600">
              <Clapperboard className="h-10 w-10" aria-hidden />
              <p className="text-sm">{t('sessions.selectHint')}</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
