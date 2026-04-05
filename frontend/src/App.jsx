import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { BrowserRouter, Routes, Route, NavLink, Navigate, useNavigate, useLocation } from 'react-router-dom'
import { OverviewPage } from './pages/OverviewPage.jsx'
import { TradingPage } from './pages/TradingPage.jsx'
import { SafetyPage } from './pages/SafetyPage.jsx'
import { EventsPage } from './pages/EventsPage.jsx'
import { GuidancePage } from './pages/GuidancePage.jsx'
import { ReportPage } from './pages/ReportPage.jsx'
import { SessionReportPage } from './pages/SessionReportPage.jsx'
import { SettingsPage } from './pages/SettingsPage.jsx'
import { ShutdownControlPage } from './pages/ShutdownControlPage.jsx'
import { SymbolTimelinePage } from './pages/SymbolTimelinePage.jsx'
import { SettingsProvider, useSettings } from './context/SettingsContext.jsx'

const REPORTS_PATHS = ['/report', '/session-report', '/events', '/timeline']

function Layout() {
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const { t, i18n } = useTranslation('navigation')
  const navigate = useNavigate()
  const location = useLocation()

  const [reportsOpen, setReportsOpen] = useState(() => REPORTS_PATHS.includes(location.pathname))

  // Auto-expand reports group when navigating directly to a child route
  useEffect(() => {
    if (REPORTS_PATHS.includes(location.pathname)) setReportsOpen(true)
  }, [location.pathname])

  const NAV_ITEMS = [
    { path: '/',         label: t('overview'),       icon: '📊' },
    { path: '/trading',  label: t('tradingSignals'), icon: '💹' },
    { path: '/safety',   label: t('safetyRisk'),     icon: '🛡️' },
    {
      group: 'reports',
      label: t('reports'),
      icon: '📑',
      children: [
        { path: '/report',         label: t('dailyReport'),    icon: '📈' },
        { path: '/session-report', label: t('sessionReport'),  icon: '🕐' },
        { path: '/events',         label: t('eventHistory'),   icon: '📋' },
        { path: '/timeline',       label: t('symbolTimeline'), icon: '⏱️' },
      ],
    },
    { path: '/guidance', label: t('guidance'),        icon: '🧭' },
    { path: '/shutdown', label: t('shutdownControl'), icon: '🛑' },
    { path: '/settings', label: t('systemSettings'),  icon: '⚙️' },
  ]

  // Flat list for mobile header lookup
  const ALL_PATHS = [
    { path: '/', icon: '📊', label: t('overview') },
    { path: '/trading', icon: '💹', label: t('tradingSignals') },
    { path: '/safety', icon: '🛡️', label: t('safetyRisk') },
    { path: '/report', icon: '📈', label: t('dailyReport') },
    { path: '/session-report', icon: '🕐', label: t('sessionReport') },
    { path: '/events', icon: '📋', label: t('eventHistory') },
    { path: '/timeline', icon: '⏱️', label: t('symbolTimeline') },
    { path: '/guidance', icon: '🧭', label: t('guidance') },
    { path: '/shutdown', icon: '🛑', label: t('shutdownControl') },
    { path: '/settings', icon: '⚙️', label: t('systemSettings') },
  ]

  const isReportsActive = REPORTS_PATHS.includes(location.pathname)

  const { tradingMode } = useSettings()

  const toggleLang = () => {
    const next = i18n.language === 'vi' ? 'en' : 'vi'
    i18n.changeLanguage(next)
  }

  const navLinkClass = (isActive) =>
    `w-full flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-colors text-[#dbe7ff] hover:bg-[#1d2b4a] hover:text-white ${
      isActive ? 'bg-[#2f6fed] !text-white' : ''
    }`

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
          {NAV_ITEMS.map(item => {
            if (item.group) {
              return (
                <div key={item.group}>
                  {/* Group parent button */}
                  <button
                    onClick={() => setReportsOpen(o => !o)}
                    className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-colors text-[#dbe7ff] hover:bg-[#1d2b4a] hover:text-white ${
                      isReportsActive && !reportsOpen ? 'bg-[#1d2b4a]' : ''
                    }`}
                  >
                    <span className="text-base">{item.icon}</span>
                    <span className="flex-1 text-left">{item.label}</span>
                    <span className={`text-xs transition-transform duration-200 ${reportsOpen ? 'rotate-90' : ''}`}>▶</span>
                  </button>
                  {/* Children */}
                  {reportsOpen && (
                    <div className="ml-3 mt-0.5 space-y-0.5 pl-3 border-l border-[#1d2b4a]">
                      {item.children.map(child => (
                        <NavLink
                          key={child.path}
                          to={child.path}
                          onClick={() => setSidebarOpen(false)}
                          className={({ isActive }) => navLinkClass(isActive)}
                        >
                          <span className="text-base">{child.icon}</span>
                          {child.label}
                        </NavLink>
                      ))}
                    </div>
                  )}
                </div>
              )
            }
            return (
              <NavLink
                key={item.path}
                to={item.path}
                end={item.path === '/'}
                onClick={() => setSidebarOpen(false)}
                className={({ isActive }) => navLinkClass(isActive)}
              >
                <span className="text-base">{item.icon}</span>
                {item.label}
              </NavLink>
            )
          })}
        </nav>

        {/* Footer — pinned to bottom */}
        <div className="mt-auto px-4 py-4 border-t border-[#1d2b4a] space-y-2">
          {/* Trading mode badge */}
          {tradingMode && (
            <div className="flex items-center gap-2 px-2">
              <span className="text-xs font-semibold rounded-full px-3 py-1 w-full text-center"
                style={
                  tradingMode === 'testnet'
                    ? { background: '#1e3050', border: '1px solid #3b82f6', color: '#93c5fd' }
                    : { background: '#3b1c1c', border: '1px solid #ef4444', color: '#fca5a5' }
                }
              >
                {tradingMode === 'testnet' ? '🧪 TestNet' : '💰 Live'}
              </span>
            </div>
          )}
          <p className="text-xs px-2" style={{ color: '#8ba4cc' }}>{t('dataRefreshes')}</p>
          <p className="text-xs px-2" style={{ color: '#4a6080' }}>{t('every15to30s')}</p>
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
            {ALL_PATHS.find(n => n.path === location.pathname)?.icon}{' '}
            {ALL_PATHS.find(n => n.path === location.pathname)?.label}
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
            <Route path="/timeline"        element={<SymbolTimelinePage />} />
            <Route path="/safety"    element={<SafetyPage />} />
            <Route path="/events"    element={<EventsPage />} />
            <Route path="/guidance"  element={<GuidancePage onNavigate={p => navigate(p)} />} />
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
