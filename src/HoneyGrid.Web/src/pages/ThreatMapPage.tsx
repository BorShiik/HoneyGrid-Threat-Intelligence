import { useEffect, useMemo, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Globe2, Maximize2, Minimize2, Zap } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { useStatsGeo } from '@/api/queries';
import type { HoneypotEvent } from '@/types/api';

/* ── Constants ── */
const SENSOR_LOCATION = { lat: 50.11, lng: 8.68, label: 'HoneyGrid EU' };
const MAX_ARCS = 120;

/* ── Types ── */
interface ArcData {
  id: string;
  startLat: number;
  startLng: number;
  endLat: number;
  endLng: number;
  color: string;
  dashGap: number;
  event: HoneypotEvent;
}

interface PointData {
  lat: number;
  lng: number;
  size: number;
  color: string;
  label: string;
  count: number;
}

/* ── Threat color helper ── */
function threatColor(event: HoneypotEvent): string {
  if (event.eventType === 'login.success') return '#e11d48';
  const score = event.threatIntel?.score ?? 0;
  if (score >= 80) return '#e11d48';
  if (score >= 60) return '#f97316';
  if (score >= 30) return '#f59e0b';
  return '#3b82f6';
}

/* ── Globe Component (lazy-loaded) ── */
function GlobeScene({
  arcs,
  points,
  fullscreen,
}: {
  arcs: ArcData[];
  points: PointData[];
  fullscreen: boolean;
}) {
  const globeRef = useRef<any>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [GlobeGL, setGlobeGL] = useState<any>(null);

  // Dynamically import react-globe.gl
  useEffect(() => {
    import('react-globe.gl').then((mod) => {
      setGlobeGL(() => mod.default);
    });
  }, []);

  // Auto-rotate
  useEffect(() => {
    if (!globeRef.current) return;
    const controls = globeRef.current.controls();
    if (controls) {
      controls.autoRotate = true;
      controls.autoRotateSpeed = 0.4;
      controls.enableDamping = true;
      controls.dampingFactor = 0.1;
    }
  }, [GlobeGL]);

  // Resize
  const [dims, setDims] = useState({ w: 800, h: 500 });
  useEffect(() => {
    if (!containerRef.current) return;
    const obs = new ResizeObserver((entries) => {
      const { width, height } = entries[0].contentRect;
      setDims({ w: width, h: height });
    });
    obs.observe(containerRef.current);
    return () => obs.disconnect();
  }, []);

  if (!GlobeGL) {
    return (
      <div ref={containerRef} className="w-full h-full flex items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <Globe2 className="h-12 w-12 text-zinc-700 animate-pulse" />
          <span className="text-sm text-zinc-600">{fullscreen ? '' : 'Инициализация 3D движка…'}</span>
        </div>
      </div>
    );
  }

  return (
    <div ref={containerRef} className="w-full h-full">
      <GlobeGL
        ref={globeRef}
        width={dims.w}
        height={dims.h}
        backgroundColor="rgba(0,0,0,0)"
        globeImageUrl="//unpkg.com/three-globe/example/img/earth-night.jpg"
        bumpImageUrl="//unpkg.com/three-globe/example/img/earth-topology.png"
        atmosphereColor="#f59e0b"
        atmosphereAltitude={0.18}
        // Arcs
        arcsData={arcs}
        arcStartLat={(d: ArcData) => d.startLat}
        arcStartLng={(d: ArcData) => d.startLng}
        arcEndLat={(d: ArcData) => d.endLat}
        arcEndLng={(d: ArcData) => d.endLng}
        arcColor={(d: ArcData) => d.color}
        arcDashLength={0.6}
        arcDashGap={(d: ArcData) => d.dashGap}
        arcDashAnimateTime={1200}
        arcStroke={0.5}
        arcAltitudeAutoScale={0.4}
        // Points
        pointsData={points}
        pointLat={(d: PointData) => d.lat}
        pointLng={(d: PointData) => d.lng}
        pointColor={(d: PointData) => d.color}
        pointAltitude={0.01}
        pointRadius={(d: PointData) => d.size}
        pointsMerge={false}
        // Rings (pulsating at sensor location)
        ringsData={[SENSOR_LOCATION]}
        ringLat={(d: any) => d.lat}
        ringLng={(d: any) => d.lng}
        ringColor={() => '#10b981'}
        ringMaxRadius={4}
        ringPropagationSpeed={2}
        ringRepeatPeriod={800}
      />
    </div>
  );
}

