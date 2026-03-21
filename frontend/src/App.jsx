import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { OverviewPage } from './pages/OverviewPage.jsx'
import { TradingPage } from './pages/TradingPage.jsx'
import { SafetyPage } from './pages/SafetyPage.jsx'
import { EventsPage } from './pages/EventsPage.jsx'
import { GuidancePage } from './pages/GuidancePage.jsx'

export default function App() {
  const [page, setPage] = useState('overview')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const { t, i18n } = useTranslation('navigation')

  const NAV_ITEMS = [
    { id: 'overview', label: t('overview'), icon: '📊' },
    { id: 'trading', label: t('tradingSignals'), icon: '💹' },
    { id: 'safety', label: t('safetyRisk'), icon: '🛡️' },
    { id: 'events', label: t('eventHistory'), icon: '📋' },
    { id: 'guidance', label: t('guidance'), icon: '🧭' },
  ]

  const toggleLang = () => {
    const next = i18n.language === 'vi' ? 'en' : 'vi'
    i18n.changeLanguage(next)
  }

  const renderPage = () => {
    switch (page) {
      case 'overview': return <OverviewPage />
      case 'trading': return <TradingPage />
      case 'safety': return <SafetyPage />
      case 'events': return <EventsPage />
      case 'guidance': return <GuidancePage onNavigate={p => { setPage(p); setSidebarOpen(false) }} />
      default: return <OverviewPage />
    }
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
            <button
              key={item.id}
              onClick={() => { setPage(item.id); setSidebarOpen(false) }}
              className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-colors text-[#dbe7ff] hover:bg-[#1d2b4a] hover:text-white ${
                page === item.id ? 'bg-[#2f6fed] !text-white' : ''
              }`}
            >
              <span className="text-base">{item.icon}</span>
              {item.label}
            </button>
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
            {NAV_ITEMS.find(n => n.id === page)?.icon} {NAV_ITEMS.find(n => n.id === page)?.label}
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
          {renderPage()}
        </main>
      </div>
    </div>
  )
}
