import { useQuery } from '@tanstack/react-query';
import { apiGet } from './client';
import type {
  CredentialStats,
  GeoStats,
  HoneypotEvent,
  SessionReplay,
  SessionSummary,
  StatsOverview,
  StixBundle,
  ThreatActor,
} from '@/types/api';

/** Query-key factory — keeps cache keys consistent across the app. */
export const queryKeys = {
  feed: (since?: string, take?: number) => ['feed', { since, take }] as const,
  statsOverview: ['stats', 'overview'] as const,
  statsGeo: ['stats', 'geo'] as const,
  statsCredentials: ['stats', 'credentials'] as const,
  actors: ['actors'] as const,
  actor: (id: string) => ['actors', id] as const,
  sessions: ['sessions'] as const,
  sessionReplay: (id: string) => ['sessions', id, 'replay'] as const,
  iocsStix: ['iocs', 'stix'] as const,
};

/** GET /api/feed?since=&take= */
export function useFeed(options?: { since?: string; take?: number }) {
  return useQuery({
    queryKey: queryKeys.feed(options?.since, options?.take),
    queryFn: () =>
      apiGet<HoneypotEvent[]>('/api/feed', {
        since: options?.since,
        take: options?.take,
      }),
  });
}

/** GET /api/stats/overview */
export function useStatsOverview() {
  return useQuery({
    queryKey: queryKeys.statsOverview,
    queryFn: () => apiGet<StatsOverview>('/api/stats/overview'),
  });
}

/** GET /api/stats/geo */
export function useStatsGeo() {
  return useQuery({
    queryKey: queryKeys.statsGeo,
    queryFn: () => apiGet<GeoStats>('/api/stats/geo'),
  });
}

/** GET /api/stats/credentials */
export function useStatsCredentials() {
  return useQuery({
    queryKey: queryKeys.statsCredentials,
    queryFn: () => apiGet<CredentialStats>('/api/stats/credentials'),
  });
}

/** GET /api/actors */
export function useActors() {
  return useQuery({
    queryKey: queryKeys.actors,
    queryFn: () => apiGet<ThreatActor[]>('/api/actors'),
  });
}

/** GET /api/actors/{id} */
export function useActor(id: string) {
  return useQuery({
    queryKey: queryKeys.actor(id),
    queryFn: () => apiGet<ThreatActor>(`/api/actors/${id}`),
    enabled: id.length > 0,
  });
}

/** GET /api/sessions */
export function useSessions() {
  return useQuery({
    queryKey: queryKeys.sessions,
    queryFn: () => apiGet<SessionSummary[]>('/api/sessions'),
  });
}

/** GET /api/sessions/{id}/replay */
export function useSessionReplay(id: string) {
  return useQuery({
    queryKey: queryKeys.sessionReplay(id),
    queryFn: () => apiGet<SessionReplay>(`/api/sessions/${id}/replay`),
    enabled: id.length > 0,
  });
}

/** GET /api/iocs/stix */
export function useIocsStix() {
  return useQuery({
    queryKey: queryKeys.iocsStix,
    queryFn: () => apiGet<StixBundle>('/api/iocs/stix'),
  });
}
