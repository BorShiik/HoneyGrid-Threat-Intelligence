import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { pl } from './locales/pl';
import { en } from './locales/en';
import { ru } from './locales/ru';

/** Supported UI languages. Polish is the default. */
export const LANGS = ['pl', 'en', 'ru'] as const;
export type Lang = (typeof LANGS)[number];

export const LANG_META: Record<Lang, { label: string; flag: string }> = {
  pl: { label: 'Polski', flag: '🇵🇱' },
  en: { label: 'English', flag: '🇬🇧' },
  ru: { label: 'Русский', flag: '🇷🇺' },
};

/** localStorage key used to persist the user's language choice. */
export const LANG_STORAGE_KEY = 'honeygrid-lang';

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      pl: { translation: pl },
      en: { translation: en },
      ru: { translation: ru },
    },
    fallbackLng: 'pl',
    supportedLngs: [...LANGS],
    load: 'languageOnly',
    interpolation: { escapeValue: false }, // React already escapes
    react: { useSuspense: false }, // resources are bundled — no async load / Suspense
    detection: {
      // Default to Polish unless the user has explicitly chosen a language.
      order: ['localStorage'],
      lookupLocalStorage: LANG_STORAGE_KEY,
      caches: ['localStorage'],
    },
  });

// Keep <html lang> in sync with the active language (a11y + SEO).
function syncHtmlLang(lng: string) {
  if (typeof document !== 'undefined') {
    document.documentElement.lang = (lng || 'pl').split('-')[0];
  }
}
i18n.on('languageChanged', syncHtmlLang);
syncHtmlLang(i18n.language);

export default i18n;
