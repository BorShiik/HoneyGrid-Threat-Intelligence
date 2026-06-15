import { useEffect, useMemo, useState } from 'react';
import { motion } from 'framer-motion';
import { Globe2, Maximize2, Minimize2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { useLiveAttacks } from '@/lib/liveAttacks';
import { useStatsGeo } from '@/api/queries';
import { useThreatMapStore } from '@/stores/threatMapStore';
import { ThreatGlobe } from '@/components/threatmap/ThreatGlobe';
import { StreamStats } from '@/components/threatmap/StreamStats';
import { ActiveSourcesList } from '@/components/threatmap/ActiveSourcesList';
import { ThreatTicker } from '@/components/threatmap/ThreatTicker';
import type { ThreatSource } from '@/components/threatmap/types';
import type { HoneypotEvent } from '@/types/api';

/** Aggregates a live event buffer into per-country attack sources. */
function aggregateSources(events: HoneypotEvent[]): ThreatSource[] {
  const map = new Map<string, ThreatSource>();
  for (const e of events) {
    if (!e.geo) continue;
    const existing = map.get(e.geo.country);
    if (existing) {
      existing.count += 1;
    } else {
      map.set(e.geo.country, {
        country: e.geo.country,
        countryName: e.geo.countryName,
        lat: e.geo.lat,
        lng: e.geo.lon,
        count: 1,
      });
    }
  }
  return [...map.values()].sort((a, b) => b.count - a.count);
}

/* ══════════════════════════════════════════════════════════════════════
   THREAT MAP — holographic cyber-globe (R3F) under a glass HUD.
   ══════════════════════════════════════════════════════════════════════ */
export function ThreatMapPage() {
  const { t } = useTranslation();
  const { data: geo } = useStatsGeo();
  const { events, simulated } = useLiveAttacks({ bufferSize: 200 });
  const [fullscreen, setFullscreen] = useState(false);
  const setHovered = useThreatMapStore((s) => s.setHoveredCountry);

  // Clear any cross-component hover focus when leaving the map.
  useEffect(() => () => setHovered(null), [setHovered]);

  // Prefer the stable geo aggregate for the globe (keeps arcs/camera steady);
  // fall back to the live buffer until the geo query resolves.
  const geoSources = useMemo<ThreatSource[] | null>(() => {
    if (!geo?.points?.length) return null;
    return geo.points
      .map((p) => ({
        country: p.country,
        countryName: p.countryName,
        lat: p.lat,
        lng: p.lon,
        count: p.count,
      }))
      .sort((a, b) => b.count - a.count);
  }, [geo]);

  const liveSources = useMemo(() => aggregateSources(events), [events]);
  const baseSources = geoSources ?? liveSources;
  const sources = useMemo(() => baseSources.slice(0, 12), [baseSources]);

  const stats = useMemo(() => {
    const critical = events.filter(
      (e) => e.eventType === 'login.success' || (e.threatIntel?.score ?? 0) >= 80,
    ).length;
    const countries = new Set(events.map((e) => e.geo?.country).filter(Boolean)).size;
    return { total: events.length, critical, countries };
  }, [events]);

  return (
    <motion.section
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className={cn('relative', fullscreen ? 'fixed inset-0 z-50 bg-black' : 'space-y-4')}
    >
      {!fullscreen && (
        <div className="flex items-start justify-between">
          <div>
            <h2 className="flex items-center gap-2 text-xl font-bold tracking-tight text-white">
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
            className="flex items-center gap-1.5 rounded-lg bg-white/5 px-3 py-1.5 text-xs text-zinc-400 transition-colors hover:bg-white/10 hover:text-white"
          >
            <Maximize2 className="h-3.5 w-3.5" /> {t('threatMap.fullscreen')}
          </button>
        </div>
      )}

      <div
        className={cn(
          'relative overflow-hidden rounded-2xl border border-white/5 bg-[#050505]',
          fullscreen ? 'h-full rounded-none border-0' : 'h-[calc(100vh-180px)] min-h-[520px]',
        )}
      >
        {/* 3D globe (interactive — OrbitControls live here) */}
        <div className="absolute inset-0">
          <ThreatGlobe sources={sources} />
        </div>

        {/* HUD overlay — transparent to pointer events except on the panels */}
        <div className="pointer-events-none absolute inset-0 z-10">
          <div className="pointer-events-auto absolute left-4 top-4">
            <StreamStats
              total={stats.total}
              critical={stats.critical}
              countries={stats.countries}
              live={!simulated}
            />
          </div>

          <div className="pointer-events-auto absolute right-4 top-4 hidden sm:block">
            <ActiveSourcesList sources={sources} />
          </div>

          <div className="pointer-events-auto absolute bottom-4 left-4">
            <ThreatTicker events={events} />
          </div>

          {fullscreen && (
            <button
              onClick={() => setFullscreen(false)}
              className="pointer-events-auto absolute bottom-4 right-4 rounded-lg border border-white/10 bg-zinc-900/40 p-2 text-zinc-300 backdrop-blur-xl transition-colors hover:text-white"
              aria-label={t('common.close')}
            >
              <Minimize2 className="h-4 w-4" />
            </button>
          )}
        </div>
      </div>
    </motion.section>
  );
}
