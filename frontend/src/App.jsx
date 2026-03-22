import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BrowserRouter, Routes, Route, NavLink, Navigate, useNavigate } from 'react-router-dom'
import { OverviewPage } from './pages/OverviewPage.jsx'
import { TradingPage } from './pages/TradingPage.jsx'
import { SafetyPage } from './pages/SafetyPage.jsx'
import { EventsPage } from './pages/EventsPage.jsx'
import { GuidancePage } from './pages/GuidancePage.jsx'
import { ReportPage } from './pages/ReportPage.jsx'
import { SessionReportPage } from './pages/SessionReportPage.jsx'
import { SettingsPage } from './pages/SettingsPage.jsx'
import { BudgetPage } from './pages/BudgetPage.jsx'
import { ShutdownControlPage } from './pages/ShutdownControlPage.jsx'
import { SettingsProvider } from './context/SettingsContext.jsx'

function Layout() {
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const { t, i18n } = useTranslation('navigation')
  const navigate = useNavigate()

  const NAV_ITEMS = [
    { path: '/',          label: t('overview'),        icon: '📊' },
    { path: '/trading',   label: t('tradingSignals'),  icon: '💹' },
    { path: '/safety',    label: t('safetyRisk'),      icon: '🛡️' },
    { path: '/events',    label: t('eventHistory'),    icon: '📋' },
    { path: '/report',         label: t('dailyReport'),     icon: '📈' },
    { path: '/session-report', label: t('sessionReport'),   icon: '🕐' },
    { path: '/guidance',  label: t('guidance'),        icon: '🧭' },
    { path: '/budget',    label: t('budget'),            icon: '💰' },
    { path: '/shutdown',  label: t('shutdownControl'),  icon: '🛑' },
    { path: '/settings',  label: t('systemSettings'),  icon: '⚙️' },
  ]

  const toggleLang = () => {
    const next = i18n.language === 'vi' ? 'en' : 'vi'
    i18n.changeLanguage(next)
  }

  return (
    <div className="min-h-screen flex">
      {/* Mobile overlay */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 bg-black bg-opacity-40 z-20 lg:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside className={`
        fixed top-0 left-0 h-full w-64 flex flex-col z-30
        transform transition-transform duration-200 ease-in-out
        border-r border-[#1d2b4a]
        ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
        lg:sticky lg:top-0 lg:h-screen lg:translate-x-0 lg:flex-shrink-0 lg:z-auto
      `} style={{ background: '#0b1730' }}>
        {/* Brand */}
        <div className="px-6 py-5 border-b border-[#1d2b4a] flex items-start justify-between gap-2">
          <div>
            <h1 className="text-lg font-bold text-white">🤖 CryptoTrader</h1>
            <p className="text-xs mt-0.5" style={{ color: '#8ba4cc' }}>{t('brandSubtitle')}</p>
          </div>
          <button
            onClick={toggleLang}
            className="shrink-0 mt-1 text-xs font-semibold px-2 py-1 rounded-md border border-[#2f6fed] text-[#7fb3ff] hover:bg-[#1d2b4a] transition-colors"
            title="Switch language"
          >
            {t('switchLang')}
          </button>
        </div>

        {/* Nav */}
        <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">
          {NAV_ITEMS.map(item => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.path === '/'}
              onClick={() => setSidebarOpen(false)}
              className={({ isActive }) =>
                `w-full flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-colors text-[#dbe7ff] hover:bg-[#1d2b4a] hover:text-white ${
                  isActive ? 'bg-[#2f6fed] !text-white' : ''
                }`
              }
            >
              <span className="text-base">{item.icon}</span>
              {item.label}
            </NavLink>
          ))}
        </nav>

        {/* Footer — pinned to bottom */}
        <div className="mt-auto px-6 py-4 border-t border-[#1d2b4a]">
          <p className="text-xs" style={{ color: '#8ba4cc' }}>{t('dataRefreshes')}</p>
          <p className="text-xs mt-0.5" style={{ color: '#4a6080' }}>{t('every15to30s')}</p>
        </div>
      </aside>

      {/* Main content */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Mobile header */}
        <header className="lg:hidden bg-white border-b border-[#dbe4ef] px-4 py-3 flex items-center gap-3 sticky top-0 z-10">
          <button
            onClick={() => setSidebarOpen(true)}
            className="p-2 rounded-lg text-gray-600 hover:bg-gray-100"
          >
            ☰
          </button>
          <h1 className="font-semibold text-gray-800 flex-1">
            {NAV_ITEMS.find(n => n.path === window.location.pathname)?.icon}{' '}
            {NAV_ITEMS.find(n => n.path === window.location.pathname)?.label}
          </h1>
          <button
            onClick={toggleLang}
            className="text-xs font-semibold px-2 py-1 rounded-md border border-gray-300 text-gray-600 hover:bg-gray-100 transition-colors"
          >
            {t('switchLang')}
          </button>
        </header>

        {/* Page content */}
        <main className="flex-1 p-4 lg:p-6 max-w-7xl mx-auto w-full">
          <Routes>
            <Route path="/"          element={<OverviewPage />} />
            <Route path="/trading"   element={<TradingPage />} />
            <Route path="/report"          element={<ReportPage />} />
            <Route path="/session-report"  element={<SessionReportPage />} />
            <Route path="/safety"    element={<SafetyPage />} />
            <Route path="/events"    element={<EventsPage />} />
            <Route path="/guidance"  element={<GuidancePage onNavigate={p => navigate(p)} />} />
            <Route path="/budget"    element={<BudgetPage />} />
            <Route path="/shutdown"  element={<ShutdownControlPage />} />
            <Route path="/settings"  element={<SettingsPage />} />
            <Route path="*"          element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <SettingsProvider>
        <Layout />
      </SettingsProvider>
    </BrowserRouter>
  )
}
