import { useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Activity,
  Brain,
  ChevronLeft,
  ChevronRight,
  Fingerprint,
  Globe2,
  Hexagon,
  KeyRound,
  LayoutDashboard,
  Network,
  Search,
  ShieldAlert,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useConnectionStore } from '@/stores/connectionStore';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { CommandPalette, OPEN_COMMAND_EVENT } from '@/components/CommandPalette';

interface NavItem {
  to: string;
  labelKey: string;
  icon: React.ComponentType<{ className?: string }>;
}

const NAV_ITEMS: NavItem[] = [
  { to: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard },
  { to: '/threat-map', labelKey: 'nav.threatMap', icon: Globe2 },
  { to: '/live-feed', labelKey: 'nav.liveFeed', icon: Activity },
  { to: '/actors', labelKey: 'nav.analytics', icon: Fingerprint },
  { to: '/sdn', labelKey: 'nav.sdn', icon: Network },
  { to: '/credentials', labelKey: 'nav.credentials', icon: KeyRound },
  { to: '/ioc', labelKey: 'nav.ioc', icon: ShieldAlert },
  { to: '/ai-integrations', labelKey: 'nav.ai', icon: Brain },
];

function ConnectionPulse() {
  const { t } = useTranslation();
  const status = useConnectionStore((s) => s.status);
  return (
    <div className="flex items-center gap-2">
      <span className="relative flex h-2.5 w-2.5">
        {status === 'connected' && (
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
        )}
        <span
          className={cn(
            'relative inline-flex h-2.5 w-2.5 rounded-full',
            status === 'connected' && 'bg-emerald-500',
            status === 'disconnected' && 'bg-red-500',
            status === 'connecting' && 'bg-amber-500 animate-pulse',
          )}
        />
      </span>
      <span className="text-xs text-zinc-400" data-testid="connection-status">
        {status === 'connected'
          ? t('topbar.online')
          : status === 'connecting'
            ? t('topbar.connecting')
            : t('topbar.offline')}
      </span>
    </div>
  );
}

function Breadcrumb() {
  const { t } = useTranslation();
  const location = useLocation();
  const current = NAV_ITEMS.find((n) => n.to === location.pathname) ?? NAV_ITEMS[0];
  return (
    <div className="flex items-center gap-2 text-sm">
      <span className="text-zinc-500">HoneyGrid</span>
      <span className="text-zinc-600">/</span>
      <span className="text-zinc-300 font-medium">{t(current.labelKey)}</span>
    </div>
  );
}

export function AppShell() {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="flex h-screen overflow-hidden bg-[#09090b]">
      {/* ── Sidebar ── */}
      <motion.aside
        animate={{ width: expanded ? 240 : 72 }}
        transition={{ type: 'spring', stiffness: 300, damping: 30 }}
        onMouseEnter={() => setExpanded(true)}
        onMouseLeave={() => setExpanded(false)}
        className="relative z-30 flex shrink-0 flex-col glass-strong"
        style={{ borderRight: '1px solid rgba(255,255,255,0.06)' }}
      >
        {/* Logo */}
        <div className="flex h-14 items-center gap-2.5 px-4 border-b border-white/5">
          <Hexagon className="h-7 w-7 text-amber-500 shrink-0" aria-hidden />
          <AnimatePresence>
            {expanded && (
              <motion.span
                initial={{ opacity: 0, x: -8 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: -8 }}
                transition={{ duration: 0.15 }}
                className="text-lg font-bold tracking-tight whitespace-nowrap"
              >
                Honey<span className="text-amber-500">Grid</span>
              </motion.span>
            )}
          </AnimatePresence>
        </div>

        {/* Navigation */}
        <nav aria-label="Nawigacja główna" className="flex-1 py-3 px-2 space-y-0.5 overflow-hidden">
          {NAV_ITEMS.map(({ to, labelKey, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                cn(
                  'group relative flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-all duration-200',
                  isActive ? 'text-white' : 'text-zinc-500 hover:text-zinc-200 hover:bg-white/5',
                )
              }
            >
              {({ isActive }) => (
                <>
                  {isActive && (
                    <motion.div
                      layoutId="sidebar-active"
                      className="absolute inset-0 rounded-lg bg-gradient-to-r from-amber-500/15 to-transparent border border-amber-500/20"
                      transition={{ type: 'spring', stiffness: 350, damping: 30 }}
                    />
                  )}
                  {isActive && (
                    <motion.div
                      layoutId="sidebar-indicator"
                      className="absolute left-0 top-1/2 -translate-y-1/2 h-6 w-0.5 rounded-r-full bg-amber-500"
                      transition={{ type: 'spring', stiffness: 350, damping: 30 }}
                    />
                  )}
                  <Icon className={cn('h-[18px] w-[18px] shrink-0 relative z-10', isActive && 'text-amber-500')} />
                  <AnimatePresence>
                    {expanded && (
                      <motion.span
                        initial={{ opacity: 0, x: -4 }}
                        animate={{ opacity: 1, x: 0 }}
                        exit={{ opacity: 0, x: -4 }}
                        transition={{ duration: 0.12 }}
                        className="relative z-10 whitespace-nowrap"
                      >
                        {t(labelKey)}
                      </motion.span>
                    )}
                  </AnimatePresence>
                </>
              )}
            </NavLink>
          ))}
        </nav>

        {/* Collapse toggle */}
        <div className="border-t border-white/5 p-2">
          <button
            onClick={() => setExpanded((p) => !p)}
            className="flex w-full items-center justify-center rounded-md p-2 text-zinc-500 hover:text-zinc-300 hover:bg-white/5 transition-colors"
            aria-label={expanded ? 'Zwiń menu' : 'Rozwiń menu'}
          >
            {expanded ? <ChevronLeft className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
          </button>
        </div>
      </motion.aside>

      {/* ── Main Column ── */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top Bar */}
        <header
          className="flex h-12 items-center justify-between px-5 glass-strong"
          style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}
        >
          <Breadcrumb />
          <div className="flex items-center gap-3">
            <button
              onClick={() => window.dispatchEvent(new CustomEvent(OPEN_COMMAND_EVENT))}
              className="flex items-center gap-2 rounded-md bg-white/5 px-3 py-1.5 text-xs text-zinc-500 hover:bg-white/10 hover:text-zinc-300 transition-colors"
            >
              <Search className="h-3.5 w-3.5" />
              <span>{t('topbar.searchPlaceholder')}</span>
              <kbd className="ml-2 rounded bg-white/5 px-1.5 py-0.5 font-mono text-[10px] text-zinc-600">⌘K</kbd>
            </button>
            <LanguageSwitcher />
            <ConnectionPulse />
          </div>
        </header>

        {/* Content */}
        <main className="flex-1 overflow-y-auto p-5">
          <Outlet />
        </main>
      </div>

      {/* Global ⌘K command palette */}
      <CommandPalette />
    </div>
  );
}
