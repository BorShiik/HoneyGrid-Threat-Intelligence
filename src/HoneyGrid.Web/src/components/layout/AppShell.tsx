import { NavLink, Outlet } from 'react-router-dom';
import {
  Activity,
  Fingerprint,
  Globe2,
  Hexagon,
  KeyRound,
  LayoutDashboard,
  ShieldAlert,
  Siren,
  TerminalSquare,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useConnectionStore } from '@/stores/connectionStore';

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
}

const NAV_ITEMS: NavItem[] = [
  { to: '/', label: 'Pulpit', icon: LayoutDashboard },
  { to: '/mapa-atakow', label: 'Mapa ataków', icon: Globe2 },
  { to: '/strumien', label: 'Strumień na żywo', icon: Activity },
  { to: '/aktorzy', label: 'Aktorzy zagrożeń', icon: Fingerprint },
  { to: '/sesje', label: 'Sesje / Odtwarzanie', icon: TerminalSquare },
  { to: '/poswiadczenia', label: 'Analiza poświadczeń', icon: KeyRound },
  { to: '/ioc', label: 'Wskaźniki IoC (STIX)', icon: ShieldAlert },
  { to: '/incydenty', label: 'Incydenty (SOC)', icon: Siren },
];

const STATUS_LABELS = {
  connected: 'Połączono',
  disconnected: 'Rozłączono',
  connecting: 'Łączenie…',
} as const;

function ConnectionIndicator() {
  const status = useConnectionStore((s) => s.status);
  return (
    <div className="flex items-center gap-2 text-sm text-muted-foreground">
      <span
        aria-hidden
        className={cn(
          'inline-block h-2.5 w-2.5 rounded-full',
          status === 'connected' && 'bg-status-online shadow-[0_0_6px] shadow-status-online',
          status === 'disconnected' && 'bg-status-offline',
          status === 'connecting' && 'animate-pulse bg-severity-medium',
        )}
      />
      <span data-testid="connection-status">{STATUS_LABELS[status]}</span>
    </div>
  );
}

export function AppShell() {
  return (
    <div className="flex min-h-screen">
      {/* Sidebar */}
      <aside className="flex w-64 shrink-0 flex-col border-r bg-card/40">
        <div className="flex h-14 items-center gap-2 border-b px-4">
          <Hexagon className="h-6 w-6 text-primary" aria-hidden />
          <span className="text-lg font-bold tracking-tight">
            Honey<span className="text-primary">Grid</span>
          </span>
        </div>
        <nav aria-label="Nawigacja główna" className="flex-1 space-y-1 p-3">
          {NAV_ITEMS.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-accent text-accent-foreground'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                )
              }
            >
              <Icon className="h-4 w-4" />
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t p-3 text-xs text-muted-foreground">
          Platforma analizy zagrożeń
          <br />
          Tydzień 0 — fundament
        </div>
      </aside>

      {/* Main column */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center justify-between border-b bg-card/40 px-6">
          <h1 className="text-sm font-semibold text-muted-foreground">
            HoneyGrid — centrum operacji bezpieczeństwa
          </h1>
          <ConnectionIndicator />
        </header>
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
