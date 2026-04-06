import { useEffect, useState, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useLedgerSignalR } from '../hooks/useLedgerSignalR'
import { ledgerApi } from '../services/ledgerApi'

const ENTRY_TYPES = ['', 'INITIAL_FUNDING', 'REALIZED_PNL', 'COMMISSION', 'FUNDING_FEE', 'WITHDRAWAL']
const PAGE_SIZE   = 50

function fmt(n, digits = 4) {
  if (n === null || n === undefined) return '—'
  return Number(n).toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

export default function EntriesPage() {
  const { t } = useTranslation()
  const [environment, setEnvironment] = useState(import.meta.env.VITE_DEFAULT_ENVIRONMENT ?? 'TESTNET')
  const [accountId, setAccountId]     = useState(null)
  const [sessionId, setSessionId]     = useState(null)
  const [data, setData]               = useState(null)
  const [page, setPage]               = useState(1)
  const [filters, setFilters]         = useState({ symbol: '', type: '', fromDate: '', toDate: '' })
  const [loading, setLoading]         = useState(false)
  const [error, setError]             = useState(null)

  // Bootstrap to get account/session context
  useEffect(() => {
    setError(null)
    setData(null)
    setPage(1)
    setSessionId(null)
    setAccountId(null)

    ledgerApi.bootstrap(environment)
      .then((d) => {
        setAccountId(d.accountId)

        if (d.activeSession?.id) {
          setSessionId(d.activeSession.id)
          return
        }

        return ledgerApi.getSessions(d.accountId, 'ACTIVE').then((sessions) => {
          const active = sessions.find((s) => s.status === 'ACTIVE')
          setSessionId(active?.id ?? null)
        })
      })
      .catch((e) => setError(e.message))
  }, [environment])

  const load = useCallback(() => {
    if (!sessionId) return
    setLoading(true)
    setError(null)
    ledgerApi.getEntries(sessionId, { ...filters, page, pageSize: PAGE_SIZE })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false))
  }, [sessionId, filters, page])

  useEffect(() => { load() }, [load])

  useLedgerSignalR({ onEntry: () => load() })

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1

  return (
    <div className="space-y-4">
      <h2 className="text-xl font-semibold">{t('entries.title')}</h2>

      {/* Filters */}
      <div className="flex flex-wrap gap-3 bg-gray-800 p-3 rounded-lg border border-gray-700">
        <select
          value={environment}
          onChange={(e) => setEnvironment(e.target.value)}
          className="bg-gray-900 border border-gray-600 rounded px-2 py-1 text-sm text-gray-200"
        >
          <option value="TESTNET">TESTNET</option>
          <option value="MAINNET">MAINNET</option>
        </select>
        {(['symbol', 'fromDate', 'toDate']).map((f) => (
          <input
            key={f}
            type={f.includes('Date') ? 'date' : 'text'}
            placeholder={t(`entries.filter.${f}`)}
            value={filters[f]}
            onChange={(e) => { setFilters((p) => ({ ...p, [f]: e.target.value })); setPage(1) }}
            className="bg-gray-900 border border-gray-600 rounded px-2 py-1 text-sm text-gray-200 placeholder-gray-500"
          />
        ))}
        <select
          value={filters.type}
          onChange={(e) => { setFilters((p) => ({ ...p, type: e.target.value })); setPage(1) }}
          className="bg-gray-900 border border-gray-600 rounded px-2 py-1 text-sm text-gray-200"
        >
          {ENTRY_TYPES.map((tp) => (
            <option key={tp} value={tp}>{tp || t('entries.filter.allTypes')}</option>
          ))}
        </select>
      </div>

      <p className="text-xs text-gray-500">
        ENV: {environment} | ACCOUNT: {accountId ?? '—'} | SESSION: {sessionId ?? '—'}
      </p>

      {error && <p className="text-red-400 text-sm">{error}</p>}
      {loading && <p className="text-gray-400 text-sm">{t('loading')}</p>}
      {!loading && !error && !sessionId && (
        <p className="text-yellow-400 text-sm">No active session found for this environment.</p>
      )}

      {data && (
        <>
          <p className="text-xs text-gray-500">{t('entries.total')}: {data.total}</p>
          <div className="overflow-x-auto">
            <table className="w-full text-sm border border-gray-700 rounded-lg overflow-hidden">
              <thead className="bg-gray-800 text-gray-400 text-xs">
                <tr>
                  {['timestamp', 'type', 'symbol', 'amount', 'txId'].map((h) => (
                    <th key={h} className="px-3 py-2 text-left">{t(`entries.col.${h}`)}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.entries.map((e) => (
                  <tr key={e.id} className="border-t border-gray-800 hover:bg-gray-800/50">
                    <td className="px-3 py-2 text-gray-400 text-xs whitespace-nowrap">
                      {new Date(e.timestamp).toLocaleString()}
                    </td>
                    <td className="px-3 py-2">
                      <span className={`text-xs px-1.5 py-0.5 rounded ${typeColor(e.type)}`}>{e.type}</span>
                    </td>
                    <td className="px-3 py-2 font-mono text-blue-300">{e.symbol ?? '—'}</td>
                    <td className={`px-3 py-2 font-mono ${Number(e.amount) >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                      {Number(e.amount) >= 0 ? '+' : ''}{fmt(e.amount)}
                    </td>
                    <td className="px-3 py-2 text-gray-500 text-xs truncate max-w-32">{e.binanceTransactionId ?? '—'}</td>
                  </tr>
                ))}
                {data.entries.length === 0 && (
                  <tr><td colSpan={5} className="px-3 py-6 text-center text-gray-500">{t('entries.empty')}</td></tr>
                )}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          <div className="flex items-center gap-2 text-sm">
            <button
              disabled={page <= 1}
              onClick={() => setPage((p) => p - 1)}
              className="px-3 py-1 bg-gray-800 rounded disabled:opacity-40 hover:bg-gray-700"
            >
              {t('pagination.prev')}
            </button>
            <span className="text-gray-400">{page} / {totalPages}</span>
            <button
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
              className="px-3 py-1 bg-gray-800 rounded disabled:opacity-40 hover:bg-gray-700"
            >
              {t('pagination.next')}
            </button>
          </div>
        </>
      )}
    </div>
  )
}

function typeColor(type) {
  switch (type) {
    case 'REALIZED_PNL':    return 'bg-green-900 text-green-300'
    case 'COMMISSION':      return 'bg-yellow-900 text-yellow-300'
    case 'FUNDING_FEE':     return 'bg-orange-900 text-orange-300'
    case 'WITHDRAWAL':      return 'bg-red-900 text-red-300'
    case 'INITIAL_FUNDING': return 'bg-blue-900 text-blue-300'
    default:                return 'bg-gray-700 text-gray-300'
  }
}
