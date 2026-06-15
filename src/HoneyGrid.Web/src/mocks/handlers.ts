import { http, HttpResponse } from 'msw';
import type { CredentialStats, GeoStats, StatsOverview, ThreatActor } from '@/types/api';
import {
  generateFeed,
  generateSessionReplay,
  generateStixBundle,
  MOCK_ACTORS,
  MOCK_SESSIONS,
} from './generator';

/** MSW request handlers covering the full HoneyGrid REST contract. */
export const handlers = [
  // GET /api/feed?since=&take=
  http.get('/api/feed', ({ request }) => {
    const url = new URL(request.url);
    const take = Number(url.searchParams.get('take') ?? '50');
    const since = url.searchParams.get('since');
    let feed = generateFeed(Math.min(Math.max(take, 1), 200));
    if (since) {
      feed = feed.filter((e) => e.timestamp > since);
    }
    return HttpResponse.json(feed);
  }),

  // GET /api/stats/overview
  http.get('/api/stats/overview', () => {
    const overview: StatsOverview = {
      totalEvents: 412_338,
      eventsLast24h: 18_204,
      uniqueAttackers: 6_412,
      activeSessions: 14,
      topCountries: [
        { country: 'CN', countryName: 'Chiny', count: 5840 },
        { country: 'RU', countryName: 'Rosja', count: 4112 },
        { country: 'US', countryName: 'Stany Zjednoczone', count: 2904 },
        { country: 'BR', countryName: 'Brazylia', count: 1733 },
        { country: 'IN', countryName: 'Indie', count: 1421 },
      ],
      eventsBySensorType: { ssh: 11_204, web: 5_113, rdp: 1_887 },
      eventsByType: {
        'login.failed': 12_490,
        'login.success': 312,
        command: 1_204,
        'http.request': 3_866,
        connect: 332,
      },
    };
    return HttpResponse.json(overview);
  }),

  // GET /api/stats/geo
  http.get('/api/stats/geo', () => {
    const geo: GeoStats = {
      points: [
        { country: 'CN', countryName: 'Chiny', lat: 31.23, lon: 121.47, count: 5840 },
        { country: 'RU', countryName: 'Rosja', lat: 55.75, lon: 37.62, count: 4112 },
        { country: 'US', countryName: 'Stany Zjednoczone', lat: 39.04, lon: -77.49, count: 2904 },
        { country: 'BR', countryName: 'Brazylia', lat: -23.55, lon: -46.63, count: 1733 },
        { country: 'IN', countryName: 'Indie', lat: 19.08, lon: 72.88, count: 1421 },
        { country: 'VN', countryName: 'Wietnam', lat: 21.03, lon: 105.85, count: 980 },
        { country: 'NL', countryName: 'Holandia', lat: 52.37, lon: 4.9, count: 715 },
        { country: 'IR', countryName: 'Iran', lat: 35.69, lon: 51.39, count: 402 },
      ],
      updatedAt: new Date().toISOString(),
    };
    return HttpResponse.json(geo);
  }),

  // GET /api/stats/credentials
  http.get('/api/stats/credentials', () => {
    const stats: CredentialStats = {
      topUsernames: [
        { username: 'root', count: 8120 },
        { username: 'admin', count: 4310 },
        { username: 'ubuntu', count: 1290 },
        { username: 'pi', count: 880 },
        { username: 'test', count: 644 },
      ],
      topPasswords: [
        { password: '123456', count: 5230 },
        { password: 'admin', count: 3180 },
        { password: 'password', count: 2090 },
        { password: 'root', count: 1740 },
        { password: 'raspberry', count: 860 },
      ],
      topPairs: [
        { username: 'root', password: '123456', count: 4110 },
        { username: 'admin', password: 'admin', count: 2980 },
        { username: 'root', password: 'root', count: 1620 },
        { username: 'pi', password: 'raspberry', count: 840 },
        { username: 'administrator', password: 'P@ssw0rd', count: 512 },
      ],
      totalAttempts: 31_240,
    };
    return HttpResponse.json(stats);
  }),

  // GET /api/actors
  http.get('/api/actors', () => HttpResponse.json(MOCK_ACTORS)),

  // GET /api/actors/{id}
  http.get('/api/actors/:id', ({ params }) => {
    const actor: ThreatActor | undefined = MOCK_ACTORS.find((a) => a.id === params.id);
    if (!actor) {
      return HttpResponse.json({ title: 'Nie znaleziono aktora' }, { status: 404 });
    }
    return HttpResponse.json(actor);
  }),

  // GET /api/sessions
  http.get('/api/sessions', () => HttpResponse.json(MOCK_SESSIONS)),

  // GET /api/sessions/{id}/replay
  http.get('/api/sessions/:id/replay', ({ params }) =>
    HttpResponse.json(generateSessionReplay(String(params.id))),
  ),

  // GET /api/iocs/stix
  http.get('/api/iocs/stix', () => HttpResponse.json(generateStixBundle())),

  // GET /api/sdn/nodes
  http.get('/api/sdn/nodes', () => {
    return HttpResponse.json([
      { id: 'sdn-01', name: 'Edge-WEU-01', location: 'frankfurt', status: 'active', dynamicMigration: true },
      { id: 'sdn-02', name: 'Edge-WEU-02', location: 'amsterdam', status: 'active', dynamicMigration: false },
      { id: 'sdn-03', name: 'Core-NEU-01', location: 'dublin', status: 'active', dynamicMigration: true },
      { id: 'sdn-04', name: 'Edge-EUS-01', location: 'virginia', status: 'degraded', dynamicMigration: true },
      { id: 'sdn-05', name: 'Edge-SEA-01', location: 'singapore', status: 'active', dynamicMigration: false },
      { id: 'sdn-06', name: 'Core-WUS-01', location: 'seattle', status: 'offline', dynamicMigration: false },
    ]);
  }),

  // POST /api/sdn/nodes/{id}/migration
  http.post('/api/sdn/nodes/:id/migration', async ({ params }) => {
    // Return a mock toggled node
    return HttpResponse.json({
      id: params.id,
      dynamicMigration: true // In a real mock we'd track state, but returning success is enough for UI
    });
  }),

  // GET /api/mcp/servers
  http.get('/api/mcp/servers', () => {
    return HttpResponse.json([
      {
        id: 'mcp-01', name: 'ThreatIntel Analyzer', provider: 'Cloudflare Workers AI',
        status: 'connected', endpoint: 'https://ti-analyzer.honeygrid.workers.dev',
        tools: ['query_threat_logs', 'enrich_ip', 'classify_attack', 'generate_ioc'],
        lastPing: 12, requestsToday: 847,
      },
      {
        id: 'mcp-02', name: 'Sentinel Bridge', provider: 'Azure Functions',
        status: 'connected', endpoint: 'https://hg-sentinel-bridge.azurewebsites.net/mcp',
        tools: ['create_incident', 'update_watchlist', 'run_kql_query'],
        lastPing: 34, requestsToday: 312,
      },
      {
        id: 'mcp-03', name: 'Actor Profiler', provider: 'Azure OpenAI (gpt-4o-mini)',
        status: 'connected', endpoint: 'https://hg-ai-profiler.openai.azure.com',
        tools: ['build_actor_dossier', 'cluster_sessions', 'assess_sophistication'],
        lastPing: 89, requestsToday: 156,
      },
      {
        id: 'mcp-04', name: 'OSINT Enrichment', provider: 'Self-hosted',
        status: 'disconnected', endpoint: 'https://osint.internal.honeygrid.net/v1',
        tools: ['whois_lookup', 'dns_history', 'certificate_transparency'],
        lastPing: -1, requestsToday: 0,
      }
    ]);
  }),
];
