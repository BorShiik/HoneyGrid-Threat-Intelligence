import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { Pause, Play, RotateCcw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useSessionReplay } from '@/api/queries';
import {
  formatClock,
  framesDuration,
  framesUpTo,
  scheduleFrames,
  type PlaybackSpeed,
} from '@/lib/replay';
import type { SessionReplayFrame } from '@/types/api';

const SPEEDS: PlaybackSpeed[] = [1, 2, 4];

/** Colour attacker keystrokes ('i' frames) so they stand out from output. */
const INPUT_COLOR = '\x1b[38;2;120;200;255m';
const RESET_COLOR = '\x1b[0m';

function renderFrameData(type: 'i' | 'o', payload: string): string {
  return type === 'i' ? `${INPUT_COLOR}${payload}${RESET_COLOR}` : payload;
}

interface SessionReplayPlayerProps {
  /** Caller MUST pass a `key={sessionId}` so the player remounts per session. */
  sessionId: string;
}

/**
 * xterm.js-based read-only TTY replay player.
 *
 * Playback is driven by speed-adjusted setTimeout writes computed by the pure
 * helpers in `@/lib/replay`. Frames are flushed in batches per animation frame
 * so a burst of near-simultaneous frames cannot jank the main thread. The
 * component is expected to be remounted (via `key`) when the session changes,
 * so there is no in-effect state reset.
 */
