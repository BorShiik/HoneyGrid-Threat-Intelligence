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
  Menu,
  Network,
  Search,
  ShieldAlert,
  Bell,
  BellOff,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useConnectionStore } from '@/stores/connectionStore';
import { useSettingsStore } from '@/lib/settings';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { CommandPalette, OPEN_COMMAND_EVENT } from '@/components/CommandPalette';
import { AttackToaster } from '@/components/AttackToaster';

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

function Logo({ expanded }: { expanded: boolean }) {
  return (
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
  );
}

/** Shared navigation list (desktop sidebar + mobile drawer). */
function NavList({
  expanded,
  ariaLabel,
  idPrefix,
  onNavigate,
}: {
  expanded: boolean;
  ariaLabel: string;
  idPrefix: string;
  onNavigate?: () => void;
}) {
  const { t } = useTranslation();
  return (
    <nav aria-label={ariaLabel} className="flex-1 py-3 px-2 space-y-0.5 overflow-y-auto overflow-x-hidden">
      {NAV_ITEMS.map(({ to, labelKey, icon: Icon }) => (
        <NavLink
          key={to}
          to={to}
          end={to === '/'}
          onClick={onNavigate}
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
                  layoutId={`${idPrefix}-active`}
                  className="absolute inset-0 rounded-lg bg-gradient-to-r from-amber-500/15 to-transparent border border-amber-500/20"
                  transition={{ type: 'spring', stiffness: 350, damping: 30 }}
                />
              )}
              {isActive && (
                <motion.div
                  layoutId={`${idPrefix}-indicator`}
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
  );
}

function ConnectionPulse() {
  const { t } = useTranslation();
  const status = useConnectionStore((s) => s.status);
  const simulated = useConnectionStore((s) => s.simulated);
  // The simulator reports status 'connected' so views stay populated, but the
  // data is synthetic — show an amber "demo" state instead of a green "online"
  // dot so the header never implies the stream is real.
  const connectedReal = status === 'connected' && !simulated;
  return (
    <div className="flex items-center gap-2">
      <span className="relative flex h-2.5 w-2.5">
        {connectedReal && (
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
        )}
        <span
          className={cn(
            'relative inline-flex h-2.5 w-2.5 rounded-full',
            connectedReal && 'bg-emerald-500',
            simulated && 'bg-amber-500',
            status === 'disconnected' && !simulated && 'bg-red-500',
            status === 'connecting' && !simulated && 'bg-amber-500 animate-pulse',
          )}
        />
      </span>
      <span className="hidden text-xs text-zinc-400 md:inline" data-testid="connection-status">
        {simulated
          ? t('topbar.simulated')
          : status === 'connected'
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
    <div className="flex items-center gap-2 text-sm min-w-0">
      <span className="hidden text-zinc-500 sm:inline">HoneyGrid</span>
      <span className="hidden text-zinc-600 sm:inline">/</span>
      <span className="truncate font-medium text-zinc-300">{t(current.labelKey)}</span>
    </div>
  );
}

export function AppShell() {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <div className="flex h-screen overflow-hidden bg-[#09090b]">
      {/* ── Desktop sidebar (hover-expand) ── */}
      <motion.aside
        animate={{ width: expanded ? 240 : 72 }}
        transition={{ type: 'spring', stiffness: 300, damping: 30 }}
        onMouseEnter={() => setExpanded(true)}
        onMouseLeave={() => setExpanded(false)}
        className="relative z-30 hidden shrink-0 flex-col glass-strong lg:flex"
        style={{ borderRight: '1px solid rgba(255,255,255,0.06)' }}
      >
        <Logo expanded={expanded} />
        <NavList expanded={expanded} ariaLabel="Nawigacja główna" idPrefix="desktop" />
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

      {/* ── Mobile drawer ── */}
      <AnimatePresence>
        {mobileOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setMobileOpen(false)}
              className="fixed inset-0 z-40 bg-black/60 backdrop-blur-sm lg:hidden"
            />
            <motion.aside
              initial={{ x: -288 }}
              animate={{ x: 0 }}
              exit={{ x: -288 }}
              transition={{ type: 'spring', stiffness: 320, damping: 32 }}
              className="fixed inset-y-0 left-0 z-50 flex w-64 flex-col glass-strong lg:hidden"
              style={{ borderRight: '1px solid rgba(255,255,255,0.06)' }}
            >
              <Logo expanded />
              <NavList
                expanded
                ariaLabel="Nawigacja mobilna"
                idPrefix="mobile"
                onNavigate={() => setMobileOpen(false)}
              />
            </motion.aside>
          </>
        )}
      </AnimatePresence>

      {/* ── Main Column ── */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top Bar */}
        <header
          className="relative z-40 flex h-12 items-center justify-between gap-2 px-3 sm:px-5 glass-strong"
          style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}
        >
          <div className="flex min-w-0 items-center gap-2">
            <button
              onClick={() => setMobileOpen(true)}
              className="rounded-md p-1.5 text-zinc-400 hover:bg-white/5 hover:text-white transition-colors lg:hidden"
              aria-label="Otwórz menu"
            >
              <Menu className="h-5 w-5" />
            </button>
            <Breadcrumb />
          </div>

          <div className="flex items-center gap-2 sm:gap-3">
            <button
              onClick={() => window.dispatchEvent(new CustomEvent(OPEN_COMMAND_EVENT))}
              aria-label={t('topbar.searchPlaceholder')}
              className="flex items-center gap-2 rounded-md bg-white/5 px-2 py-1.5 text-xs text-zinc-500 hover:bg-white/10 hover:text-zinc-300 transition-colors sm:px-3"
            >
              <Search className="h-3.5 w-3.5" />
              <span className="hidden sm:inline">{t('topbar.searchPlaceholder')}</span>
              <kbd className="ml-2 hidden rounded bg-white/5 px-1.5 py-0.5 font-mono text-[10px] text-zinc-600 sm:inline">
                ⌘K
              </kbd>
            </button>
            <button
              onClick={() => useSettingsStore.getState().setMuteToasts(!useSettingsStore.getState().muteToasts)}
              aria-label={useSettingsStore((s) => s.muteToasts) ? 'Unmute alerts' : 'Mute alerts'}
              className="flex items-center justify-center rounded-md p-1.5 text-zinc-400 hover:bg-white/10 hover:text-zinc-200 transition-colors"
            >
              {useSettingsStore((s) => s.muteToasts) ? (
                <BellOff className="h-4 w-4" />
              ) : (
                <Bell className="h-4 w-4" />
              )}
            </button>
            <LanguageSwitcher />
            <ConnectionPulse />
          </div>
        </header>

        {/* Content */}
        <main className="flex-1 overflow-y-auto p-3 sm:p-5">
          <Outlet />
        </main>
      </div>

      {/* Global ⌘K command palette */}
      <CommandPalette />

      {/* Critical-attack toasts */}
      <AttackToaster />
    </div>
  );
}
