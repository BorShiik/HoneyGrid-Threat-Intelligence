import { Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { TooltipProvider } from '@/components/ui/tooltip';
import { AppShell } from '@/components/layout/AppShell';
import { DashboardPage } from '@/pages/DashboardPage';
import { AttackMapPage } from '@/pages/AttackMapPage';
import { LiveFeedPage } from '@/pages/LiveFeedPage';
import { ThreatActorsPage } from '@/pages/ThreatActorsPage';
import { SessionsPage } from '@/pages/SessionsPage';
import { CredentialsPage } from '@/pages/CredentialsPage';
import { IocPage } from '@/pages/IocPage';
import { IncidentsPage } from '@/pages/IncidentsPage';
import { DesignSystemPage } from '@/pages/DesignSystemPage';

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
      <TooltipProvider delayDuration={200}>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<DashboardPage />} />
            <Route path="mapa-atakow" element={<AttackMapPage />} />
            <Route path="strumien" element={<LiveFeedPage />} />
            <Route path="aktorzy" element={<ThreatActorsPage />} />
            <Route path="sesje" element={<SessionsPage />} />
            <Route path="poswiadczenia" element={<CredentialsPage />} />
            <Route path="ioc" element={<IocPage />} />
            <Route path="incydenty" element={<IncidentsPage />} />
            {/* Dev-only design system showcase */}
            <Route path="design-system" element={<DesignSystemPage />} />
          </Route>
        </Routes>
      </TooltipProvider>
    </QueryClientProvider>
  );
}
