import { Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MotionConfig } from 'framer-motion';
import { TooltipProvider } from '@/components/ui/tooltip';
import { AppShell } from '@/components/layout/AppShell';
import { DashboardPage } from '@/pages/DashboardPage';
import { ThreatMapPage } from '@/pages/ThreatMapPage';
import { LiveFeedPage } from '@/pages/LiveFeedPage';
import { ThreatActorsPage } from '@/pages/ThreatActorsPage';
import { CredentialsPage } from '@/pages/CredentialsPage';
import { IocPage } from '@/pages/IocPage';
import { SdnMonitoringPage } from '@/pages/SdnMonitoringPage';
import { AiIntegrationsPage } from '@/pages/AiIntegrationsPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <MotionConfig reducedMotion="user">
        <TooltipProvider delayDuration={200}>
          <Routes>
            <Route element={<AppShell />}>
              <Route index element={<DashboardPage />} />
              <Route path="threat-map" element={<ThreatMapPage />} />
              <Route path="live-feed" element={<LiveFeedPage />} />
              <Route path="actors" element={<ThreatActorsPage />} />
              <Route path="credentials" element={<CredentialsPage />} />
              <Route path="ioc" element={<IocPage />} />
              <Route path="sdn" element={<SdnMonitoringPage />} />
              <Route path="ai-integrations" element={<AiIntegrationsPage />} />
            </Route>
          </Routes>
        </TooltipProvider>
      </MotionConfig>
    </QueryClientProvider>
  );
}
