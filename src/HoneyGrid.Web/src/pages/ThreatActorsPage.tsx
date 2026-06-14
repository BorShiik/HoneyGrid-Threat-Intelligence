import { useState } from 'react';
import { motion } from 'framer-motion';
import { Fingerprint } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { SeverityBadge, SEVERITY_LABELS } from '@/components/SeverityBadge';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';
import { useActors } from '@/api/queries';
import type { Severity, ThreatActor } from '@/types/api';

const SOPHISTICATION_LABELS: Record<string, string> = {
  minimal: 'minimalne',
  intermediate: 'średnie',
  advanced: 'zaawansowane',
};

const INTENT_LABELS: Record<string, string> = {
  opportunistic: 'oportunistyczna',
  targeted: 'ukierunkowana',
  automated: 'zautomatyzowana',
};

const SEVERITY_RING: Record<Severity, string> = {
  critical: 'ring-severity-critical',
  high: 'ring-severity-high',
  medium: 'ring-severity-medium',
  low: 'ring-severity-low',
};

const SEVERITY_FILL: Record<Severity, string> = {
  critical: 'fill-severity-critical',
  high: 'fill-severity-high',
  medium: 'fill-severity-medium',
  low: 'fill-severity-low',
};

const SEVERITY_STROKE: Record<Severity, string> = {
  critical: 'stroke-severity-critical',
  high: 'stroke-severity-high',
  medium: 'stroke-severity-medium',
  low: 'stroke-severity-low',
};

/** Deterministic constellation layout: bubbles on a ring, size ∝ √eventCount. */
function bubbleLayout(actors: ThreatActor[]) {
  const cx = 360;
  const cy = 180;
  const maxEvents = Math.max(1, ...actors.map((a) => a.eventCount));
  return actors.map((actor, i) => {
    const angle = (i / Math.max(1, actors.length)) * Math.PI * 2 - Math.PI / 2;
    const ring = actors.length > 1 ? 120 : 0;
    return {
      actor,
      x: cx + Math.cos(angle) * ring,
      y: cy + Math.sin(angle) * ring,
      r: 26 + Math.sqrt(actor.eventCount / maxEvents) * 44,
    };
  });
}

function Dossier({ actor }: { actor: ThreatActor }) {
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <SeverityBadge severity={actor.severity} />
        <Badge variant="outline">
          Zaawansowanie: {SOPHISTICATION_LABELS[actor.sophistication] ?? actor.sophistication}
        </Badge>
        <Badge variant="outline">Intencja: {INTENT_LABELS[actor.intent] ?? actor.intent}</Badge>
      </div>

      {actor.description && <p className="text-sm">{actor.description}</p>}

      <div className="grid grid-cols-2 gap-3 text-sm">
        <Stat label="Zdarzenia" value={formatInt(actor.eventCount)} />
        <Stat label="Kraje" value={actor.countries.join(', ') || '—'} />
        <Stat label="Pierwsza aktywność" value={new Date(actor.firstSeen).toLocaleDateString('pl-PL')} />
        <Stat label="Ostatnia aktywność" value={new Date(actor.lastSeen).toLocaleDateString('pl-PL')} />
      </div>

      <div>
        <div className="mb-1.5 text-xs font-medium text-muted-foreground">Znane adresy IP</div>
        <div className="flex flex-wrap gap-1.5">
          {actor.knownIps.map((ip) => (
            <code key={ip} className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">
              {ip}
            </code>
          ))}
        </div>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-card px-3 py-2">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="font-medium">{value}</div>
    </div>
  );
}

export function ThreatActorsPage() {
  const { data, isPending, isError } = useActors();
  const [selected, setSelected] = useState<ThreatActor | null>(null);
  const layout = data ? bubbleLayout(data) : [];

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div>
        <h2 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
          <Fingerprint className="size-6 text-primary" /> Aktorzy zagrożeń
        </h2>
        <p className="mt-1 max-w-2xl text-muted-foreground">
          Skorelowane kampanie i grupy: rozmiar węzła odpowiada liczbie zdarzeń, kolor — poziomowi
          zagrożenia. Kliknij węzeł lub kartę, aby otworzyć dossier.
        </p>
      </div>

      {isError && (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">
          Nie udało się pobrać profili aktorów.
        </p>
      )}

      {isPending ? (
        <Skeleton className="aspect-[2/1] w-full" />
      ) : data ? (
        <Card className="overflow-hidden">
          <CardContent className="p-0">
            <svg viewBox="0 0 720 360" className="w-full bg-[oklch(0.19_0.02_260)]" role="img" aria-label="Graf aktorów">
              {layout.map(({ actor, x, y, r }) => (
                <g
                  key={actor.id}
                  className="cursor-pointer"
                  onClick={() => setSelected(actor)}
                  role="button"
                  aria-label={`Aktor ${actor.name}`}
                >
                  <circle cx={x} cy={y} r={r + 4} className={cn('fill-none opacity-40')} />
                  <circle cx={x} cy={y} r={r} className={cn(SEVERITY_FILL[actor.severity], 'opacity-25')} />
                  <circle
                    cx={x}
                    cy={y}
                    r={r}
                    className={cn('fill-none stroke-2', SEVERITY_STROKE[actor.severity])}
                  />
                  <text x={x} y={y - 2} textAnchor="middle" className="fill-foreground text-[11px] font-semibold">
                    {actor.name.length > 16 ? `${actor.name.slice(0, 15)}…` : actor.name}
                  </text>
                  <text x={x} y={y + 12} textAnchor="middle" className="fill-muted-foreground text-[10px]">
                    {formatInt(actor.eventCount)}
                  </text>
                </g>
              ))}
            </svg>
          </CardContent>
        </Card>
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {(data ?? []).map((actor) => (
          <button key={actor.id} type="button" onClick={() => setSelected(actor)} className="text-left">
            <Card className={cn('h-full ring-1 ring-inset transition-colors hover:bg-muted/40', SEVERITY_RING[actor.severity])}>
              <CardContent className="space-y-2 py-4">
                <div className="flex items-center justify-between gap-2">
                  <span className="truncate font-semibold">{actor.name}</span>
                  <SeverityBadge severity={actor.severity} />
                </div>
                <div className="text-xs text-muted-foreground">
                  {formatInt(actor.eventCount)} zdarzeń · {actor.countries.join(', ')}
                </div>
                <div className="text-xs text-muted-foreground">
                  {SEVERITY_LABELS[actor.severity]} · {SOPHISTICATION_LABELS[actor.sophistication] ?? actor.sophistication}
                </div>
              </CardContent>
            </Card>
          </button>
        ))}
      </div>

      <Dialog open={selected !== null} onOpenChange={(open) => !open && setSelected(null)}>
        <DialogContent>
          {selected && (
            <>
              <DialogHeader>
                <DialogTitle>{selected.name}</DialogTitle>
                <DialogDescription>Dossier aktora zagrożeń · {selected.id}</DialogDescription>
              </DialogHeader>
              <Dossier actor={selected} />
            </>
          )}
        </DialogContent>
      </Dialog>
    </motion.section>
  );
}
