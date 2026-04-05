import { useEffect, useState, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useLedgerSignalR } from '../hooks/useLedgerSignalR'
import { ledgerApi } from '../services/ledgerApi'

function StatCard({ label, value, colorClass = 'text-white' }) {
  return (
    <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
      <p className="text-xs text-gray-400 mb-1">{label}</p>
      <p className={`text-2xl font-bold font-mono ${colorClass}`}>{value}</p>
    </div>
  )
}

function fmt(n, digits = 2) {
  if (n === null || n === undefined) return '—'
  return Number(n).toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

export default function LedgerPage() {
  const { t } = useTranslation()
  const [account, setAccount]   = useState(null)
  const [equity, setEquity]     = useState(null)
  const [accountId, setAccountId] = useState(null)
  const [loading, setLoading]   = useState(true)
  const [error, setError]       = useState(null)

  // Bootstrap account on mount
  useEffect(() => {
    const env = import.meta.env.VITE_DEFAULT_ENVIRONMENT ?? 'TESTNET'
    ledgerApi.bootstrap(env)
      .then((data) => {
        setAccountId(data.accountId)
        return ledgerApi.getAccount(data.accountId)
      })
      .then(setAccount)
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  const handleEquity = useCallback((data) => setEquity(data), [])
  const handleBalance = useCallback((data) => {
    setAccount((prev) => prev ? { ...prev, currentBalance: data.balance } : prev)
  }, [])

  const { isConnected } = useLedgerSignalR({ onEquity: handleEquity, onBalance: handleBalance })

  if (loading) return <p className="text-gray-400">{t('loading')}</p>
  if (error)   return <p className="text-red-400">{t('error')}: {error}</p>
  if (!account) return <p className="text-gray-400">{t('noSession')}</p>

  const netPnl = account.netPnl ?? 0
  const roe    = account.roePercent ?? 0
  const unrealized = equity?.unrealizedPnl ?? 0
  const realTimeEquity = equity?.realTimeEquity ?? account.currentBalance

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">{t('dashboard.title')}</h2>
          <p className="text-xs text-gray-500 mt-0.5">{t('dashboard.session')}: {account.id}</p>
        </div>
        <span className={`text-xs px-2 py-1 rounded-full ${isConnected ? 'bg-green-900 text-green-300' : 'bg-red-900 text-red-300'}`}>
          {isConnected ? t('status.live') : t('status.offline')}
        </span>
      </div>

      {/* Stats grid */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-4">
        <StatCard label={t('dashboard.initialBalance')}  value={`$${fmt(account.initialBalance)}`} />
        <StatCard label={t('dashboard.currentBalance')}  value={`$${fmt(account.currentBalance)}`} />
        <StatCard
          label={t('dashboard.netPnl')}
          value={`${netPnl >= 0 ? '+' : ''}$${fmt(netPnl)}`}
          colorClass={netPnl >= 0 ? 'text-green-400' : 'text-red-400'}
        />
        <StatCard
          label={t('dashboard.unrealizedPnl')}
          value={`${unrealized >= 0 ? '+' : ''}$${fmt(unrealized)}`}
          colorClass={unrealized >= 0 ? 'text-green-400' : 'text-red-400'}
        />
        <StatCard label={t('dashboard.realTimeEquity')} value={`$${fmt(realTimeEquity)}`} colorClass="text-blue-300" />
      </div>

      {/* ROE */}
      <div className="bg-gray-800 rounded-lg p-4 border border-gray-700 inline-block">
        <p className="text-xs text-gray-400 mb-1">{t('dashboard.roe')}</p>
        <p className={`text-3xl font-bold font-mono ${roe >= 0 ? 'text-green-400' : 'text-red-400'}`}>
          {roe >= 0 ? '+' : ''}{fmt(roe)}%
        </p>
      </div>

      {/* Open positions from equity update */}
      {equity?.positions?.length > 0 && (
        <div>
          <h3 className="text-sm font-medium text-gray-300 mb-2">{t('dashboard.openPositions')}</h3>
          <div className="overflow-x-auto">
            <table className="w-full text-sm border border-gray-700 rounded-lg overflow-hidden">
              <thead className="bg-gray-800 text-gray-400 text-xs">
                <tr>
                  {['symbol', 'quantity', 'entryPrice', 'markPrice', 'unrealizedPnl'].map((h) => (
                    <th key={h} className="px-3 py-2 text-left">{t(`positions.${h}`)}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {equity.positions.map((p, i) => (
                  <tr key={i} className="border-t border-gray-800 hover:bg-gray-800/50">
                    <td className="px-3 py-2 font-mono text-blue-300">{p.symbol}</td>
                    <td className="px-3 py-2 font-mono">{p.quantity}</td>
                    <td className="px-3 py-2 font-mono">{fmt(p.entryPrice, 4)}</td>
                    <td className="px-3 py-2 font-mono">{fmt(p.markPrice, 4)}</td>
                    <td className={`px-3 py-2 font-mono ${p.unrealizedPnl >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                      {p.unrealizedPnl >= 0 ? '+' : ''}{fmt(p.unrealizedPnl)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
