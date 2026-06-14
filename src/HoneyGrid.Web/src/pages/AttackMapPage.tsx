import { useEffect, useMemo, useRef, useState } from 'react';
import { motion } from 'framer-motion';
import { Globe2 } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { useStatsGeo } from '@/api/queries';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { formatInt } from '@/lib/format';

const W = 720;
const H = 360;

/** Equirectangular projection of (lat, lon) onto the SVG canvas. */
function project(lat: number, lon: number): { x: number; y: number } {
  return { x: ((lon + 180) / 360) * W, y: ((90 - lat) / 180) * H };
}

/** A representative collection sensor (Western Europe) all arcs terminate at. */
const SENSOR = project(50.11, 8.68);

interface Arc {
  id: string;
  from: { x: number; y: number };
  country: string;
}

/** Graticule — meridians/parallels every 30°. */
function Graticule() {
  const lines: React.ReactNode[] = [];
  for (let lon = -150; lon <= 150; lon += 30) {
    const { x } = project(0, lon);
    lines.push(<line key={`m${lon}`} x1={x} y1={0} x2={x} y2={H} stroke="currentColor" strokeWidth={0.5} />);
  }
  for (let lat = -60; lat <= 60; lat += 30) {
    const { y } = project(lat, 0);
    lines.push(<line key={`p${lat}`} x1={0} y1={y} x2={W} y2={y} stroke="currentColor" strokeWidth={0.5} />);
  }
  return <g className="text-border/60">{lines}</g>;
}

export function AttackMapPage() {
  const { data: geo, isPending } = useStatsGeo();
  const { events, latest, simulated } = useLiveAttacks({ bufferSize: 80 });
  const [arcs, setArcs] = useState<Arc[]>([]);
  const arcSeq = useRef(0);

  // Spawn a fading arc for each new live event that has coordinates.
  useEffect(() => {
    if (!latest?.geo?.lat || !latest.geo.lon) return;
    arcSeq.current += 1;
    const arc: Arc = {
      id: `${latest.id}-${arcSeq.current}`,
      from: project(latest.geo.lat, latest.geo.lon),
      country: latest.geo.countryName ?? '',
    };
    setArcs((prev) => [...prev.slice(-11), arc]);
  }, [latest]);

  const maxCount = useMemo(() => Math.max(1, ...(geo?.points ?? []).map((p) => p.count)), [geo]);

  const liveByCountry = useMemo(() => {
    const map = new Map<string, number>();
    for (const e of events) {
      const key = e.geo?.countryName ?? 'Nieznany';
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return [...map.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8);
  }, [events]);

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div>
        <h2 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
          <Globe2 className="size-6 text-primary" /> Mapa ataków na żywo
        </h2>
        <p className="mt-1 max-w-2xl text-muted-foreground">
          Geograficzne źródła ataków rzutowane w czasie rzeczywistym; łuki prowadzą od źródła do
          sensora zbierającego w Europie Zachodniej.
          {simulated && ' (tryb demonstracyjny — symulowany strumień)'}
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
        <Card className="overflow-hidden">
          <CardContent className="p-0">
            {isPending ? (
              <Skeleton className="aspect-[2/1] w-full" />
            ) : (
              <svg
                viewBox={`0 0 ${W} ${H}`}
                className="w-full bg-[oklch(0.19_0.02_260)]"
                role="img"
                aria-label="Mapa ataków"
              >
                <Graticule />

                {/* Aggregate origins (size + glow by volume). */}
                {(geo?.points ?? []).map((p) => {
                  const { x, y } = project(p.lat, p.lon);
                  const r = 2 + (p.count / maxCount) * 9;
                  return (
                    <g key={p.country}>
                      <circle cx={x} cy={y} r={r * 1.8} className="fill-severity-high/15" />
                      <circle cx={x} cy={y} r={r} className="fill-severity-high/80" />
                    </g>
                  );
                })}

                {/* Live arcs from attacker → sensor. */}
                {arcs.map((arc) => {
                  const mx = (arc.from.x + SENSOR.x) / 2;
                  const my = Math.min(arc.from.y, SENSOR.y) - 40;
                  const d = `M ${arc.from.x} ${arc.from.y} Q ${mx} ${my} ${SENSOR.x} ${SENSOR.y}`;
                  return (
                    <g key={arc.id}>
                      <motion.path
                        d={d}
                        fill="none"
                        className="stroke-primary"
                        strokeWidth={1.2}
                        initial={{ pathLength: 0, opacity: 0.9 }}
                        animate={{ pathLength: 1, opacity: 0 }}
                        transition={{ duration: 1.6, ease: 'easeOut' }}
                      />
                      <motion.circle
                        cx={arc.from.x}
                        cy={arc.from.y}
                        r={3}
                        className="fill-primary"
                        initial={{ opacity: 0.9, scale: 0.6 }}
                        animate={{ opacity: 0, scale: 2.4 }}
                        transition={{ duration: 1.6, ease: 'easeOut' }}
                      />
                    </g>
                  );
                })}

                {/* Sensor node. */}
                <g>
                  <circle cx={SENSOR.x} cy={SENSOR.y} r={5} className="fill-status-online" />
                  <circle
                    cx={SENSOR.x}
                    cy={SENSOR.y}
                    r={5}
                    className="fill-none stroke-status-online/50"
                    strokeWidth={1.5}
                  />
                </g>
              </svg>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-base">Najaktywniejsze źródła (na żywo)</CardTitle>
          </CardHeader>
          <CardContent className="space-y-1.5">
            {liveByCountry.length === 0 && (
              <p className="text-sm text-muted-foreground">Oczekiwanie na zdarzenia…</p>
            )}
            {liveByCountry.map(([country, count], i) => (
              <LiveCountryRow key={country} rank={i + 1} country={country} count={count} />
            ))}
            <div className="mt-3 border-t pt-3 text-sm text-muted-foreground">
              Zdarzeń w oknie:{' '}
              <span className="font-mono text-foreground">{formatInt(events.length)}</span>
            </div>
          </CardContent>
        </Card>
      </div>
    </motion.section>
  );
}

function LiveCountryRow({ rank, country, count }: { rank: number; country: string; count: number }) {
  return (
    <div className="flex items-center gap-2 text-sm">
      <span className="w-5 text-right font-mono text-xs text-muted-foreground">{rank}</span>
      <span className="flex-1 truncate">{country}</span>
      <span className={cn('font-mono tabular-nums', count > 3 ? 'text-severity-high' : 'text-foreground')}>
        {count}
      </span>
    </div>
  );
}
