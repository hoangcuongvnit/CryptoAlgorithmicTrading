import { Routes, Route, NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import LedgerPage from './pages/LedgerPage'
import EntriesPage from './pages/EntriesPage'
import SessionsPage from './pages/SessionsPage'
import PnLBreakdownPage from './pages/PnLBreakdownPage'
import StableCoinsPage from './pages/StableCoinsPage'

const NAV_ITEMS = [
  { to: '/',          labelKey: 'nav.dashboard' },
  { to: '/entries',   labelKey: 'nav.entries'   },
  { to: '/sessions',  labelKey: 'nav.sessions'  },
  { to: '/pnl',       labelKey: 'nav.pnl'       },
  { to: '/stable-coins', labelKey: 'nav.stableCoins' },
]

export default function App() {
  const { t, i18n } = useTranslation()

  return (
    <div className="min-h-screen flex flex-col">
      {/* Top nav */}
      <header className="bg-gray-900 border-b border-gray-800 px-6 py-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-white">{t('appTitle')}</h1>
        <nav className="flex gap-4">
          {NAV_ITEMS.map(({ to, labelKey }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                isActive
                  ? 'text-blue-400 text-sm font-medium'
                  : 'text-gray-400 hover:text-white text-sm'
              }
            >
              {t(labelKey)}
            </NavLink>
          ))}
        </nav>
        <button
          className="text-xs text-gray-500 hover:text-gray-300"
          onClick={() => i18n.changeLanguage(i18n.language === 'vi' ? 'en' : 'vi')}
        >
          {i18n.language === 'vi' ? 'EN' : 'VI'}
        </button>
      </header>

      {/* Page content */}
      <main className="flex-1 p-6">
        <Routes>
          <Route path="/"         element={<LedgerPage />} />
          <Route path="/entries"  element={<EntriesPage />} />
          <Route path="/sessions" element={<SessionsPage />} />
          <Route path="/pnl"      element={<PnLBreakdownPage />} />
          <Route path="/stable-coins" element={<StableCoinsPage />} />
        </Routes>
      </main>
    </div>
  )
}
