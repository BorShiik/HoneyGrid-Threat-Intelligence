import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Activity,
  Brain,
  CornerDownLeft,
  Fingerprint,
  Globe2,
  KeyRound,
  LayoutDashboard,
  Languages,
  Network,
  Search,
  ShieldAlert,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { LANGS, LANG_META } from '@/i18n';

/** Custom event the topbar search button dispatches to open the palette. */
export const OPEN_COMMAND_EVENT = 'hg:command';

interface Command {
  id: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  group: 'nav' | 'lang';
  run: () => void;
}

const NAV: { to: string; labelKey: string; icon: Command['icon'] }[] = [
  { to: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard },
  { to: '/threat-map', labelKey: 'nav.threatMap', icon: Globe2 },
  { to: '/live-feed', labelKey: 'nav.liveFeed', icon: Activity },
  { to: '/actors', labelKey: 'nav.analytics', icon: Fingerprint },
  { to: '/sdn', labelKey: 'nav.sdn', icon: Network },
  { to: '/credentials', labelKey: 'nav.credentials', icon: KeyRound },
  { to: '/ioc', labelKey: 'nav.ioc', icon: ShieldAlert },
  { to: '/ai-integrations', labelKey: 'nav.ai', icon: Brain },
];

/**
 * Global command palette (⌘K / Ctrl+K). Self-contained: registers its own
 * keyboard shortcut and listens for {@link OPEN_COMMAND_EVENT} from the topbar.
 * Provides quick navigation and language switching with full keyboard control.
 */
export function CommandPalette() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const close = useCallback(() => {
    setOpen(false);
    setQuery('');
  }, []);

  const commands = useMemo<Command[]>(() => {
    const nav = NAV.map((n) => ({
      id: `nav:${n.to}`,
      label: t(n.labelKey),
      icon: n.icon,
      group: 'nav' as const,
      run: () => {
        navigate(n.to);
        close();
      },
    }));
    const langs = LANGS.map((l) => ({
      id: `lang:${l}`,
      label: `${LANG_META[l].flag}  ${LANG_META[l].label}`,
      icon: Languages,
      group: 'lang' as const,
      run: () => {
        void i18n.changeLanguage(l);
        close();
      },
    }));
    return [...nav, ...langs];
  }, [t, i18n, navigate, close]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return commands;
    return commands.filter((c) => c.label.toLowerCase().includes(q));
  }, [commands, query]);

  // Global ⌘K / Ctrl+K toggle + custom open event.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setOpen((o) => !o);
      }
    };
    const onOpen = () => setOpen(true);
    window.addEventListener('keydown', onKey);
    window.addEventListener(OPEN_COMMAND_EVENT, onOpen);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener(OPEN_COMMAND_EVENT, onOpen);
    };
  }, []);

  // Focus the input + reset selection when opening.
  useEffect(() => {
    if (open) {
      setSelected(0);
      const id = window.setTimeout(() => inputRef.current?.focus(), 30);
      return () => window.clearTimeout(id);
    }
  }, [open]);

  useEffect(() => setSelected(0), [query]);

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setSelected((s) => Math.min(s + 1, filtered.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setSelected((s) => Math.max(s - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      filtered[selected]?.run();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      close();
    }
  };

  const firstLangIndex = filtered.findIndex((c) => c.group === 'lang');

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[60] flex items-start justify-center p-4 pt-[12vh]"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
        >
          <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={close} />
          <motion.div
            role="dialog"
            aria-modal="true"
            aria-label="Command palette"
            initial={{ opacity: 0, scale: 0.98, y: -8 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.98, y: -8 }}
            transition={{ type: 'spring', stiffness: 380, damping: 30 }}
            className="relative w-full max-w-xl overflow-hidden rounded-2xl glass-strong shadow-2xl"
          >
            {/* Input */}
            <div className="flex items-center gap-3 border-b border-white/5 px-4">
              <Search className="h-4 w-4 shrink-0 text-zinc-500" />
              <input
                ref={inputRef}
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={onKeyDown}
                placeholder={t('topbar.searchPlaceholder')}
                aria-label={t('topbar.searchPlaceholder')}
                className="h-12 w-full bg-transparent text-sm text-zinc-100 placeholder:text-zinc-600 focus:outline-none"
              />
              <kbd className="rounded bg-white/5 px-1.5 py-0.5 font-mono text-[10px] text-zinc-600">ESC</kbd>
            </div>

            {/* Results */}
            <ul className="max-h-80 overflow-y-auto p-2" role="listbox">
              {filtered.length === 0 && (
                <li className="px-3 py-8 text-center text-sm text-zinc-600">{t('common.noData')}</li>
              )}
              {filtered.map((cmd, i) => {
                const Icon = cmd.icon;
                const isActive = i === selected;
                return (
                  <li key={cmd.id}>
                    {i === firstLangIndex && (
                      <div className="px-3 pb-1 pt-3 text-[10px] uppercase tracking-wider text-zinc-600">
                        {t('language.label')}
                      </div>
                    )}
                    <button
                      role="option"
                      aria-selected={isActive}
                      onMouseEnter={() => setSelected(i)}
                      onClick={() => cmd.run()}
                      className={cn(
                        'flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm transition-colors',
                        isActive ? 'bg-amber-500/10 text-white' : 'text-zinc-400 hover:bg-white/5',
                      )}
                    >
                      <Icon className={cn('h-4 w-4 shrink-0', isActive ? 'text-amber-500' : 'text-zinc-500')} />
                      <span className="flex-1 text-left">{cmd.label}</span>
                      {isActive && <CornerDownLeft className="h-3.5 w-3.5 text-zinc-600" />}
                    </button>
                  </li>
                );
              })}
            </ul>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
