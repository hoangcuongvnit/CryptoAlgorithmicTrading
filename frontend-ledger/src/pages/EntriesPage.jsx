import { useEffect, useState, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useLedgerSignalR } from '../hooks/useLedgerSignalR'
import { ledgerApi } from '../services/ledgerApi'

const ENTRY_TYPES = ['', 'INITIAL_FUNDING', 'REALIZED_PNL', 'COMMISSION', 'FUNDING_FEE', 'WITHDRAWAL']
const PAGE_SIZE   = 50

function fmt(n, digits = 4) {
  if (n === null || n === undefined) return '—'
  const v = Number(n)
  return v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

function fmtPct(n, digits = 2) {
  if (n === null || n === undefined) return '—'
  const v = Number(n)
  if (!Number.isFinite(v)) return '—'
  return `${v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`
}

export default function EntriesPage() {
  const { t }   = useTranslation()
  const [environment, setEnvironment] = useState(import.meta.env.VITE_DEFAULT_ENVIRONMENT ?? 'TESTNET')
  const [accountId, setAccountId]     = useState(null)
  const [sessionId, setSessionId] = useState(null)
  const [data, setData]           = useState(null)
  const [equityData, setEquityData] = useState(null)
  const [page, setPage]           = useState(1)
  const [filters, setFilters]     = useState({ symbol: '', type: '', fromDate: '', toDate: '' })
  const [loading, setLoading]     = useState(false)
  const [equityLoading, setEquityLoading] = useState(false)
  const [error, setError]         = useState(null)
  const [equityError, setEquityError] = useState(null)

  // Bootstrap to get account/session context
  useEffect(() => {
    setError(null)
    setData(null)
    setEquityData(null)
    setPage(1)
    setSessionId(null)
    setAccountId(null)
    setEquityError(null)

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

  const loadEquityTimeline = useCallback(() => {
    if (!sessionId) return
    setEquityLoading(true)
    setEquityError(null)
    ledgerApi.getSellEquityTimeline(sessionId)
      .then(setEquityData)
      .catch((e) => setEquityError(e.message))
      .finally(() => setEquityLoading(false))
  }, [sessionId])

  useEffect(() => { load() }, [load])
  useEffect(() => { loadEquityTimeline() }, [loadEquityTimeline])

  // Reload on new real-time entry
  useLedgerSignalR({
    onEntry: (entry) => {
      load()
      if (entry?.type === 'REALIZED_PNL') {
        loadEquityTimeline()
      }
    },
  })

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1
  const timelinePoints = equityData?.points ?? []
  const equitySummary = equityData?.summary ?? null
  const latestPoint = timelinePoints.length > 0 ? timelinePoints[timelinePoints.length - 1] : null

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

      {sessionId && (
        <div className="space-y-3 bg-gray-800 p-4 rounded-lg border border-gray-700">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h3 className="text-sm font-semibold text-gray-100">{t('entries.equity.title')}</h3>
              <p className="text-xs text-gray-400">{t('entries.equity.subtitle')}</p>
            </div>
            <p className="text-xs text-gray-400">
              {t('entries.equity.totalPoints')}: {timelinePoints.length}
            </p>
          </div>

          {equityError && <p className="text-red-400 text-sm">{equityError}</p>}
          {equityLoading && <p className="text-gray-400 text-sm">{t('entries.equity.loading')}</p>}

          {!equityLoading && !equityError && timelinePoints.length === 0 && (
            <p className="text-yellow-400 text-sm">{t('entries.equity.empty')}</p>
          )}

          {timelinePoints.length > 0 && (
            <>
              {equitySummary && (
                <div className="grid grid-cols-2 md:grid-cols-4 gap-2 text-xs">
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.firstEquity')}</p>
                    <p className="text-gray-100 font-mono">{fmt(equitySummary.firstEquity)}</p>
                  </div>
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.latestEquity')}</p>
                    <p className="text-gray-100 font-mono">{fmt(equitySummary.latestEquity)}</p>
                  </div>
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.deltaValue')}</p>
                    <p className={`font-mono ${Number(equitySummary.deltaValue) >= 0 ? 'text-green-300' : 'text-red-300'}`}>
                      {Number(equitySummary.deltaValue) >= 0 ? '+' : ''}{fmt(equitySummary.deltaValue)}
                    </p>
                  </div>
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.deltaPercent')}</p>
                    <p className={`font-mono ${Number(equitySummary.deltaPercent) >= 0 ? 'text-green-300' : 'text-red-300'}`}>
                      {Number(equitySummary.deltaPercent) >= 0 ? '+' : ''}{fmtPct(equitySummary.deltaPercent)}
                    </p>
                  </div>
                </div>
              )}

              <EquitySellTimelineChart
                points={timelinePoints}
                labels={{
                  txId: t('entries.equity.tooltip.txId'),
                  symbol: t('entries.equity.tooltip.symbol'),
                  soldAt: t('entries.equity.tooltip.soldAt'),
                  holdings: t('entries.equity.tooltip.holdings'),
                  noHoldings: t('entries.equity.tooltip.noHoldings'),
                  totalEquity: t('entries.equity.totalEquity'),
                  currentBalance: t('entries.equity.currentBalance'),
                  holdingsValue: t('entries.equity.holdingsValue'),
                }}
              />

              {latestPoint && (
                <div className="grid grid-cols-2 md:grid-cols-3 gap-2 text-xs">
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.currentBalance')}</p>
                    <p className="text-blue-300 font-mono">{fmt(latestPoint.currentBalance)}</p>
                  </div>
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.holdingsValue')}</p>
                    <p className="text-cyan-300 font-mono">{fmt(latestPoint.holdingsMarketValue)}</p>
                  </div>
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.totalEquity')}</p>
                    <p className="text-green-300 font-mono">{fmt(latestPoint.totalEquity)}</p>
                  </div>
                  <div className="bg-gray-900 rounded p-2 border border-gray-700">
                    <p className="text-gray-500">{t('entries.equity.lastSellAt')}</p>
                    <p className="text-gray-200">{new Date(latestPoint.snapshotTime).toLocaleString()}</p>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
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

function EquitySellTimelineChart({ points, labels }) {
  const [hoveredPoint, setHoveredPoint] = useState(null)
  const width = 900
  const height = 240
  const pad = { top: 16, right: 24, bottom: 28, left: 64 }
  const chartW = width - pad.left - pad.right
  const chartH = height - pad.top - pad.bottom

  const ms = points.map((p) => new Date(p.snapshotTime).getTime())
  const values = points.map((p) => Number(p.totalEquity))

  const minX = Math.min(...ms)
  const maxX = Math.max(...ms)
  const xRange = Math.max(maxX - minX, 1)

  const minV = Math.min(...values)
  const maxV = Math.max(...values)
  const span = Math.max(maxV - minV, 1)
  const padV = span * 0.15
  const yMin = minV - padV
  const yMax = maxV + padV
  const yRange = Math.max(yMax - yMin, 1)

  const xAt = (timeMs) => {
    if (points.length === 1) return pad.left + chartW / 2
    return pad.left + ((timeMs - minX) / xRange) * chartW
  }

  const yAt = (value) => pad.top + (1 - ((value - yMin) / yRange)) * chartH

  const chartPoints = points.map((p) => {
    const timeMs = new Date(p.snapshotTime).getTime()
    return {
      ...p,
      x: xAt(timeMs),
      y: yAt(Number(p.totalEquity)),
    }
  })

  const path = chartPoints.map((p, idx) => `${idx === 0 ? 'M' : 'L'} ${p.x.toFixed(2)} ${p.y.toFixed(2)}`).join(' ')
  const yTicks = 4

  const tooltipWidth = 320
  const tooltipBaseHeight = 126
  const tooltipLineHeight = 18
  const tooltipExtra = hoveredPoint?.holdings?.length ? Math.min(5, hoveredPoint.holdings.length) * tooltipLineHeight : 0
  const tooltipHeight = tooltipBaseHeight + tooltipExtra

  const tooltipX = hoveredPoint
    ? Math.min(Math.max(hoveredPoint.x + 12, pad.left), width - pad.right - tooltipWidth)
    : 0
  const tooltipY = hoveredPoint
    ? Math.min(Math.max(hoveredPoint.y - 12, pad.top), height - pad.bottom - tooltipHeight)
    : 0

  return (
    <div className="w-full rounded-lg border border-gray-700 bg-gray-900/50 p-2">
      <svg viewBox={`0 0 ${width} ${height}`} className="w-full h-auto">
        {Array.from({ length: yTicks + 1 }, (_, i) => {
          const ratio = i / yTicks
          const y = pad.top + ratio * chartH
          const value = yMax - ratio * yRange
          return (
            <g key={i}>
              <line x1={pad.left} y1={y} x2={width - pad.right} y2={y} stroke="#374151" strokeDasharray="3 3" />
              <text x={pad.left - 8} y={y + 4} textAnchor="end" fontSize="10" fill="#9ca3af">
                {fmt(value, 2)}
              </text>
            </g>
          )
        })}

        <line x1={pad.left} y1={height - pad.bottom} x2={width - pad.right} y2={height - pad.bottom} stroke="#4b5563" />
        <line x1={pad.left} y1={pad.top} x2={pad.left} y2={height - pad.bottom} stroke="#4b5563" />

        <path d={path} fill="none" stroke="#22d3ee" strokeWidth="2.5" />

        {chartPoints.map((p, idx) => (
          <g
            key={p.triggerTransactionId}
            onMouseEnter={() => setHoveredPoint(p)}
            onMouseLeave={() => setHoveredPoint(null)}
          >
            <circle cx={p.x} cy={p.y} r={hoveredPoint?.triggerTransactionId === p.triggerTransactionId ? '5' : '3.5'} fill="#34d399" />
            <title>{`SELL #${idx + 1}`}</title>
          </g>
        ))}

        {hoveredPoint && (
          <foreignObject x={tooltipX} y={tooltipY} width={tooltipWidth} height={tooltipHeight}>
            <div xmlns="http://www.w3.org/1999/xhtml" style={{
              background: 'rgba(17,24,39,0.96)',
              border: '1px solid #374151',
              borderRadius: '8px',
              padding: '10px',
              color: '#e5e7eb',
              fontSize: '11px',
              lineHeight: 1.35,
              boxShadow: '0 10px 25px rgba(0,0,0,0.35)',
            }}>
              <div style={{ color: '#34d399', fontWeight: 600, marginBottom: '4px' }}>
                {labels.totalEquity}: {fmt(hoveredPoint.totalEquity, 4)}
              </div>
              <div>{labels.currentBalance}: {fmt(hoveredPoint.currentBalance, 4)}</div>
              <div>{labels.holdingsValue}: {fmt(hoveredPoint.holdingsMarketValue, 4)}</div>
              <div>{labels.soldAt}: {new Date(hoveredPoint.snapshotTime).toLocaleString()}</div>
              <div>{labels.symbol}: {hoveredPoint.triggerSymbol ?? '—'}</div>
              <div style={{ wordBreak: 'break-all' }}>{labels.txId}: {hoveredPoint.triggerTransactionId}</div>
              <div style={{ marginTop: '6px', color: '#9ca3af' }}>{labels.holdings}:</div>
              {hoveredPoint.holdings?.length > 0 ? (
                hoveredPoint.holdings.slice(0, 5).map((h) => (
                  <div key={`${hoveredPoint.triggerTransactionId}-${h.symbol}`} style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>
                    {h.symbol}: {fmt(h.quantity, 6)} x {fmt(h.markPrice, 4)} = {fmt(h.marketValue, 4)}
                  </div>
                ))
              ) : (
                <div style={{ color: '#9ca3af' }}>{labels.noHoldings}</div>
              )}
              {hoveredPoint.holdings?.length > 5 && (
                <div style={{ color: '#9ca3af' }}>+{hoveredPoint.holdings.length - 5} more...</div>
              )}
            </div>
          </foreignObject>
        )}
      </svg>
    </div>
  )
}

function typeColor(type) {
  switch (type) {
    case 'REALIZED_PNL':   return 'bg-green-900 text-green-300'
    case 'COMMISSION':     return 'bg-yellow-900 text-yellow-300'
    case 'FUNDING_FEE':    return 'bg-orange-900 text-orange-300'
    case 'WITHDRAWAL':     return 'bg-red-900 text-red-300'
    case 'INITIAL_FUNDING': return 'bg-blue-900 text-blue-300'
    default:               return 'bg-gray-700 text-gray-300'
  }
}
