import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'

import viNavigation from './locales/vi/navigation.json'
import viCommon from './locales/vi/common.json'
import viOverview from './locales/vi/overview.json'
import viTrading from './locales/vi/trading.json'
import viSafety from './locales/vi/safety.json'
import viEvents from './locales/vi/events.json'
import viGuidance from './locales/vi/guidance.json'
import viReport from './locales/vi/report.json'
import viSessionReport from './locales/vi/session-report.json'
import viSettings from './locales/vi/settings.json'
import viShutdown from './locales/vi/shutdown.json'
import viTimeline from './locales/vi/timeline.json'

import enNavigation from './locales/en/navigation.json'
import enCommon from './locales/en/common.json'
import enOverview from './locales/en/overview.json'
import enTrading from './locales/en/trading.json'
import enSafety from './locales/en/safety.json'
import enEvents from './locales/en/events.json'
import enGuidance from './locales/en/guidance.json'
import enReport from './locales/en/report.json'
import enSessionReport from './locales/en/session-report.json'
import enSettings from './locales/en/settings.json'
import enShutdown from './locales/en/shutdown.json'
import enTimeline from './locales/en/timeline.json'

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      vi: {
        navigation: viNavigation,
        common: viCommon,
        overview: viOverview,
        trading: viTrading,
        safety: viSafety,
        events: viEvents,
        guidance: viGuidance,
        report: viReport,
        sessionReport: viSessionReport,
        settings: viSettings,
        shutdown: viShutdown,
        timeline: viTimeline,
      },
      en: {
        navigation: enNavigation,
        common: enCommon,
        overview: enOverview,
        trading: enTrading,
        safety: enSafety,
        events: enEvents,
        guidance: enGuidance,
        report: enReport,
        sessionReport: enSessionReport,
        settings: enSettings,
        shutdown: enShutdown,
        timeline: enTimeline,
      },
    },
    lng: 'vi',
    fallbackLng: 'en',
    defaultNS: 'common',
    detection: {
      order: ['localStorage'],
      lookupLocalStorage: 'lang',
      cacheUserLanguage: true,
    },
    interpolation: {
      escapeValue: false,
    },
  })

export default i18n