/* ── Active Sources Panel ── */
function ActiveSourcesPanel({ events }: { events: HoneypotEvent[] }) {
  const { t } = useTranslation();
  const byCountry = useMemo(() => {
    const map = new Map<string, { name: string; code: string; count: number }>();
    for (const e of events) {
      if (!e.geo) continue;
      const key = e.geo.country;
      const existing = map.get(key);
      if (existing) {
        existing.count++;
      } else {
        map.set(key, { name: e.geo.countryName, code: e.geo.country, count: 1 });
      }
    }
    return [...map.values()].sort((a, b) => b.count - a.count).slice(0, 8);
  }, [events]);

  const max = Math.max(1, byCountry[0]?.count ?? 1);

  return (
    <div className="space-y-2">
      <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider">{t('threatMap.activeSources')}</h4>
      {byCountry.map((c, i) => (
        <motion.div
          key={c.code}
          initial={{ opacity: 0, x: 8 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: i * 0.03 }}
          className="flex items-center gap-2 text-sm"
        >
          <span className="w-5 text-right font-mono text-[10px] text-zinc-600">{i + 1}</span>
          <span className="w-7 font-mono text-[10px] text-zinc-500 uppercase">{c.code}</span>
          <div className="flex-1 h-1 rounded-full bg-zinc-800/50 overflow-hidden">
            <div
              className="h-full rounded-full bg-gradient-to-r from-amber-500 to-orange-500/60 transition-all duration-500"
              style={{ width: `${(c.count / max) * 100}%` }}
            />
          </div>
          <span className="font-mono text-[10px] text-zinc-400 tabular-nums w-7 text-right">{c.count}</span>
        </motion.div>
      ))}
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════
   THREAT MAP PAGE
   ══════════════════════════════════════════════════════════════════════ */
export function ThreatMapPage() {
  const { t } = useTranslation();
  const { data: geo } = useStatsGeo();
  const { events, latest, simulated } = useLiveAttacks({ bufferSize: 200 });
  const [fullscreen, setFullscreen] = useState(false);
  const [arcs, setArcs] = useState<ArcData[]>([]);

  // Spawn arcs from live events
  useEffect(() => {
    if (!latest?.geo?.lat || !latest.geo.lon) return;
    const arc: ArcData = {
      id: `${latest.id}-${Date.now()}`,
      startLat: latest.geo.lat,
      startLng: latest.geo.lon,
      endLat: SENSOR_LOCATION.lat,
      endLng: SENSOR_LOCATION.lng,
      color: threatColor(latest),
      dashGap: 0.3 + Math.random() * 0.5,
      event: latest,
    };
    setArcs((prev) => [...prev.slice(-(MAX_ARCS - 1)), arc]);
  }, [latest]);

  // Static geo points
  const points: PointData[] = useMemo(() => {
    if (!geo?.points) return [];
    const maxCount = Math.max(1, ...geo.points.map((p) => p.count));
    return geo.points.map((p) => ({
      lat: p.lat,
      lng: p.lon,
      size: 0.15 + (p.count / maxCount) * 0.6,
      color: p.count / maxCount > 0.5 ? '#e11d48' : p.count / maxCount > 0.25 ? '#f97316' : '#f59e0b',
      label: p.countryName,
      count: p.count,
    }));
  }, [geo]);

  // Event stats
  const stats = useMemo(() => {
    const critical = events.filter((e) => e.eventType === 'login.success' || (e.threatIntel?.score ?? 0) >= 80).length;
    const countries = new Set(events.map((e) => e.geo?.country).filter(Boolean)).size;
    return { total: events.length, critical, countries };
  }, [events]);

  return (
    <motion.section
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className={cn(
        'relative',
        fullscreen ? 'fixed inset-0 z-50 bg-black' : 'space-y-4',
      )}
    >
      {/* Header (not in fullscreen) */}
      {!fullscreen && (
        <div className="flex items-start justify-between">
          <div>
            <h2 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
              <Globe2 className="h-5 w-5 text-amber-500" />
              {t('threatMap.title')}
            </h2>
            <p className="mt-1 text-sm text-zinc-500">
              {t('threatMap.subtitle')}
              {simulated && ` · ${t('common.demoMode')}`}
            </p>
          </div>
          <button
            onClick={() => setFullscreen(true)}
            className="flex items-center gap-1.5 rounded-lg bg-white/5 px-3 py-1.5 text-xs text-zinc-400 hover:bg-white/10 hover:text-white transition-colors"
          >
            <Maximize2 className="h-3.5 w-3.5" /> {t('threatMap.fullscreen')}
          </button>
        </div>
      )}

      {/* Globe Container */}
      <div className={cn(
        'relative overflow-hidden rounded-xl',
        fullscreen ? 'h-full' : 'h-[calc(100vh-200px)] min-h-[480px]',
      )}>
        {/* Background gradient */}
        <div className="absolute inset-0 bg-gradient-to-b from-black via-zinc-950 to-black" />

        {/* The 3D Globe */}
        <div className="absolute inset-0">
          <GlobeScene arcs={arcs} points={points} fullscreen={fullscreen} />
        </div>

        {/* ── Glass Overlay Panels ── */}
        {/* Left: Controls + Stats */}
        <div className="absolute top-4 left-4 z-10 pointer-events-auto w-44 space-y-3 sm:w-56">
          <div className="glass rounded-xl p-3.5 space-y-3">
            <div className="flex items-center gap-2">
              <Zap className="h-3.5 w-3.5 text-amber-500" />
              <span className="text-xs font-semibold text-zinc-300">{t('threatMap.streamStats')}</span>
            </div>
            <div className="grid grid-cols-3 gap-2">
              <div className="text-center">
                <div className="font-mono text-lg font-bold text-white tabular-nums">{stats.total}</div>
                <div className="text-[10px] text-zinc-500">{t('threatMap.eventsShort')}</div>
              </div>
              <div className="text-center">
                <div className="font-mono text-lg font-bold text-rose-400 tabular-nums">{stats.critical}</div>
                <div className="text-[10px] text-zinc-500">{t('threatMap.criticalShort')}</div>
              </div>
              <div className="text-center">
                <div className="font-mono text-lg font-bold text-amber-400 tabular-nums">{stats.countries}</div>
                <div className="text-[10px] text-zinc-500">{t('threatMap.countriesShort')}</div>
              </div>
            </div>
          </div>

          {/* Connection indicator */}
          <div className="glass rounded-xl p-3">
            <div className="flex items-center gap-2">
              <span className="relative flex h-2 w-2">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
              </span>
              <span className="text-[11px] text-zinc-400">
                {simulated ? t('threatMap.simulation') : t('threatMap.liveStream')} · {arcs.length} {t('threatMap.arcs')}
              </span>
            </div>
          </div>
        </div>

        {/* Right: Active sources (hidden on the narrowest screens to avoid overlap) */}
        <div className="absolute top-4 right-4 z-10 pointer-events-auto hidden w-52 sm:block">
          <div className="glass rounded-xl p-3.5">
            <ActiveSourcesPanel events={events} />
          </div>
        </div>

        {/* Bottom: Latest attack */}
        <AnimatePresence mode="wait">
          {latest && (
            <motion.div
              key={latest.id}
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={{ duration: 0.3 }}
              className="absolute bottom-4 left-4 right-4 z-10 pointer-events-none"
            >
              <div className="glass rounded-xl px-4 py-2.5 flex flex-wrap items-center gap-x-4 gap-y-1 max-w-xl">
                <span
                  className="h-2 w-2 rounded-full shrink-0"
                  style={{ backgroundColor: threatColor(latest), boxShadow: `0 0 8px ${threatColor(latest)}60` }}
                />
                <span className="font-mono text-xs text-zinc-300">{latest.attackerIp}</span>
                <span className="text-[10px] text-zinc-500">→</span>
                <span className="text-xs text-zinc-400">{latest.geo?.countryName ?? 'Unknown'}</span>
                <span className="text-[10px] text-zinc-600 ml-auto font-mono">
                  {new Date(latest.timestamp).toLocaleTimeString('en-GB')}
                </span>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Fullscreen exit */}
        {fullscreen && (
          <button
            onClick={() => setFullscreen(false)}
            className="absolute top-4 right-4 z-20 glass rounded-lg p-2 text-zinc-400 hover:text-white transition-colors pointer-events-auto"
          >
            <Minimize2 className="h-4 w-4" />
          </button>
        )}
      </div>
    </motion.section>
  );
}
