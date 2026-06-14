import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Check } from 'lucide-react';
import { cn } from '@/lib/utils';
import { LANGS, LANG_META, type Lang } from '@/i18n';

/**
 * Compact glass language switcher (PL / EN / RU). Persists the choice via
 * i18next's localStorage detector and animates the dropdown with spring physics.
 */
export function LanguageSwitcher() {
  const { i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const current: Lang = (LANGS as readonly string[]).includes(i18n.language)
    ? (i18n.language as Lang)
    : 'pl';

  // Close on outside click.
  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, []);

  const select = (lang: Lang) => {
    void i18n.changeLanguage(lang);
    setOpen(false);
  };

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label="Language"
        className="flex items-center gap-1.5 rounded-md bg-white/5 px-2.5 py-1.5 text-xs text-zinc-300 hover:bg-white/10 transition-colors"
      >
        <span className="text-base leading-none">{LANG_META[current].flag}</span>
        <span className="font-medium uppercase">{current}</span>
      </button>

      <AnimatePresence>
        {open && (
          <motion.ul
            initial={{ opacity: 0, y: -6, scale: 0.96 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -6, scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 400, damping: 30 }}
            role="listbox"
            className="absolute right-0 top-full z-50 mt-2 w-40 overflow-hidden rounded-xl glass-strong p-1"
          >
            {LANGS.map((l) => (
              <li key={l}>
                <button
                  role="option"
                  aria-selected={l === current}
                  onClick={() => select(l)}
                  className={cn(
                    'flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-sm transition-colors',
                    l === current ? 'bg-white/10 text-white' : 'text-zinc-400 hover:bg-white/5 hover:text-zinc-200',
                  )}
                >
                  <span className="text-base leading-none">{LANG_META[l].flag}</span>
                  <span className="flex-1 text-left">{LANG_META[l].label}</span>
                  {l === current && <Check className="h-3.5 w-3.5 text-amber-500" />}
                </button>
              </li>
            ))}
          </motion.ul>
        )}
      </AnimatePresence>
    </div>
  );
}
