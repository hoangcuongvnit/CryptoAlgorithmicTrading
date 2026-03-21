import { useState } from 'react'
import { OverviewPage } from './pages/OverviewPage.jsx'
import { TradingPage } from './pages/TradingPage.jsx'
import { SafetyPage } from './pages/SafetyPage.jsx'
import { EventsPage } from './pages/EventsPage.jsx'

const NAV_ITEMS = [
  { id: 'overview', label: 'Overview', icon: '📊' },
  { id: 'trading', label: 'Trading Signals', icon: '💹' },
  { id: 'safety', label: 'Safety & Risk', icon: '🛡️' },
  { id: 'events', label: 'Event History', icon: '📋' },
]

export default function App() {
  const [page, setPage] = useState('overview')
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const renderPage = () => {
    switch (page) {
      case 'overview': return <OverviewPage />
      case 'trading': return <TradingPage />
      case 'safety': return <SafetyPage />
      case 'events': return <EventsPage />
      default: return <OverviewPage />
    }
  }

  return (
    <div className="min-h-screen bg-gray-50 flex">
      {/* Mobile overlay */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 bg-black bg-opacity-30 z-20 lg:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside className={`
        fixed top-0 left-0 h-full w-64 bg-gray-900 text-white flex flex-col z-30
        transform transition-transform duration-200 ease-in-out
        ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
        lg:translate-x-0 lg:static lg:flex
      `}>
        {/* Brand */}
        <div className="px-6 py-5 border-b border-gray-700">
          <h1 className="text-lg font-bold text-white">🤖 CryptoTrader</h1>
          <p className="text-xs text-gray-400 mt-0.5">Automated Trading Dashboard</p>
        </div>

        {/* Nav */}
        <nav className="flex-1 px-3 py-4 space-y-1">
          {NAV_ITEMS.map(item => (
            <button
              key={item.id}
              onClick={() => { setPage(item.id); setSidebarOpen(false) }}
              className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-colors ${
                page === item.id
                  ? 'bg-blue-600 text-white'
                  : 'text-gray-300 hover:bg-gray-800 hover:text-white'
              }`}
            >
              <span className="text-base">{item.icon}</span>
              {item.label}
            </button>
          ))}
        </nav>

        {/* Footer */}
        <div className="px-6 py-4 border-t border-gray-700">
          <p className="text-xs text-gray-500">Data refreshes automatically</p>
          <p className="text-xs text-gray-600 mt-0.5">Every 15–30 seconds</p>
        </div>
      </aside>

      {/* Main content */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Mobile header */}
        <header className="lg:hidden bg-white border-b border-gray-200 px-4 py-3 flex items-center gap-3 sticky top-0 z-10">
          <button
            onClick={() => setSidebarOpen(true)}
            className="p-2 rounded-lg text-gray-600 hover:bg-gray-100"
          >
            ☰
          </button>
          <h1 className="font-semibold text-gray-800">
            {NAV_ITEMS.find(n => n.id === page)?.icon} {NAV_ITEMS.find(n => n.id === page)?.label}
          </h1>
        </header>

        {/* Page content */}
        <main className="flex-1 p-4 lg:p-6 max-w-7xl mx-auto w-full">
          {renderPage()}
        </main>
      </div>
    </div>
  )
}
