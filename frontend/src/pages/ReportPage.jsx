import { useState, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import {
  useDailyReport,
  useDailySymbolBreakdown,
  useDailyTimeAnalytics,
  useDailyHourly,
  useOpenPositions,
} from '../hooks/useDashboard.js'
import { useSettings } from '../context/SettingsContext.jsx'
import { formatTime } from '../utils/dateFormat.js'

// ── Helpers ─────────────────────────────────────────────────────────────────

function toDateStr(d) {
  return d.toISOString().split('T')[0]
}

function fmtPnl(v) {
  if (v === undefined || v === null) return '—'
  const n = Number(v)
  const sign = n >= 0 ? '+' : ''
  return `${sign}$${Math.abs(n).toFixed(2)}`
}

function pnlColor(v) {
  const n = Number(v)
  if (n > 0) return 'text-green-600'
  if (n < 0) return 'text-red-500'
  return 'text-gray-500'
}

function fmtPct(v) {
  if (v === undefined || v === null) return '—'
  return `${(Number(v) * 100).toFixed(1)}%`
}

function fmtNum(v, decimals = 4) {
  if (v === undefined || v === null) return '—'
  return Number(v).toFixed(decimals)
}

function fmtMins(v) {
  if (!v) return '—'
  const mins = Math.round(Number(v))
  if (mins < 60) return `${mins}m`
  const h = Math.floor(mins / 60)
  const m = mins % 60
  return m > 0 ? `${h}h ${m}m` : `${h}h`
}

// ── Section wrapper ──────────────────────────────────────────────────────────

function Section({ title, children }) {
  return (
    <div className="rounded-xl border border-[#dbe4ef] bg-white" style={{ boxShadow: '0 8px 30px rgba(15,23,42,0.06)' }}>
      <div className="px-5 py-3 border-b border-[#dbe4ef]">
        <h2 className="text-sm font-semibold" style={{ color: '#1e3a5f' }}>{title}</h2>
      </div>
      <div className="p-5">{children}</div>
    </div>
  )
}

// ── Loading / Error states ───────────────────────────────────────────────────

function LoadingRow() {
  return (
    <div className="flex items-center justify-center py-8 text-sm" style={{ color: '#94a3b8' }}>
      <span className="animate-pulse">Loading...</span>
    </div>
  )
}

function EmptyRow({ msg }) {
  return (
    <div className="flex items-center justify-center py-8 text-sm" style={{ color: '#94a3b8' }}>
      {msg}
    </div>
  )
}

// ── SVG Mini Bar Chart (Buy vs Sell by symbol) ───────────────────────────────

function BarChart({ symbols }) {
  if (!symbols || symbols.length === 0) return <EmptyRow msg="No trades" />
  const maxCount = Math.max(...symbols.map(s => s.buyCount + s.sellCount), 1)
  const barW = 24, gap = 8, height = 80

  return (
    <div className="overflow-x-auto">
      <svg
        width={symbols.length * (barW * 2 + gap + 16) + 8}
        height={height + 40}
        className="block"
      >
        {symbols.map((s, i) => {
          const x = i * (barW * 2 + gap + 16) + 4
          const buyH = Math.round((s.buyCount / maxCount) * height)
          const sellH = Math.round((s.sellCount / maxCount) * height)
          return (
            <g key={s.symbol}>
              {/* Buy bar */}
              <rect
                x={x}
                y={height - buyH}
                width={barW}
                height={buyH}
                fill="#22c55e"
                rx={3}
              />
              {/* Sell bar */}
              <rect
                x={x + barW + 4}
                y={height - sellH}
                width={barW}
                height={sellH}
                fill="#ef4444"
                rx={3}
              />
              {/* Label */}
              <text
                x={x + barW}
                y={height + 14}
                textAnchor="middle"
                fontSize={9}
                fill="#64748b"
              >
                {s.symbol.replace('USDT', '')}
              </text>
              {/* Buy count */}
              {buyH > 0 && (
                <text x={x + barW / 2} y={height - buyH - 2} textAnchor="middle" fontSize={8} fill="#166534">
                  {s.buyCount}
                </text>
              )}
              {/* Sell count */}
              {sellH > 0 && (
                <text x={x + barW + 4 + barW / 2} y={height - sellH - 2} textAnchor="middle" fontSize={8} fill="#991b1b">
                  {s.sellCount}
                </text>
              )}
            </g>
          )
        })}
        {/* Legend */}
        <rect x={4} y={height + 26} width={10} height={10} fill="#22c55e" rx={2} />
        <text x={16} y={height + 35} fontSize={9} fill="#64748b">Buy</text>
        <rect x={44} y={height + 26} width={10} height={10} fill="#ef4444" rx={2} />
        <text x={56} y={height + 35} fontSize={9} fill="#64748b">Sell</text>
      </svg>
    </div>
  )
}

// ── Hourly heatmap row ───────────────────────────────────────────────────────

function HourlyHeatmap({ buckets }) {
  if (!buckets || buckets.length === 0) return <EmptyRow msg="No hourly data" />
  const allHours = Array.from({ length: 24 }, (_, i) => {
    const found = buckets.find(b => b.hour === i)
    return { hour: i, buyCount: found?.buyCount ?? 0, sellCount: found?.sellCount ?? 0 }
  })
  const maxTotal = Math.max(...allHours.map(h => h.buyCount + h.sellCount), 1)

  return (
    <div className="overflow-x-auto">
      <div className="flex gap-1 items-end" style={{ minWidth: 600 }}>
        {allHours.map(h => {
          const total = h.buyCount + h.sellCount
          const intensity = total / maxTotal
          const bg = total === 0
            ? '#f1f5f9'
            : `rgba(47, 111, 237, ${0.15 + intensity * 0.75})`
          return (
            <div key={h.hour} className="flex flex-col items-center" style={{ flex: 1 }}>
              <div
                className="w-full rounded text-xs text-center font-medium"
                style={{
                  height: 28,
                  background: bg,
                  color: intensity > 0.5 ? '#fff' : '#334155',
                  lineHeight: '28px',
                  fontSize: 10,
                }}
                title={`${h.hour}:00 — ${h.buyCount}B / ${h.sellCount}S`}
              >
                {total > 0 ? total : ''}
              </div>
              <span className="text-xs mt-1" style={{ color: '#94a3b8', fontSize: 9 }}>
                {h.hour}
              </span>
            </div>
          )
        })}
      </div>
    </div>
  )
}

// ── Holding duration histogram (bucketed) ───────────────────────────────────

function DurationHistogram({ trades }) {
  const closed = trades?.filter(t => t.holdingMinutes != null) ?? []
  if (closed.length === 0) return <EmptyRow msg="No closed trades" />

  const buckets = [
    { label: '<5m',   min: 0,   max: 5 },
    { label: '5-15m', min: 5,   max: 15 },
    { label: '15-30m',min: 15,  max: 30 },
    { label: '30m-1h',min: 30,  max: 60 },
    { label: '1-4h',  min: 60,  max: 240 },
    { label: '>4h',   min: 240, max: Infinity },
  ]

  const counts = buckets.map(b => ({
    label: b.label,
    count: closed.filter(t => t.holdingMinutes >= b.min && t.holdingMinutes < b.max).length,
  }))

  const maxCount = Math.max(...counts.map(c => c.count), 1)

  return (
    <div className="flex gap-2 items-end h-24">
      {counts.map(c => (
        <div key={c.label} className="flex-1 flex flex-col items-center gap-1">
          <span className="text-xs font-medium" style={{ color: '#475569' }}>{c.count}</span>
          <div
            className="w-full rounded-t"
            style={{
              height: Math.max(4, Math.round((c.count / maxCount) * 60)),
              background: c.count > 0 ? '#2f6fed' : '#e2e8f0',
            }}
          />
          <span className="text-xs text-center" style={{ color: '#94a3b8', fontSize: 9 }}>{c.label}</span>
        </div>
      ))}
    </div>
  )
}

// ── Main ReportPage ──────────────────────────────────────────────────────────

export function ReportPage() {
  const { t } = useTranslation('report')
  const { systemTimezone } = useSettings()
  const [selectedDate, setSelectedDate] = useState(toDateStr(new Date()))
  const [activeTab, setActiveTab] = useState('overview')

  const { data: summary,       loading: summaryLoading }   = useDailyReport(selectedDate)
  const { data: symbols,       loading: symbolsLoading }   = useDailySymbolBreakdown(selectedDate)
  const { data: timeData,      loading: timeLoading }      = useDailyTimeAnalytics(selectedDate)
  const { data: hourlyBuckets, loading: hourlyLoading }    = useDailyHourly(selectedDate)
  const { data: positions,     loading: posLoading }       = useOpenPositions()

  const TABS = [
    { id: 'overview',  label: t('tabs.overview') },
    { id: 'symbols',   label: t('tabs.symbols') },
    { id: 'pnl',       label: t('tabs.pnl') },
    { id: 'holdings',  label: t('tabs.holdings') },
    { id: 'time',      label: t('tabs.time') },
  ]

  const totalEquity = useMemo(() => {
    if (!positions) return null
    return positions.reduce((sum, p) => sum + (p.currentPrice ?? 0) * (p.quantity ?? 0), 0)
  }, [positions])

  return (
    <div className="space-y-5">
      {/* ── Page header + date filter ──────────────────────────────────────── */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
          <p className="text-sm mt-0.5" style={{ color: '#64748b' }}>{t('subtitle')}</p>
        </div>
        <div className="flex items-center gap-2">
          <label className="text-sm font-medium" style={{ color: '#475569' }}>{t('date')}</label>
          <input
            type="date"
            value={selectedDate}
            max={toDateStr(new Date())}
            onChange={e => setSelectedDate(e.target.value)}
            className="rounded-lg border border-[#dbe4ef] px-3 py-1.5 text-sm"
            style={{ color: '#0f172a', background: '#fff' }}
          />
          <button
            onClick={() => setSelectedDate(toDateStr(new Date()))}
            className="rounded-lg px-3 py-1.5 text-sm font-medium border border-[#2f6fed] text-[#2f6fed] hover:bg-[#eff6ff] transition-colors"
          >
            {t('today')}
          </button>
        </div>
      </div>

      {/* ── KPI Cards ─────────────────────────────────────────────────────── */}
      {summaryLoading ? (
        <LoadingRow />
      ) : (
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-3">
          <StatCard
            title={t('kpi.totalTrades')}
            value={summary?.totalTrades ?? 0}
            icon="📊"
          />
          <StatCard
            title={t('kpi.buyOrders')}
            value={summary?.buyOrders ?? 0}
            icon="🟢"
            colorClass="text-green-600"
          />
          <StatCard
            title={t('kpi.sellOrders')}
            value={summary?.sellOrders ?? 0}
            icon="🔴"
            colorClass="text-red-500"
          />
          <StatCard
            title={t('kpi.winRate')}
            value={fmtPct(summary?.winRate)}
            subtitle={`${summary?.winTrades ?? 0}W / ${summary?.lossTrades ?? 0}L`}
            icon="🏆"
            colorClass={summary?.winRate >= 0.5 ? 'text-green-600' : 'text-red-500'}
          />
          <StatCard
            title={t('kpi.realizedPnL')}
            value={fmtPnl(summary?.realizedPnL)}
            subtitle={`PF: ${summary?.profitFactor === 999 ? '∞' : fmtNum(summary?.profitFactor, 2)}`}
            icon="💰"
            colorClass={pnlColor(summary?.realizedPnL)}
          />
        </div>
      )}

      {/* ── Section Tabs ──────────────────────────────────────────────────── */}
      <div className="flex gap-1 border-b border-[#dbe4ef] overflow-x-auto">
        {TABS.map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`px-4 py-2 text-sm font-medium whitespace-nowrap transition-colors border-b-2 ${
              activeTab === tab.id
                ? 'border-[#2f6fed] text-[#2f6fed]'
                : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* ── Tab: Overview ─────────────────────────────────────────────────── */}
      {activeTab === 'overview' && (
        <div className="space-y-4">
          {/* Win/Loss Quality */}
          <Section title={t('sections.winLoss')}>
            {summaryLoading ? <LoadingRow /> : (
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <div className="text-center">
                  <p className="text-2xl font-bold text-green-600">{fmtPnl(summary?.grossProfit)}</p>
                  <p className="text-xs mt-1" style={{ color: '#64748b' }}>{t('labels.grossProfit')}</p>
                </div>
                <div className="text-center">
                  <p className="text-2xl font-bold text-red-500">{fmtPnl(summary?.grossLoss)}</p>
                  <p className="text-xs mt-1" style={{ color: '#64748b' }}>{t('labels.grossLoss')}</p>
                </div>
                <div className="text-center">
                  <p className="text-2xl font-bold text-green-600">{fmtPnl(summary?.avgWin)}</p>
                  <p className="text-xs mt-1" style={{ color: '#64748b' }}>{t('labels.avgWin')}</p>
                </div>
                <div className="text-center">
                  <p className="text-2xl font-bold text-red-500">{fmtPnl(summary?.avgLoss)}</p>
                  <p className="text-xs mt-1" style={{ color: '#64748b' }}>{t('labels.avgLoss')}</p>
                </div>
              </div>
            )}
          </Section>

          {/* Order types */}
          <Section title={t('sections.orderTypes')}>
            {summaryLoading ? <LoadingRow /> : (
              <div className="flex flex-wrap gap-3">
                {[
                  { label: t('labels.marketOrders'), value: summary?.marketOrders ?? 0, color: '#2f6fed' },
                  { label: t('labels.limitOrders'),  value: summary?.limitOrders  ?? 0, color: '#7c3aed' },
                  { label: t('labels.failedOrders'), value: summary?.failedOrders ?? 0, color: '#ef4444' },
                ].map(item => (
                  <div
                    key={item.label}
                    className="flex items-center gap-3 rounded-xl px-4 py-3"
                    style={{ background: '#f8fafc', border: '1px solid #e2e8f0', minWidth: 140 }}
                  >
                    <span className="text-xl font-bold" style={{ color: item.color }}>{item.value}</span>
                    <span className="text-xs" style={{ color: '#64748b' }}>{item.label}</span>
                  </div>
                ))}
              </div>
            )}
          </Section>

          {/* Trade frequency chart */}
          <Section title={t('sections.frequency')}>
            {symbolsLoading ? <LoadingRow /> : symbols?.length === 0 ? <EmptyRow msg={t('empty.noTrades')} /> : (
              <BarChart symbols={symbols} />
            )}
          </Section>

          {/* Hourly heatmap */}
          <Section title={t('sections.hourlyHeatmap')}>
            {hourlyLoading ? <LoadingRow /> : (
              <HourlyHeatmap buckets={hourlyBuckets} />
            )}
          </Section>
        </div>
      )}

      {/* ── Tab: Symbol Breakdown ──────────────────────────────────────────── */}
      {activeTab === 'symbols' && (
        <Section title={t('sections.symbolBreakdown')}>
          {symbolsLoading ? <LoadingRow /> : symbols?.length === 0 ? <EmptyRow msg={t('empty.noTrades')} /> : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm border-collapse">
                <thead>
                  <tr style={{ background: '#f8fafc' }}>
                    {[
                      t('col.symbol'), t('col.buyCount'), t('col.sellCount'),
                      t('col.buyQty'), t('col.sellQty'),
                      t('col.avgBuy'), t('col.avgSell'),
                      t('col.winLoss'), t('col.realizedPnL'), t('col.lastTrade'),
                    ].map(h => (
                      <th key={h} className="text-left px-3 py-2 text-xs font-semibold border-b border-[#e2e8f0]" style={{ color: '#475569' }}>
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {symbols.map((s, i) => (
                    <tr
                      key={s.symbol}
                      className="hover:bg-[#f8fafc] transition-colors"
                      style={{ borderBottom: '1px solid #f1f5f9' }}
                    >
                      <td className="px-3 py-2 font-semibold" style={{ color: '#1e3a5f' }}>{s.symbol}</td>
                      <td className="px-3 py-2 text-green-600 font-medium">{s.buyCount}</td>
                      <td className="px-3 py-2 text-red-500 font-medium">{s.sellCount}</td>
                      <td className="px-3 py-2" style={{ color: '#475569' }}>{fmtNum(s.buyQty)}</td>
                      <td className="px-3 py-2" style={{ color: '#475569' }}>{fmtNum(s.sellQty)}</td>
                      <td className="px-3 py-2" style={{ color: '#475569' }}>{s.avgBuyPrice ? `$${fmtNum(s.avgBuyPrice, 2)}` : '—'}</td>
                      <td className="px-3 py-2" style={{ color: '#475569' }}>{s.avgSellPrice ? `$${fmtNum(s.avgSellPrice, 2)}` : '—'}</td>
                      <td className="px-3 py-2">
                        <span className="text-green-600">{s.winCount}W</span>
                        {' / '}
                        <span className="text-red-500">{s.lossCount}L</span>
                      </td>
                      <td className={`px-3 py-2 font-medium ${pnlColor(s.realizedPnL)}`}>{fmtPnl(s.realizedPnL)}</td>
                      <td className="px-3 py-2 text-xs" style={{ color: '#94a3b8' }}>
                        {s.lastTradeTime ? formatTime(s.lastTradeTime, systemTimezone) : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Section>
      )}

      {/* ── Tab: PnL Analysis ─────────────────────────────────────────────── */}
      {activeTab === 'pnl' && (
        <div className="space-y-4">
          {/* Per-symbol PnL bar */}
          <Section title={t('sections.pnlBySymbol')}>
            {symbolsLoading ? <LoadingRow /> : symbols?.length === 0 ? <EmptyRow msg={t('empty.noTrades')} /> : (() => {
              const maxAbs = Math.max(...symbols.map(s => Math.abs(Number(s.realizedPnL))), 1)
              return (
                <div className="space-y-2">
                  {symbols.map(s => {
                    const pnl = Number(s.realizedPnL)
                    const pct = Math.abs(pnl) / maxAbs * 100
                    return (
                      <div key={s.symbol} className="flex items-center gap-3">
                        <span className="text-xs font-medium w-24 shrink-0" style={{ color: '#475569' }}>
                          {s.symbol.replace('USDT', '')}
                        </span>
                        <div className="flex-1 h-5 rounded-full bg-gray-100 overflow-hidden">
                          <div
                            className="h-full rounded-full transition-all"
                            style={{
                              width: `${pct}%`,
                              background: pnl >= 0 ? '#22c55e' : '#ef4444',
                            }}
                          />
                        </div>
                        <span className={`text-xs font-semibold w-20 text-right ${pnlColor(pnl)}`}>
                          {fmtPnl(pnl)}
                        </span>
                      </div>
                    )
                  })}
                </div>
              )
            })()}
          </Section>

          {/* PnL summary cards */}
          <Section title={t('sections.pnlSummary')}>
            {summaryLoading ? <LoadingRow /> : (
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <StatCard title={t('labels.grossProfit')} value={fmtPnl(summary?.grossProfit)} colorClass="text-green-600" />
                <StatCard title={t('labels.grossLoss')}   value={fmtPnl(summary?.grossLoss)}   colorClass="text-red-500" />
                <StatCard title={t('labels.profitFactor')} value={summary?.profitFactor === 999 ? '∞' : fmtNum(summary?.profitFactor, 2)} colorClass="text-blue-600" />
                <StatCard title={t('kpi.realizedPnL')} value={fmtPnl(summary?.realizedPnL)} colorClass={pnlColor(summary?.realizedPnL)} />
              </div>
            )}
          </Section>
        </div>
      )}

      {/* ── Tab: Holdings ─────────────────────────────────────────────────── */}
      {activeTab === 'holdings' && (
        <div className="space-y-4">
          {/* Open positions */}
          <Section title={t('sections.openPositions')}>
            {posLoading ? <LoadingRow /> : !positions || positions.length === 0 ? (
              <EmptyRow msg={t('empty.noPositions')} />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm border-collapse">
                  <thead>
                    <tr style={{ background: '#f8fafc' }}>
                      {[t('col.symbol'), t('col.qty'), t('col.entryPrice'), t('col.currentPrice'), t('col.marketValue'), t('col.unrealizedPnL'), t('col.roe')].map(h => (
                        <th key={h} className="text-left px-3 py-2 text-xs font-semibold border-b border-[#e2e8f0]" style={{ color: '#475569' }}>
                          {h}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {positions.map(p => {
                      const marketValue = (p.currentPrice ?? 0) * (p.quantity ?? 0)
                      return (
                        <tr key={p.symbol} className="hover:bg-[#f8fafc]" style={{ borderBottom: '1px solid #f1f5f9' }}>
                          <td className="px-3 py-2 font-semibold" style={{ color: '#1e3a5f' }}>{p.symbol}</td>
                          <td className="px-3 py-2" style={{ color: '#475569' }}>{fmtNum(p.quantity)}</td>
                          <td className="px-3 py-2" style={{ color: '#475569' }}>${fmtNum(p.entryPrice ?? p.avgEntryPrice, 2)}</td>
                          <td className="px-3 py-2" style={{ color: '#475569' }}>${fmtNum(p.currentPrice, 2)}</td>
                          <td className="px-3 py-2" style={{ color: '#475569' }}>${fmtNum(marketValue, 2)}</td>
                          <td className={`px-3 py-2 font-medium ${pnlColor(p.unrealizedPnL)}`}>{fmtPnl(p.unrealizedPnL)}</td>
                          <td className={`px-3 py-2 font-medium ${pnlColor(p.roe)}`}>{p.roe != null ? `${Number(p.roe).toFixed(2)}%` : '—'}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot>
                    <tr style={{ background: '#f8fafc', borderTop: '2px solid #e2e8f0' }}>
                      <td colSpan={4} className="px-3 py-2 text-xs font-semibold" style={{ color: '#475569' }}>
                        {t('labels.totalMarketValue')}
                      </td>
                      <td className="px-3 py-2 font-bold" style={{ color: '#1e3a5f' }}>
                        ${fmtNum(totalEquity, 2)}
                      </td>
                      <td colSpan={2} className="px-3 py-2 text-xs font-semibold text-right" style={{ color: '#475569' }}>
                        {positions.length} {t('labels.positions')}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            )}
          </Section>

          {/* Allocation breakdown */}
          {positions && positions.length > 0 && (
            <Section title={t('sections.allocation')}>
              <div className="space-y-2">
                {positions.map(p => {
                  const mv   = (p.currentPrice ?? 0) * (p.quantity ?? 0)
                  const pct  = totalEquity > 0 ? (mv / totalEquity) * 100 : 0
                  return (
                    <div key={p.symbol} className="flex items-center gap-3">
                      <span className="text-xs font-medium w-24 shrink-0" style={{ color: '#475569' }}>
                        {p.symbol.replace('USDT', '')}
                      </span>
                      <div className="flex-1 h-4 rounded-full bg-gray-100 overflow-hidden">
                        <div
                          className="h-full rounded-full"
                          style={{ width: `${pct}%`, background: '#2f6fed' }}
                        />
                      </div>
                      <span className="text-xs font-semibold w-12 text-right" style={{ color: '#475569' }}>
                        {pct.toFixed(1)}%
                      </span>
                    </div>
                  )
                })}
              </div>
            </Section>
          )}
        </div>
      )}

      {/* ── Tab: Time Analytics ───────────────────────────────────────────── */}
      {activeTab === 'time' && (
        <div className="space-y-4">
          {/* Summary cards */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            {timeLoading ? <LoadingRow /> : (
              <>
                <StatCard
                  title={t('labels.avgHolding')}
                  value={fmtMins(timeData?.avgHoldingMinutes)}
                  icon="⏱️"
                />
                <StatCard
                  title={t('labels.avgHoldingWin')}
                  value={fmtMins(timeData?.avgHoldingWinMinutes)}
                  icon="✅"
                  colorClass="text-green-600"
                />
                <StatCard
                  title={t('labels.avgHoldingLoss')}
                  value={fmtMins(timeData?.avgHoldingLossMinutes)}
                  icon="❌"
                  colorClass="text-red-500"
                />
              </>
            )}
          </div>

          {/* Duration histogram */}
          <Section title={t('sections.durationHistogram')}>
            {timeLoading ? <LoadingRow /> : (
              <DurationHistogram trades={timeData?.trades} />
            )}
          </Section>

          {/* Individual trade list */}
          <Section title={t('sections.tradeLog')}>
            {timeLoading ? <LoadingRow /> : !timeData?.trades?.length ? <EmptyRow msg={t('empty.noTrades')} /> : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm border-collapse">
                  <thead>
                    <tr style={{ background: '#f8fafc' }}>
                      {[t('col.symbol'), t('col.side'), t('col.openTime'), t('col.closeTime'), t('col.duration'), t('col.realizedPnL')].map(h => (
                        <th key={h} className="text-left px-3 py-2 text-xs font-semibold border-b border-[#e2e8f0]" style={{ color: '#475569' }}>
                          {h}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {timeData.trades.slice(0, 50).map(tr => (
                      <tr key={tr.orderId} className="hover:bg-[#f8fafc]" style={{ borderBottom: '1px solid #f1f5f9' }}>
                        <td className="px-3 py-2 font-medium" style={{ color: '#1e3a5f' }}>{tr.symbol}</td>
                        <td className={`px-3 py-2 font-medium ${tr.side === 'Buy' ? 'text-green-600' : 'text-red-500'}`}>
                          {tr.side}
                        </td>
                        <td className="px-3 py-2 text-xs" style={{ color: '#64748b' }}>
                          {formatTime(tr.openTime, systemTimezone)}
                        </td>
                        <td className="px-3 py-2 text-xs" style={{ color: '#64748b' }}>
                          {tr.closeTime ? formatTime(tr.closeTime, systemTimezone) : '—'}
                        </td>
                        <td className="px-3 py-2 text-xs" style={{ color: '#475569' }}>
                          {fmtMins(tr.holdingMinutes)}
                        </td>
                        <td className={`px-3 py-2 font-medium text-xs ${pnlColor(tr.realizedPnL)}`}>
                          {tr.realizedPnL != null ? fmtPnl(tr.realizedPnL) : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Section>
        </div>
      )}
    </div>
  )
}