export default function SessionReplayPlayer({ sessionId }: SessionReplayPlayerProps) {
  const { data, isPending, isError } = useSessionReplay(sessionId);

  const containerRef = useRef<HTMLDivElement>(null);
  const termRef = useRef<Terminal | null>(null);

  // Pending writes flushed once per animation frame to avoid layout thrash.
  const writeQueueRef = useRef<string[]>([]);
  const rafRef = useRef<number | null>(null);
  const timersRef = useRef<number[]>([]);
  // Interwał płynnie aktualizujący pozycję (slider jedzie też w "ciszy" między klatkami).
  const tickerRef = useRef<number | null>(null);

  const [isPlaying, setIsPlaying] = useState(false);
  const [speed, setSpeed] = useState<PlaybackSpeed>(1);
  const speedRef = useRef<PlaybackSpeed>(1);
  // Current playback position in ms (logical, not wall-clock).
  const [positionMs, setPositionMs] = useState(0);
  const positionRef = useRef(0);

  // Tylko klatki WYJŚCIA ('o'). Strumień wyjścia zawiera już echo wpisanych znaków
  // (shell odbija każdy klawisz), więc dołączanie klatek wejścia ('i') dublowałoby
  // znaki ("wwhhooaammii" zamiast "whoami"). Wyjście = dokładnie to, co widział atakujący.
  const frames = useMemo<SessionReplayFrame[]>(
    () => (data?.frames ?? []).filter((f) => f.type === 'o'),
    [data],
  );
  const hasTty = frames.length > 0;
  const durationMs = useMemo(
    () => framesDuration(frames, data?.durationMs ?? 0),
    [frames, data],
  );

  const clearTimers = useCallback(() => {
    timersRef.current.forEach((id) => window.clearTimeout(id));
    timersRef.current = [];
    if (rafRef.current !== null) {
      window.cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }
    if (tickerRef.current !== null) {
      window.clearInterval(tickerRef.current);
      tickerRef.current = null;
    }
    writeQueueRef.current = [];
  }, []);

  const enqueueWrite = useCallback((payload: string) => {
    writeQueueRef.current.push(payload);
    if (rafRef.current !== null) return;
    rafRef.current = window.requestAnimationFrame(() => {
      rafRef.current = null;
      const batch = writeQueueRef.current.join('');
      writeQueueRef.current = [];
      if (batch) termRef.current?.write(batch);
    });
  }, []);

  /** (Re)build the setTimeout schedule from `from` at the current speed. */
  const runSchedule = useCallback(
    (from: number) => {
      clearTimers();
      const currentSpeed = speedRef.current;
      const schedule = scheduleFrames(frames, from, currentSpeed);
      schedule.forEach(({ frame, delayMs }) => {
        const id = window.setTimeout(() => {
          enqueueWrite(renderFrameData(frame.type, frame.data));
        }, delayMs);
        timersRef.current.push(id);
      });
      // Płynny pasek postępu: pozycję liczymy z zegara ściennego × prędkość, także
      // w przerwach między klatkami (pauzy atakującego) — slider nie "zamiera".
      // Po dojściu do końca zatrzymujemy odtwarzanie.
      const startWall = performance.now();
      tickerRef.current = window.setInterval(() => {
        const pos = Math.min(durationMs, from + (performance.now() - startWall) * currentSpeed);
        positionRef.current = pos;
        setPositionMs(pos);
        if (pos >= durationMs) {
          if (tickerRef.current !== null) {
            window.clearInterval(tickerRef.current);
            tickerRef.current = null;
          }
          setIsPlaying(false);
        }
      }, 100);
    },
    [clearTimers, durationMs, enqueueWrite, frames],
  );

  // Initialise terminal AFTER data is loaded — while pending/error the component
  // renders a skeleton/message (no container div), so the terminal div only
  // exists once isPending/isError are false. Gating on them (not just mount)
  // fixes the race where the player stayed blank because the effect ran once on
  // the skeleton and never re-ran. Component is keyed per session by the parent.
  useEffect(() => {
    if (isPending || isError || !containerRef.current) return;
    const term = new Terminal({
      // true: traktuj samo '\n' jak '\r\n' (zrzuty ttylog Cowrie często nie mają
      // CR → bez tego linie układają się "schodkami").
      convertEol: true,
      disableStdin: true, // read-only — nobody can type into a replay
      cursorBlink: false,
      fontSize: 13,
      fontFamily:
        'ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace',
      theme: { background: '#0b0f17', foreground: '#d7dde8', cursor: '#0b0f17' },
      scrollback: 5000,
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(containerRef.current);
    try {
      fit.fit();
    } catch {
      /* container not yet measured — ignore */
    }
    termRef.current = term;

    // Pokaż pełny zrzut sesji od razu (inaczej terminal jest pusty do kliknięcia
    // Odtwórz, a przy sesjach exec/instant nie widać, że coś w nim jest).
    // Odtwórz/Restart przewinie i odtworzy z taktowaniem czasowym.
    if (frames.length > 0) {
      const dump = frames.map((f) => renderFrameData(f.type, f.data)).join('');
      if (dump) term.write(dump);
      positionRef.current = durationMs;
      setPositionMs(durationMs);
    }

    const onResize = () => {
      try {
        fit.fit();
      } catch {
        /* ignore */
      }
    };
    window.addEventListener('resize', onResize);

    return () => {
      window.removeEventListener('resize', onResize);
      clearTimers();
      term.dispose();
      termRef.current = null;
    };
  }, [clearTimers, isPending, isError, frames, durationMs]);

  const seekTo = useCallback(
    (target: number) => {
      clearTimers();
      setIsPlaying(false);
      const term = termRef.current;
      if (term) {
        term.reset();
        const batch = framesUpTo(frames, target)
          .map((f) => renderFrameData(f.type, f.data))
          .join('');
        if (batch) term.write(batch);
      }
      positionRef.current = target;
      setPositionMs(target);
    },
    [clearTimers, frames],
  );

  const play = useCallback(() => {
    if (!hasTty) return;
    const term = termRef.current;
    if (!term) return;

    // Restart od początku, gdy jesteśmy na końcu LUB nie ma już nic do odtworzenia
    // od bieżącej pozycji (np. po wstępnym pełnym zrzucie pozycja jest na końcu).
    let from = positionRef.current;
    const remaining = scheduleFrames(frames, from, speedRef.current);
    if (from >= durationMs || remaining.length === 0) {
      term.reset();
      from = 0;
      positionRef.current = 0;
      setPositionMs(0);
    }

    setIsPlaying(true);
    runSchedule(from);
  }, [durationMs, frames, hasTty, runSchedule]);

  const pause = useCallback(() => {
    clearTimers();
    setIsPlaying(false);
  }, [clearTimers]);

  const restart = useCallback(() => {
    const term = termRef.current;
    if (!term || !hasTty) return;
    term.reset();
    positionRef.current = 0;
    setPositionMs(0);
    setIsPlaying(true);
    runSchedule(0);
  }, [hasTty, runSchedule]);

  // Changing speed reschedules from the current position if playing.
  const changeSpeed = useCallback(
    (next: PlaybackSpeed) => {
      speedRef.current = next;
      setSpeed(next);
      if (isPlaying) runSchedule(positionRef.current);
    },
    [isPlaying, runSchedule],
  );

  if (isPending) {
    return <Skeleton className="h-80 w-full" />;
  }

  if (isError) {
    return (
      <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">
        Nie udało się pobrać nagrania sesji. Spróbuj ponownie później.
      </p>
    );
  }

  return (
    <div className="space-y-3">
      {!hasTty && (
        <p className="rounded-lg border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
          Brak nagrania TTY dla tej sesji.
        </p>
      )}

      <div
        className={hasTty ? 'rounded-lg border bg-[#0b0f17] p-2' : 'hidden'}
        // xterm needs a sized container before fit() can measure it
        style={{ height: 360 }}
      >
        <div ref={containerRef} className="h-full w-full" data-testid="xterm-container" />
      </div>

      {hasTty && (
        <div className="space-y-2">
          {/* Scrubber */}
          <div className="flex items-center gap-3">
            <span className="w-12 shrink-0 font-mono text-xs text-muted-foreground tabular-nums">
              {formatClock(positionMs)}
            </span>
            <input
              type="range"
              min={0}
              max={durationMs}
              step={100}
              value={Math.min(positionMs, durationMs)}
              onChange={(e) => seekTo(Number(e.target.value))}
              aria-label="Pasek przewijania nagrania"
              className="h-1.5 flex-1 cursor-pointer appearance-none rounded-full bg-muted accent-primary"
            />
            <span className="w-12 shrink-0 text-right font-mono text-xs text-muted-foreground tabular-nums">
              {formatClock(durationMs)}
            </span>
          </div>

          {/* Controls */}
          <div className="flex flex-wrap items-center gap-2">
            {isPlaying ? (
              <Button size="sm" variant="secondary" onClick={pause}>
                <Pause /> Pauza
              </Button>
            ) : (
              <Button size="sm" onClick={play}>
                <Play /> Odtwórz
              </Button>
            )}
            <Button size="sm" variant="outline" onClick={restart}>
              <RotateCcw /> Restart
            </Button>

            <div className="ml-auto flex items-center gap-1">
              <span className="mr-1 text-xs text-muted-foreground">Prędkość</span>
              {SPEEDS.map((s) => (
                <Button
                  key={s}
                  size="sm"
                  variant={speed === s ? 'default' : 'outline'}
                  onClick={() => changeSpeed(s)}
                  className="font-mono"
                  aria-pressed={speed === s}
                >
                  {s}x
                </Button>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
