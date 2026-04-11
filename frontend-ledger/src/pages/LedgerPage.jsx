import { useEffect, useState, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useLedgerSignalR } from '../hooks/useLedgerSignalR'
import { ledgerApi } from '../services/ledgerApi'

const BINANCE_POLL_MS = 30_000

// ─── constants ────────────────────────────────────────────────────────────────

const EQUITY_RELOAD_TYPES = new Set(['REALIZED_PNL', 'COMMISSION', 'INITIAL_FUNDING', 'BUY_CASH_OUT', 'SELL_CASH_IN'])

const EVENT_COLOR = {
  SESSION_START: '#60a5fa',  // blue-400
  BUY:           '#34d399',  // green-400
  SELL:          '#f97316',  // orange-400
}

// ─── helpers ──────────────────────────────────────────────────────────────────

function fmt(n, digits = 2) {
  if (n === null || n === undefined) return '—'
  return Number(n).toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

function fmtPct(n, digits = 2) {
  if (n === null || n === undefined) return '—'
  const v = Number(n)
  if (!Number.isFinite(v)) return '—'
  return `${v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`
}

// ─── sub-components ───────────────────────────────────────────────────────────

function StatCard({ label, value, colorClass = 'text-white', formula }) {
  const [open, setOpen] = useState(false)

  return (
    <div
      className={`bg-gray-800 rounded-lg p-4 border border-gray-700 transition-colors ${formula ? 'cursor-pointer hover:border-gray-500' : ''}`}
      onClick={() => formula && setOpen((s) => !s)}
    >
      <div className="flex items-center justify-between mb-1">
        <p className="text-xs text-gray-400">{label}</p>
        {formula && (
          <span className={`text-sm leading-none transition-colors ${open ? 'text-blue-400' : 'text-gray-600 hover:text-gray-400'}`}>
            ⓘ
          </span>
        )}
      </div>
      <p className={`text-2xl font-bold font-mono ${colorClass}`}>{value}</p>
      {open && formula && (
        <div className="mt-3 pt-3 border-t border-gray-700 text-xs text-gray-300 font-mono whitespace-pre-wrap leading-relaxed">
          {formula}
        </div>
      )}
    </div>
  )
}

function RoeCard({ roe, label, formula }) {
  const [open, setOpen] = useState(false)

  return (
    <div
      className={`bg-gray-800 rounded-lg p-4 border border-gray-700 xl:min-w-[220px] transition-colors ${formula ? 'cursor-pointer hover:border-gray-500' : ''}`}
      onClick={() => formula && setOpen((s) => !s)}
    >
      <div className="flex items-center justify-between mb-1">
        <p className="text-xs text-gray-400">{label}</p>
        {formula && (
          <span className={`text-sm leading-none transition-colors ${open ? 'text-blue-400' : 'text-gray-600 hover:text-gray-400'}`}>
            ⓘ
          </span>
        )}
      </div>
      <p className={`text-3xl font-bold font-mono ${roe >= 0 ? 'text-green-400' : 'text-red-400'}`}>
        {roe >= 0 ? '+' : ''}{fmt(roe)}%
      </p>
      {open && formula && (
        <div className="mt-3 pt-3 border-t border-gray-700 text-xs text-gray-300 font-mono whitespace-pre-wrap leading-relaxed">
          {formula}
        </div>
      )}
    </div>
  )
}

// ─── main page ────────────────────────────────────────────────────────────────

export default function LedgerPage() {
  const { t } = useTranslation()

  const [account, setAccount]             = useState(null)
  const [equity, setEquity]               = useState(null)
  const [equityTimeline, setEquityTimeline] = useState(null)
  const [timelineLoading, setTimelineLoading] = useState(false)
  const [loading, setLoading]             = useState(true)
  const [error, setError]                 = useState(null)
  const [binanceAccount, setBinanceAccount] = useState(null)
  const [binanceLoading, setBinanceLoading] = useState(false)

  // Bootstrap account on mount — auto-detect active environment so we always
  // follow whichever account (TESTNET or MAINNET) is currently receiving trades.
  useEffect(() => {
    ledgerApi.getActiveEnvironment()
      .then(({ environment }) => environment)
      .catch(() => import.meta.env.VITE_DEFAULT_ENVIRONMENT ?? 'TESTNET')
      .then((env) => ledgerApi.bootstrap(env))
      .then((data) => ledgerApi.getAccount(data.accountId))
      .then(setAccount)
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  // Load equity timeline — depends on account.id (sessionId)
  const loadEquityTimeline = useCallback(() => {
    if (!account?.id) return
    setTimelineLoading(true)
    ledgerApi.getEquityTimeline(account.id)
      .then(setEquityTimeline)
      .catch(() => {}) // non-critical
      .finally(() => setTimelineLoading(false))
  }, [account?.id])

  useEffect(() => { loadEquityTimeline() }, [loadEquityTimeline])

  const fetchBinanceAccount = useCallback(() => {
    setBinanceLoading(true)
    ledgerApi.getBinanceAccount()
      .then(setBinanceAccount)
      .catch(() => {}) // non-critical
      .finally(() => setBinanceLoading(false))
  }, [])

  useEffect(() => {
    fetchBinanceAccount()
    const id = setInterval(fetchBinanceAccount, BINANCE_POLL_MS)
    return () => clearInterval(id)
  }, [fetchBinanceAccount])

  const handleEquity  = useCallback((data) => setEquity(data), [])
  const handleBalance = useCallback((data) => {
    // Chỉ cập nhật balance nếu sessionId khớp với session đang xem,
    // tránh cross-contamination khi Executor chạy MAINNET nhưng UI đang xem TESTNET account.
    setAccount((prev) => {
      if (!prev) return prev
      if (data.sessionId && prev.id && data.sessionId !== prev.id) return prev
      return { ...prev, currentBalance: data.balance }
    })
  }, [])
  const handleEntry = useCallback((entry) => {
    if (EQUITY_RELOAD_TYPES.has(entry?.type)) loadEquityTimeline()
  }, [loadEquityTimeline])

  const { isConnected } = useLedgerSignalR({
    onEquity: handleEquity,
    onBalance: handleBalance,
    onEntry: handleEntry,
  })

  if (loading) return <p className="text-gray-400">{t('loading')}</p>
  if (error)   return <p className="text-red-400">{t('error')}: {error}</p>
  if (!account) return <p className="text-gray-400">{t('noSession')}</p>

  const netPnl         = account.netPnl ?? 0
  const roe            = account.roePercent ?? 0
  const unrealized     = equity?.unrealizedPnl ?? 0
  const realTimeEquity = equity?.realTimeEquity ?? account.currentBalance
  const openPositions  = equity?.positions ?? []
  const totalHoldingMarketValue = openPositions.reduce((sum, p) => {
    const qty = Number(p?.quantity ?? 0)
    const mark = Number(p?.markPrice ?? 0)
    if (!Number.isFinite(qty) || !Number.isFinite(mark)) return sum
    return sum + (qty * mark)
  }, 0)

  const ct = t('dashboard.equityChart', { returnObjects: true })
  const timelinePoints = equityTimeline?.points ?? []
  const equitySummary  = equityTimeline?.summary ?? null
  const latestPoint    = timelinePoints.length > 0 ? timelinePoints[timelinePoints.length - 1] : null

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

      {/* Binance Spot Account */}
      <BinanceAccountWidget data={binanceAccount} loading={binanceLoading} onRefresh={fetchBinanceAccount} t={t} />

      {/* Stats + ROE row */}
      <div className="flex flex-col xl:flex-row gap-4 xl:items-stretch">
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-4 flex-1">
          <StatCard
            label={t('dashboard.initialBalance')}
            value={`$${fmt(account.initialBalance)}`}
            formula={t('dashboard.formulas.initialBalance')}
          />
          <StatCard
            label={t('dashboard.currentBalance')}
            value={`$${fmt(account.currentBalance)}`}
            formula={t('dashboard.formulas.currentBalance')}
          />
          <StatCard
            label={t('dashboard.netPnl')}
            value={`${netPnl >= 0 ? '+' : ''}$${fmt(netPnl)}`}
            colorClass={netPnl >= 0 ? 'text-green-400' : 'text-red-400'}
            formula={t('dashboard.formulas.netPnl')}
          />
          <StatCard
            label={t('dashboard.unrealizedPnl')}
            value={`${unrealized >= 0 ? '+' : ''}$${fmt(unrealized)}`}
            colorClass={unrealized >= 0 ? 'text-green-400' : 'text-red-400'}
            formula={t('dashboard.formulas.unrealizedPnl')}
          />
          <StatCard
            label={t('dashboard.realTimeEquity')}
            value={`$${fmt(realTimeEquity)}`}
            colorClass="text-blue-300"
            formula={t('dashboard.formulas.realTimeEquity')}
          />
        </div>
        <RoeCard roe={roe} formula={t('dashboard.formulas.roe')} label={t('dashboard.roe')} />
      </div>

      {/* Equity timeline chart */}
      <div className="space-y-3 bg-gray-800 p-4 rounded-lg border border-gray-700">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h3 className="text-sm font-semibold text-gray-100">{ct.title}</h3>
            <p className="text-xs text-gray-400">{ct.subtitle}</p>
          </div>
          <p className="text-xs text-gray-400">{ct.totalPoints}: {timelinePoints.length}</p>
        </div>

        {timelineLoading && <p className="text-gray-400 text-sm">{ct.loading}</p>}

        {!timelineLoading && timelinePoints.length === 0 && (
          <p className="text-yellow-400 text-sm">{ct.empty}</p>
        )}

        {timelinePoints.length > 0 && (
          <>
            {equitySummary && (
              <div className="grid grid-cols-2 md:grid-cols-4 gap-2 text-xs">
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.firstEquity}</p>
                  <p className="text-gray-100 font-mono">{fmt(equitySummary.firstEquity, 4)}</p>
                </div>
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.latestEquity}</p>
                  <p className="text-gray-100 font-mono">{fmt(equitySummary.latestEquity, 4)}</p>
                </div>
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.deltaValue}</p>
                  <p className={`font-mono ${Number(equitySummary.deltaValue) >= 0 ? 'text-green-300' : 'text-red-300'}`}>
                    {Number(equitySummary.deltaValue) >= 0 ? '+' : ''}{fmt(equitySummary.deltaValue, 4)}
                  </p>
                </div>
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.deltaPercent}</p>
                  <p className={`font-mono ${Number(equitySummary.deltaPercent) >= 0 ? 'text-green-300' : 'text-red-300'}`}>
                    {Number(equitySummary.deltaPercent) >= 0 ? '+' : ''}{fmtPct(equitySummary.deltaPercent)}
                  </p>
                </div>
              </div>
            )}

            <EquityTimelineChart points={timelinePoints} labels={ct} />

            {latestPoint && (
              <div className="grid grid-cols-2 md:grid-cols-4 gap-2 text-xs">
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.currentBalance}</p>
                  <p className="text-blue-300 font-mono">{fmt(latestPoint.currentBalance, 4)}</p>
                </div>
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.holdingsValue}</p>
                  <p className="text-cyan-300 font-mono">{fmt(latestPoint.holdingsMarketValue, 4)}</p>
                </div>
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.totalEquity}</p>
                  <p className="text-green-300 font-mono">{fmt(latestPoint.totalEquity, 4)}</p>
                </div>
                <div className="bg-gray-900 rounded p-2 border border-gray-700">
                  <p className="text-gray-500">{ct.lastEventAt}</p>
                  <p className="text-gray-200">{new Date(latestPoint.snapshotTime).toLocaleString()}</p>
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {/* Open positions */}
      {openPositions.length > 0 && (
        <div>
          <h3 className="text-sm font-medium text-gray-300 mb-2">{t('dashboard.openPositions')}</h3>
          <div className="overflow-x-auto">
            <table className="w-full text-sm border border-gray-700 rounded-lg overflow-hidden">
              <thead className="bg-gray-800 text-gray-400 text-xs">
                <tr>
                  {['symbol', 'quantity', 'entryPrice', 'markPrice', 'marketValue', 'unrealizedPnl'].map((h) => (
                    <th key={h} className="px-3 py-2 text-left">{t(`positions.${h}`)}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {openPositions.map((p, i) => {
                  const marketValue = Number(p.quantity ?? 0) * Number(p.markPrice ?? 0)
                  return (
                  <tr key={i} className="border-t border-gray-800 hover:bg-gray-800/50">
                    <td className="px-3 py-2 font-mono text-blue-300">{p.symbol}</td>
                    <td className="px-3 py-2 font-mono">{p.quantity}</td>
                    <td className="px-3 py-2 font-mono">{fmt(p.entryPrice, 4)}</td>
                    <td className="px-3 py-2 font-mono">{fmt(p.markPrice, 4)}</td>
                    <td className="px-3 py-2 font-mono text-cyan-300">${fmt(marketValue, 4)}</td>
                    <td className={`px-3 py-2 font-mono ${p.unrealizedPnl >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                      {p.unrealizedPnl >= 0 ? '+' : ''}{fmt(p.unrealizedPnl)}
                    </td>
                  </tr>
                )})}
              </tbody>
            </table>
          </div>
          <p className="text-xs text-gray-400 mt-2 text-right">
            {t('positions.totalMarketValue')}: <span className="font-mono text-cyan-300">${fmt(totalHoldingMarketValue, 4)}</span>
          </p>
        </div>
      )}
    </div>
  )
}

// ─── BinanceAccountWidget ─────────────────────────────────────────────────────

function BinanceAccountWidget({ data, loading, onRefresh, t }) {
  const bt = t('binanceAccount', { returnObjects: true })
  const usdtFromStable = data?.stableCoinBalances?.find((item) => item.asset === 'USDT')
  const usdtFree = usdtFromStable?.free ?? data?.usdtFree ?? 0
  const usdtLocked = usdtFromStable?.locked ?? data?.usdtLocked ?? 0
  const usdtTotal = usdtFromStable?.total ?? data?.usdtTotal ?? 0

  return (
    <div className="bg-gray-800 rounded-lg p-4 border border-gray-700 space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-sm font-semibold text-gray-100">{bt.title}</h3>
          <p className="text-xs text-gray-400">{bt.subtitle}</p>
        </div>
        <div className="flex items-center gap-3">
          {data && (
            <span className={`text-xs px-2 py-0.5 rounded-full ${data.isTestnet ? 'bg-yellow-900 text-yellow-300' : 'bg-green-900 text-green-300'}`}>
              {data.isTestnet ? 'TESTNET' : 'MAINNET'}
            </span>
          )}
          <button
            onClick={onRefresh}
            disabled={loading}
            className="text-xs px-2 py-1 rounded bg-gray-700 hover:bg-gray-600 text-gray-300 disabled:opacity-50"
          >
            {loading ? bt.refreshing : bt.refresh}
          </button>
        </div>
      </div>

      {!data && !loading && (
        <p className="text-yellow-400 text-sm">{bt.unavailable}</p>
      )}

      {loading && !data && (
        <p className="text-gray-400 text-sm">{bt.loading}</p>
      )}

      {data?.unavailable && (
        <p className="text-yellow-400 text-sm">{data.detail ?? bt.unavailable}</p>
      )}

      {data && !data.unavailable && (
        <>
          {/* Summary row */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div className="bg-gray-900 rounded p-3 border border-gray-700">
              <p className="text-xs text-gray-400">{bt.usdtFree}</p>
              <p className="text-xl font-bold font-mono text-cyan-300">${fmt(usdtFree)}</p>
            </div>
            <div className="bg-gray-900 rounded p-3 border border-gray-700">
              <p className="text-xs text-gray-400">{bt.usdtLocked}</p>
              <p className="text-xl font-bold font-mono text-yellow-300">${fmt(usdtLocked)}</p>
            </div>
            <div className="bg-gray-900 rounded p-3 border border-gray-700">
              <p className="text-xs text-gray-400">{bt.usdtTotal}</p>
              <p className="text-xl font-bold font-mono text-blue-300">${fmt(usdtTotal)}</p>
            </div>
          </div>
          <p className="text-xs text-gray-600 text-right">
            {bt.asOf}: {new Date(data.asOfUtc).toLocaleString()}
          </p>
        </>
      )}
    </div>
  )
}

// ─── chart ────────────────────────────────────────────────────────────────────

function eventColor(eventType) {
  return EVENT_COLOR[eventType] ?? EVENT_COLOR.SELL
}

function PortfolioAllocationChart({ slices, total, labels }) {
  const size = 260
  const cx = size / 2
  const cy = size / 2
  const radius = 92
  const stroke = 42

  let cumulative = 0
  const arcs = slices.map((slice) => {
    const start = cumulative
    cumulative += slice.percent
    return { ...slice, start, end: cumulative }
  })

  const polar = (angleDeg, r) => {
    const rad = ((angleDeg - 90) * Math.PI) / 180
    return { x: cx + r * Math.cos(rad), y: cy + r * Math.sin(rad) }
  }

  const describeArc = (startPct, endPct) => {
    const startAngle = (startPct / 100) * 360
    const endAngle = (endPct / 100) * 360
    const start = polar(endAngle, radius)
    const end = polar(startAngle, radius)
    const arcFlag = endAngle - startAngle > 180 ? 1 : 0
    return `M ${start.x} ${start.y} A ${radius} ${radius} 0 ${arcFlag} 0 ${end.x} ${end.y}`
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4 items-center">
      <div className="flex justify-center">
        <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
          <circle cx={cx} cy={cy} r={radius} fill="none" stroke="#1f2937" strokeWidth={stroke} />
          {arcs.map((slice) => (
            <path
              key={slice.key}
              d={describeArc(slice.start, slice.end)}
              fill="none"
              stroke={slice.color}
              strokeWidth={stroke}
              strokeLinecap="butt"
            />
          ))}
          <text x={cx} y={cy - 6} textAnchor="middle" fill="#9ca3af" fontSize="11">{labels.portfolioValue}</text>
          <text x={cx} y={cy + 16} textAnchor="middle" fill="#e5e7eb" fontSize="15" fontWeight="700">
            ${fmt(total, 2)}
          </text>
        </svg>
      </div>

      <div className="space-y-2 max-h-[250px] overflow-auto pr-1">
        {slices.map((slice) => (
          <div key={slice.key} className="flex items-center justify-between bg-gray-900 border border-gray-700 rounded px-2 py-1.5">
            <div className="flex items-center gap-2 min-w-0">
              <span className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: slice.color }} />
              <span className="text-xs text-gray-200 truncate">{slice.key === 'CASH' ? labels.cash : slice.label}</span>
            </div>
            <div className="text-right leading-tight">
              <div className="text-[11px] text-gray-400">{fmtPct(slice.percent, 2)}</div>
              <div className="text-xs font-mono text-gray-100">{fmt(slice.value, 4)}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function EventMarker({ cx, cy, eventType, size = 5, hovered = false }) {
  const s = hovered ? size * 1.4 : size
  const color = eventColor(eventType)

  if (eventType === 'SESSION_START') {
    // Diamond
    return <polygon points={`${cx},${cy - s} ${cx + s},${cy} ${cx},${cy + s} ${cx - s},${cy}`} fill={color} />
  }
  if (eventType === 'BUY') {
    // Up triangle
    return <polygon points={`${cx},${cy - s} ${cx + s * 0.9},${cy + s * 0.6} ${cx - s * 0.9},${cy + s * 0.6}`} fill={color} />
  }
  // SELL — down triangle
  return <polygon points={`${cx},${cy + s} ${cx + s * 0.9},${cy - s * 0.6} ${cx - s * 0.9},${cy - s * 0.6}`} fill={color} />
}

function EquityTimelineChart({ points, labels }) {
  const [hoveredPoint, setHoveredPoint] = useState(null)

  const width  = 900
  const height = 260
  const pad    = { top: 16, right: 24, bottom: 36, left: 68 }
  const chartW = width  - pad.left - pad.right
  const chartH = height - pad.top  - pad.bottom

  const ms     = points.map((p) => new Date(p.snapshotTime).getTime())
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
    return { ...p, x: xAt(timeMs), y: yAt(Number(p.totalEquity)) }
  })

  const path = chartPoints.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(2)} ${p.y.toFixed(2)}`).join(' ')
  const yTicks = 4

  const tooltipWidth = 320
  const tooltipBaseH = 130
  const tooltipLineH = 18
  const tooltipExtra = hoveredPoint?.holdings?.length ? Math.min(5, hoveredPoint.holdings.length) * tooltipLineH : 0
  const tooltipHeight = tooltipBaseH + tooltipExtra
  const tooltipX = hoveredPoint
    ? Math.min(Math.max(hoveredPoint.x + 14, pad.left), width - pad.right - tooltipWidth)
    : 0
  const tooltipY = hoveredPoint
    ? Math.min(Math.max(hoveredPoint.y - 14, pad.top), height - pad.bottom - tooltipHeight)
    : 0

  const eventTypeLabel = (et) => labels?.eventType?.[et] ?? et

  return (
    <div className="w-full rounded-lg border border-gray-700 bg-gray-900/50 p-2">
      {/* Legend */}
      <div className="flex gap-4 px-2 pb-1 text-xs text-gray-400">
        {['SESSION_START', 'BUY', 'SELL'].map((et) => (
          <span key={et} className="flex items-center gap-1">
            <svg width="14" height="14" viewBox="-7 -7 14 14">
              <EventMarker cx={0} cy={0} eventType={et} size={5} />
            </svg>
            {eventTypeLabel(et)}
          </span>
        ))}
      </div>

      <svg viewBox={`0 0 ${width} ${height}`} className="w-full h-auto">
        {/* Grid + Y labels */}
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

        {/* Axes */}
        <line x1={pad.left} y1={height - pad.bottom} x2={width - pad.right} y2={height - pad.bottom} stroke="#4b5563" />
        <line x1={pad.left} y1={pad.top} x2={pad.left} y2={height - pad.bottom} stroke="#4b5563" />

        {/* X-axis time labels (first, mid, last) */}
        {[0, Math.floor(chartPoints.length / 2), chartPoints.length - 1]
          .filter((idx, pos, arr) => arr.indexOf(idx) === pos && chartPoints[idx])
          .map((idx) => {
            const p = chartPoints[idx]
            const label = new Date(p.snapshotTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
            return (
              <text key={idx} x={p.x} y={height - pad.bottom + 14} textAnchor="middle" fontSize="9" fill="#6b7280">
                {label}
              </text>
            )
          })}

        {/* Equity line */}
        <path d={path} fill="none" stroke="#22d3ee" strokeWidth="2" />

        {/* Event markers */}
        {chartPoints.map((p) => {
          const isHovered = hoveredPoint?.triggerTransactionId === p.triggerTransactionId
          return (
            <g
              key={p.triggerTransactionId}
              onMouseEnter={() => setHoveredPoint(p)}
              onMouseLeave={() => setHoveredPoint(null)}
              style={{ cursor: 'pointer' }}
            >
              {/* Hit area */}
              <circle cx={p.x} cy={p.y} r={10} fill="transparent" />
              <EventMarker cx={p.x} cy={p.y} eventType={p.eventType ?? 'SELL'} size={5} hovered={isHovered} />
            </g>
          )
        })}

        {/* Tooltip */}
        {hoveredPoint && (
          <foreignObject x={tooltipX} y={tooltipY} width={tooltipWidth} height={tooltipHeight}>
            <div xmlns="http://www.w3.org/1999/xhtml" style={{
              background: 'rgba(17,24,39,0.97)',
              border: `1px solid ${eventColor(hoveredPoint.eventType ?? 'SELL')}55`,
              borderLeft: `3px solid ${eventColor(hoveredPoint.eventType ?? 'SELL')}`,
              borderRadius: '8px',
              padding: '10px 12px',
              color: '#e5e7eb',
              fontSize: '11px',
              lineHeight: 1.4,
              boxShadow: '0 10px 25px rgba(0,0,0,0.4)',
            }}>
              <div style={{ color: eventColor(hoveredPoint.eventType ?? 'SELL'), fontWeight: 700, marginBottom: '5px', fontSize: '12px' }}>
                {eventTypeLabel(hoveredPoint.eventType ?? 'SELL')}
                {hoveredPoint.triggerSymbol ? ` — ${hoveredPoint.triggerSymbol}` : ''}
              </div>
              <div>{labels.tooltip.time}: {new Date(hoveredPoint.snapshotTime).toLocaleString()}</div>
              <div style={{ marginTop: '4px' }}>
                <span style={{ color: '#22d3ee' }}>{labels.totalEquity}: {fmt(hoveredPoint.totalEquity, 4)}</span>
              </div>
              <div>{labels.currentBalance}: {fmt(hoveredPoint.currentBalance, 4)}</div>
              <div>{labels.holdingsValue}: {fmt(hoveredPoint.holdingsMarketValue, 4)}</div>
              <div style={{ wordBreak: 'break-all', color: '#6b7280', marginTop: '4px', fontSize: '10px' }}>
                {labels.tooltip.txId}: {hoveredPoint.triggerTransactionId}
              </div>
              {hoveredPoint.eventType !== 'SESSION_START' && (
                <>
                  <div style={{ marginTop: '5px', color: '#9ca3af' }}>{labels.tooltip.holdings}:</div>
                  {hoveredPoint.holdings?.length > 0 ? (
                    hoveredPoint.holdings.slice(0, 5).map((h) => (
                      <div key={`${hoveredPoint.triggerTransactionId}-${h.symbol}`} style={{ fontFamily: 'ui-monospace, monospace', fontSize: '10px' }}>
                        {h.symbol}: {fmt(h.quantity, 6)} × {fmt(h.markPrice, 4)} = {fmt(h.marketValue, 4)}
                      </div>
                    ))
                  ) : (
                    <div style={{ color: '#9ca3af' }}>{labels.tooltip.noHoldings}</div>
                  )}
                  {hoveredPoint.holdings?.length > 5 && (
                    <div style={{ color: '#9ca3af' }}>+{hoveredPoint.holdings.length - 5} more...</div>
                  )}
                </>
              )}
            </div>
          </foreignObject>
        )}
      </svg>
    </div>
  )
}
