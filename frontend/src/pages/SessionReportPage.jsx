import { useState, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import {
  useSessionDailyReport,
  useSessionEquityCurve,
  useSessionSymbols,
} from '../hooks/useDashboard.js'
import { useSettings } from '../context/SettingsContext.jsx'
import { formatTime, todayInTz } from '../utils/dateFormat.js'

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

function fmtTime(iso, timezone) {
  return formatTime(iso, timezone)
}

function fmtNum(v, decimals = 4) {
  if (v === undefined || v === null) return '—'
  return Number(v).toFixed(decimals)
}

// ── Layout primitives ─────────────────────────────────────────────────────────

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

// ── Session label ─────────────────────────────────────────────────────────────

const SESSION_LABELS = {
  1: 'S1 00:00–08:00',
  2: 'S2 08:00–16:00',
  3: 'S3 16:00–24:00',
}

// ── Equity bar chart ──────────────────────────────────────────────────────────

function SessionPnlChart({ sessions }) {
  if (!sessions || sessions.length === 0) return null

  const values = sessions.map(s => Number(s.realizedPnL))
  const absMax = Math.max(...values.map(Math.abs), 0.01)
  const BAR_H = 140

  return (
    <div className="overflow-x-auto">
      <svg width="100%" viewBox={`0 0 ${sessions.length * 80} ${BAR_H + 48}`} className="min-w-[400px]">
        {sessions.map((s, i) => {
          const v = Number(s.realizedPnL)
          const barPx = Math.max((Math.abs(v) / absMax) * (BAR_H / 2 - 4), 2)
          const isPos = v >= 0
          const cx = i * 80 + 40
          const midY = BAR_H / 2
          const y = isPos ? midY - barPx : midY
          const fill = v > 0 ? '#16a34a' : v < 0 ? '#ef4444' : '#94a3b8'

          return (
            <g key={s.sessionId}>
              {/* zero line */}
              {i === 0 && (
                <line x1="0" y1={midY} x2={sessions.length * 80} y2={midY}
                  stroke="#e2e8f0" strokeWidth="1" strokeDasharray="4 3" />
              )}
              <rect x={cx - 22} y={y} width={44} height={barPx} fill={fill} rx="3" opacity="0.85" />
              <text x={cx} y={y - 4} textAnchor="middle" fontSize="10"
                fill={fill} fontWeight="600">
                {v === 0 ? '' : fmtPnl(v)}
              </text>
              <text x={cx} y={BAR_H + 14} textAnchor="middle" fontSize="9" fill="#64748b">
                {`S${s.sessionNumber}`}
              </text>
              <text x={cx} y={BAR_H + 26} textAnchor="middle" fontSize="8" fill="#94a3b8">
                {s.totalOrders > 0 ? `${s.totalOrders}t` : '—'}
              </text>
            </g>
          )
        })}
      </svg>
    </div>
  )
}

// ── Symbol breakdown table ────────────────────────────────────────────────────

function SymbolTable({ symbols, t, loading, error }) {
  if (loading) return <LoadingRow />
  if (error) return <EmptyRow msg={t('empty.error')} />
  if (!symbols || symbols.length === 0) return <EmptyRow msg={t('empty.noSymbols')} />

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left border-b border-[#dbe4ef]" style={{ color: '#64748b' }}>
            <th className="pb-2 pr-3 font-medium">{t('col.symbol')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.buyCount')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.sellCount')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.buyQty')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.sellQty')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.avgBuy')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.avgSell')}</th>
            <th className="pb-2 pr-3 font-medium text-right">{t('col.winLoss')}</th>
            <th className="pb-2 font-medium text-right">{t('col.realizedPnL')}</th>
          </tr>
        </thead>
        <tbody>
          {symbols.map(s => (
            <tr key={s.symbol} className="border-b border-[#f1f5f9] last:border-0 hover:bg-[#f8fafc]">
              <td className="py-2 pr-3 font-mono font-semibold text-xs" style={{ color: '#1e3a5f' }}>{s.symbol}</td>
              <td className="py-2 pr-3 text-right text-green-700">{s.buyCount}</td>
              <td className="py-2 pr-3 text-right text-red-500">{s.sellCount}</td>
              <td className="py-2 pr-3 text-right font-mono text-xs">{fmtNum(s.buyQty)}</td>
              <td className="py-2 pr-3 text-right font-mono text-xs">{fmtNum(s.sellQty)}</td>
              <td className="py-2 pr-3 text-right font-mono text-xs">{s.avgBuyPrice ? fmtNum(s.avgBuyPrice, 2) : '—'}</td>
              <td className="py-2 pr-3 text-right font-mono text-xs">{s.avgSellPrice ? fmtNum(s.avgSellPrice, 2) : '—'}</td>
              <td className="py-2 pr-3 text-right text-xs">
                <span className="text-green-600">{s.winTrades}W</span>
                <span className="text-gray-400 mx-0.5">/</span>
                <span className="text-red-500">{s.lossTrades}L</span>
              </td>
              <td className={`py-2 text-right font-mono font-semibold text-xs ${pnlColor(s.realizedPnL)}`}>
                {fmtPnl(s.realizedPnL)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function SessionReportPage() {
  const { t } = useTranslation('sessionReport')
  const { systemTimezone } = useSettings()
  const [date, setDate] = useState(() => todayInTz(systemTimezone))
  const [mode, setMode] = useState('all')
  const [selectedSession, setSelectedSession] = useState(null)

  const apiMode = mode === 'all' ? undefined : mode

  const { data: sessions, loading: sessLoading, error: sessError, lastUpdated } =
    useSessionDailyReport(date, apiMode)

  const { data: equityCurve, loading: curveLoading } =
    useSessionEquityCurve(date, date, apiMode)

  const { data: symbolRows, loading: symLoading, error: symError } =
    useSessionSymbols(selectedSession, apiMode)

  // ── Derived summary ──────────────────────────────────────────────────────

  const summary = useMemo(() => {
    if (!sessions || sessions.length === 0) return null
    const activeSessions = sessions.filter(s => s.totalOrders > 0)
    const totalPnL = sessions.reduce((acc, s) => acc + Number(s.realizedPnL), 0)
    const totalTrades = sessions.reduce((acc, s) => acc + s.totalOrders, 0)
    const best = sessions.reduce((a, b) => Number(b.realizedPnL) > Number(a.realizedPnL) ? b : a, sessions[0])
    const worst = sessions.reduce((a, b) => Number(b.realizedPnL) < Number(a.realizedPnL) ? b : a, sessions[0])
    const allFlat = sessions.every(s => s.isFlatAtClose || s.totalOrders === 0)
    return { activeSessions: activeSessions.length, totalPnL, totalTrades, best, worst, allFlat }
  }, [sessions])

  return (
    <div className="space-y-5">

      {/* Page header */}
      <div>
        <h1 className="text-xl font-bold" style={{ color: '#1e3a5f' }}>{t('title')}</h1>
        <p className="text-sm mt-0.5" style={{ color: '#64748b' }}>{t('subtitle')}</p>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-3 items-center">
        <div className="flex items-center gap-2">
          <label className="text-sm font-medium" style={{ color: '#475569' }}>{t('date')}</label>
          <input
            type="date"
            value={date}
            onChange={e => { setDate(e.target.value); setSelectedSession(null) }}
            className="text-sm border border-[#dbe4ef] rounded-lg px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-400"
            style={{ color: '#1e3a5f' }}
          />
        </div>
        <button
          onClick={() => { setDate(todayInTz(systemTimezone)); setSelectedSession(null) }}
          className="text-sm px-3 py-1.5 rounded-lg border border-[#dbe4ef] hover:bg-[#f1f5f9] transition-colors"
          style={{ color: '#475569' }}
        >
          {t('today')}
        </button>
        <select
          value={mode}
          onChange={e => setMode(e.target.value)}
          className="text-sm border border-[#dbe4ef] rounded-lg px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-400"
          style={{ color: '#1e3a5f' }}
        >
          <option value="all">{t('mode.all')}</option>
          <option value="live">{t('mode.live')}</option>
        </select>
        {lastUpdated && (
          <span className="text-xs ml-auto" style={{ color: '#94a3b8' }}>
            {t('lastUpdated')}: {formatTime(lastUpdated, systemTimezone)}
          </span>
        )}
      </div>

      {/* KPI cards */}
      {summary && (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
          <StatCard
            label={t('kpi.totalTrades')}
            value={summary.totalTrades}
          />
          <StatCard
            label={t('kpi.activeSessions')}
            value={`${summary.activeSessions} / 3`}
          />
          <StatCard
            label={t('kpi.totalPnL')}
            value={fmtPnl(summary.totalPnL)}
            valueClass={pnlColor(summary.totalPnL)}
          />
          <StatCard
            label={t('kpi.bestSession')}
            value={summary.best?.totalOrders > 0
              ? `${SESSION_LABELS[summary.best.sessionNumber]?.split(' ')[0]} ${fmtPnl(summary.best.realizedPnL)}`
              : '—'}
            valueClass={pnlColor(summary.best?.realizedPnL)}
          />
          <StatCard
            label={t('kpi.worstSession')}
            value={summary.worst?.totalOrders > 0
              ? `${SESSION_LABELS[summary.worst.sessionNumber]?.split(' ')[0]} ${fmtPnl(summary.worst.realizedPnL)}`
              : '—'}
            valueClass={pnlColor(summary.worst?.realizedPnL)}
          />
        </div>
      )}

      {/* Session financial table */}
      <Section title={t('sections.sessionTable')}>
        {sessLoading
          ? <LoadingRow />
          : sessError
            ? <EmptyRow msg={t('empty.error')} />
            : !sessions || sessions.length === 0
              ? <EmptyRow msg={t('empty.noData')} />
              : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="text-left border-b border-[#dbe4ef]" style={{ color: '#64748b' }}>
                        <th className="pb-2 pr-3 font-medium">{t('col.session')}</th>
                        <th className="pb-2 pr-3 font-medium">{t('col.window')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.trades')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.buys')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.sells')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.rejected')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.winLoss')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.symbols')}</th>
                        <th className="pb-2 pr-3 font-medium text-right">{t('col.realizedPnL')}</th>
                        <th className="pb-2 font-medium text-center">{t('col.flat')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sessions.map(s => {
                        const isActive = s.totalOrders > 0
                        const isSelected = selectedSession === s.sessionId
                        return (
                          <tr
                            key={s.sessionId}
                            onClick={() => setSelectedSession(isSelected ? null : s.sessionId)}
                            className={`border-b border-[#f1f5f9] last:border-0 cursor-pointer transition-colors ${
                              isSelected ? 'bg-blue-50' : isActive ? 'hover:bg-[#f8fafc]' : 'opacity-40'
                            }`}
                          >
                            <td className="py-2.5 pr-3 font-semibold text-xs font-mono" style={{ color: '#1e3a5f' }}>
                              {s.sessionId}
                            </td>
                            <td className="py-2.5 pr-3 text-xs" style={{ color: '#475569' }}>
                              {fmtTime(s.sessionStartUtc, systemTimezone)}–{fmtTime(s.sessionEndUtc, systemTimezone)}
                            </td>
                            <td className="py-2.5 pr-3 text-right font-semibold">{s.totalOrders}</td>
                            <td className="py-2.5 pr-3 text-right text-green-700">{s.buyCount}</td>
                            <td className="py-2.5 pr-3 text-right text-red-500">{s.sellCount}</td>
                            <td className="py-2.5 pr-3 text-right text-orange-500">{s.rejectedCount || 0}</td>
                            <td className="py-2.5 pr-3 text-right text-xs">
                              <span className="text-green-600">{s.winTrades}W</span>
                              <span className="text-gray-400 mx-0.5">/</span>
                              <span className="text-red-500">{s.lossTrades}L</span>
                            </td>
                            <td className="py-2.5 pr-3 text-right text-xs" style={{ color: '#475569' }}>
                              {s.distinctSymbols > 0
                                ? <span title={s.symbolsCsv}>{s.distinctSymbols}</span>
                                : '—'}
                            </td>
                            <td className={`py-2.5 pr-3 text-right font-mono font-semibold text-xs ${pnlColor(s.realizedPnL)}`}>
                              {isActive ? fmtPnl(s.realizedPnL) : '—'}
                            </td>
                            <td className="py-2.5 text-center text-xs">
                              {isActive
                                ? s.isFlatAtClose
                                  ? <span className="text-green-600 font-semibold">{t('flat.yes')}</span>
                                  : <span className="text-red-500 font-semibold">{t('flat.no')}</span>
                                : <span style={{ color: '#94a3b8' }}>—</span>}
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                  <p className="text-xs mt-2" style={{ color: '#94a3b8' }}>{t('clickHint')}</p>
                </div>
              )}
      </Section>

      {/* Equity / PnL chart */}
      <Section title={t('sections.equityChart')}>
        {curveLoading
          ? <LoadingRow />
          : !sessions || sessions.every(s => s.totalOrders === 0)
            ? <EmptyRow msg={t('empty.noData')} />
            : (
              <div>
                <SessionPnlChart sessions={sessions} />
                <p className="text-xs mt-2 text-center" style={{ color: '#94a3b8' }}>
                  {t('chartHint')}
                </p>
              </div>
            )}
      </Section>

      {/* Symbol breakdown — shown when a session is selected */}
      <Section title={
        selectedSession
          ? `${t('sections.symbolBreakdown')} — ${selectedSession}`
          : t('sections.symbolBreakdown')
      }>
        {!selectedSession
          ? <EmptyRow msg={t('empty.selectSession')} />
          : <SymbolTable symbols={symbolRows} t={t} loading={symLoading} error={symError} />}
      </Section>

    </div>
  )
}
