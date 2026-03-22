import { useState, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import {
  useBudgetStatus,
  useBudgetLedger,
  useBudgetEquityCurve,
  apiBudgetDeposit,
  apiBudgetWithdraw,
  apiBudgetReset,
} from '../hooks/useDashboard.js'

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtUsd(v) {
  if (v === undefined || v === null) return '—'
  const n = Number(v)
  const sign = n >= 0 ? '+' : '-'
  return n >= 0 ? `$${n.toFixed(2)}` : `-$${Math.abs(n).toFixed(2)}`
}

function fmtUsdSigned(v) {
  if (v === undefined || v === null) return '—'
  const n = Number(v)
  const sign = n > 0 ? '+' : ''
  return `${sign}$${n.toFixed(2)}`
}

function pnlColor(v) {
  const n = Number(v)
  if (n > 0) return 'text-green-600'
  if (n < 0) return 'text-red-500'
  return 'text-gray-500'
}

function fmtDate(iso) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString([], { dateStyle: 'short', timeStyle: 'short' })
}

// ── Layout primitives ─────────────────────────────────────────────────────────

function Section({ title, children, action }) {
  return (
    <div className="rounded-xl border border-[#dbe4ef] bg-white" style={{ boxShadow: '0 8px 30px rgba(15,23,42,0.06)' }}>
      <div className="px-5 py-3 border-b border-[#dbe4ef] flex items-center justify-between">
        <h2 className="text-sm font-semibold" style={{ color: '#1e3a5f' }}>{title}</h2>
        {action}
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
    <div className="flex items-center justify-center py-8 text-sm" style={{ color: '#94a3b8' }}>{msg}</div>
  )
}

// ── Operation Modal ───────────────────────────────────────────────────────────

function OperationModal({ type, onClose, onSuccess, t }) {
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [requestedBy, setRequestedBy] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  const isReset = type === 'reset'
  const title = t(`modal.${type}Title`)
  const amountLabel = isReset ? t('modal.newCapital') : t('modal.amount')

  const handleSubmit = useCallback(async (e) => {
    e.preventDefault()
    const parsed = parseFloat(amount)
    if (!parsed || parsed <= 0) { setError('Amount must be a positive number'); return }

    setLoading(true)
    setError(null)
    try {
      if (type === 'deposit') {
        await apiBudgetDeposit({ amount: parsed, description, requestedBy })
      } else if (type === 'withdraw') {
        await apiBudgetWithdraw({ amount: parsed, description, requestedBy })
      } else {
        await apiBudgetReset({ newInitialCapital: parsed, description, requestedBy })
      }
      onSuccess()
    } catch (err) {
      setError(err.message ?? 'Operation failed')
    } finally {
      setLoading(false)
    }
  }, [amount, description, requestedBy, type, onSuccess])

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-40">
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-md mx-4 p-6">
        <h3 className="text-base font-semibold mb-4" style={{ color: '#1e3a5f' }}>{title}</h3>

        {isReset && (
          <p className="text-xs mb-4 p-3 rounded-lg bg-amber-50 border border-amber-200 text-amber-700">
            {t('modal.resetWarning')}
          </p>
        )}

        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label className="block text-xs font-medium mb-1" style={{ color: '#475569' }}>{amountLabel}</label>
            <input
              type="number"
              step="0.01"
              min="0.01"
              value={amount}
              onChange={e => setAmount(e.target.value)}
              className="w-full text-sm border border-[#dbe4ef] rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-400"
              placeholder="0.00"
              required
            />
          </div>

          <div>
            <label className="block text-xs font-medium mb-1" style={{ color: '#475569' }}>{t('modal.description')}</label>
            <input
              type="text"
              value={description}
              onChange={e => setDescription(e.target.value)}
              className="w-full text-sm border border-[#dbe4ef] rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-400"
            />
          </div>

          <div>
            <label className="block text-xs font-medium mb-1" style={{ color: '#475569' }}>{t('modal.requestedBy')}</label>
            <input
              type="text"
              value={requestedBy}
              onChange={e => setRequestedBy(e.target.value)}
              className="w-full text-sm border border-[#dbe4ef] rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-400"
            />
          </div>

          {error && (
            <p className="text-xs text-red-500 p-2 rounded-lg bg-red-50 border border-red-200">{error}</p>
          )}

          <div className="flex gap-2 pt-2">
            <button
              type="button"
              onClick={onClose}
              disabled={loading}
              className="flex-1 text-sm px-4 py-2 rounded-lg border border-[#dbe4ef] hover:bg-[#f1f5f9] transition-colors"
              style={{ color: '#475569' }}
            >
              {t('modal.cancel')}
            </button>
            <button
              type="submit"
              disabled={loading}
              className="flex-1 text-sm px-4 py-2 rounded-lg text-white font-medium transition-colors disabled:opacity-50"
              style={{ background: type === 'reset' ? '#dc2626' : type === 'withdraw' ? '#d97706' : '#2f6fed' }}
            >
              {loading ? t('modal.submitting') : t('modal.confirm')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Ledger table ──────────────────────────────────────────────────────────────

const PAGE_SIZE = 20

function LedgerTable({ t }) {
  const [page, setPage] = useState(0)
  const { data, loading, error } = useBudgetLedger({ limit: PAGE_SIZE, offset: page * PAGE_SIZE })

  const transactions = data?.transactions ?? []
  const total = data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  const typeBadge = (type) => {
    const colors = {
      INITIAL:     'bg-gray-100 text-gray-600',
      SESSION_PNL: 'bg-blue-100 text-blue-700',
      DEPOSIT:     'bg-green-100 text-green-700',
      WITHDRAW:    'bg-orange-100 text-orange-700',
      RESET:       'bg-red-100 text-red-600',
    }
    return (
      <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${colors[type] ?? 'bg-gray-100 text-gray-500'}`}>
        {t(`types.${type}`)}
      </span>
    )
  }

  if (loading && transactions.length === 0) return <LoadingRow />
  if (error) return <EmptyRow msg={t('ledger.noData')} />
  if (transactions.length === 0) return <EmptyRow msg={t('ledger.noData')} />

  return (
    <div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left border-b border-[#dbe4ef]" style={{ color: '#64748b' }}>
              <th className="pb-2 pr-3 font-medium">{t('ledger.date')}</th>
              <th className="pb-2 pr-3 font-medium">{t('ledger.type')}</th>
              <th className="pb-2 pr-3 font-medium text-right">{t('ledger.before')}</th>
              <th className="pb-2 pr-3 font-medium text-right">{t('ledger.amount')}</th>
              <th className="pb-2 pr-3 font-medium text-right">{t('ledger.after')}</th>
              <th className="pb-2 pr-3 font-medium">{t('ledger.description')}</th>
              <th className="pb-2 font-medium">{t('ledger.createdBy')}</th>
            </tr>
          </thead>
          <tbody>
            {transactions.map(tx => (
              <tr key={tx.id} className="border-b border-[#f1f5f9] last:border-0 hover:bg-[#f8fafc]">
                <td className="py-2 pr-3 text-xs font-mono" style={{ color: '#475569' }}>
                  {fmtDate(tx.recordedAtUtc)}
                </td>
                <td className="py-2 pr-3">{typeBadge(tx.referenceType)}</td>
                <td className="py-2 pr-3 text-right font-mono text-xs" style={{ color: '#475569' }}>
                  ${Number(tx.cashBalanceBefore).toFixed(2)}
                </td>
                <td className={`py-2 pr-3 text-right font-mono text-xs font-semibold ${pnlColor(tx.adjustmentAmount)}`}>
                  {fmtUsdSigned(tx.adjustmentAmount)}
                </td>
                <td className="py-2 pr-3 text-right font-mono text-xs font-semibold" style={{ color: '#1e3a5f' }}>
                  ${Number(tx.cashBalanceAfter).toFixed(2)}
                </td>
                <td className="py-2 pr-3 text-xs" style={{ color: '#64748b' }}>
                  {tx.description ?? '—'}
                </td>
                <td className="py-2 text-xs font-mono" style={{ color: '#94a3b8' }}>
                  {tx.createdBy ?? '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="flex items-center justify-between mt-3 pt-3 border-t border-[#f1f5f9]">
        <p className="text-xs" style={{ color: '#94a3b8' }}>
          {t('ledger.totalCount', { count: total })}
        </p>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setPage(p => Math.max(0, p - 1))}
            disabled={page === 0}
            className="text-xs px-3 py-1.5 rounded-lg border border-[#dbe4ef] disabled:opacity-40 hover:bg-[#f1f5f9] transition-colors"
            style={{ color: '#475569' }}
          >
            {t('ledger.prevPage')}
          </button>
          <span className="text-xs" style={{ color: '#64748b' }}>
            {t('ledger.page', { page: page + 1 })} / {totalPages}
          </span>
          <button
            onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))}
            disabled={page >= totalPages - 1}
            className="text-xs px-3 py-1.5 rounded-lg border border-[#dbe4ef] disabled:opacity-40 hover:bg-[#f1f5f9] transition-colors"
            style={{ color: '#475569' }}
          >
            {t('ledger.nextPage')}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Equity Curve Chart (pure SVG, no extra deps) ──────────────────────────────

const TYPE_COLOR = {
  INITIAL:      '#94a3b8',
  DEPOSIT:      '#22c55e',
  WITHDRAW:     '#f59e0b',
  RESET:        '#ef4444',
  SESSION_PNL:  '#3b82f6',
}

function EquityCurveChart({ t }) {
  const { data: points, loading, error } = useBudgetEquityCurve({})

  const [tooltip, setTooltip] = useState(null)

  const chartData = useMemo(() => {
    if (!points || points.length === 0) return null
    const W = 800, H = 200, PAD = { t: 16, r: 16, b: 32, l: 72 }
    const innerW = W - PAD.l - PAD.r
    const innerH = H - PAD.t - PAD.b

    const balances = points.map(p => Number(p.cashBalance))
    const minB = Math.min(...balances)
    const maxB = Math.max(...balances)
    const range = maxB - minB || 1

    const xs = points.map((_, i) => PAD.l + (i / Math.max(points.length - 1, 1)) * innerW)
    const ys = balances.map(b => PAD.t + innerH - ((b - minB) / range) * innerH)

    const polyline = xs.map((x, i) => `${x},${ys[i]}`).join(' ')
    const areaPath = `M${xs[0]},${PAD.t + innerH} ` +
      xs.map((x, i) => `L${x},${ys[i]}`).join(' ') +
      ` L${xs[xs.length - 1]},${PAD.t + innerH} Z`

    // Y-axis ticks (4 labels)
    const yTicks = [0, 0.33, 0.67, 1].map(f => ({
      y: PAD.t + innerH - f * innerH,
      label: `$${(minB + f * range).toFixed(0)}`,
    }))

    return { W, H, PAD, xs, ys, polyline, areaPath, yTicks, points }
  }, [points])

  if (loading && !chartData) return <LoadingRow />
  if (error || !chartData) return <EmptyRow msg={t('equityCurve.noData')} />

  const { W, H, PAD, xs, ys, polyline, areaPath, yTicks } = chartData

  return (
    <div className="relative">
      <svg
        viewBox={`0 0 ${W} ${H}`}
        className="w-full"
        style={{ height: 180 }}
        onMouseLeave={() => setTooltip(null)}
      >
        <defs>
          <linearGradient id="ecGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#3b82f6" stopOpacity="0.18" />
            <stop offset="100%" stopColor="#3b82f6" stopOpacity="0.01" />
          </linearGradient>
        </defs>

        {/* Grid lines */}
        {yTicks.map((tk, i) => (
          <g key={i}>
            <line x1={PAD.l} x2={W - PAD.r} y1={tk.y} y2={tk.y}
              stroke="#e2e8f0" strokeWidth="1" strokeDasharray="4 3" />
            <text x={PAD.l - 6} y={tk.y + 4} textAnchor="end"
              fontSize="9" fill="#94a3b8">{tk.label}</text>
          </g>
        ))}

        {/* Area fill */}
        <path d={areaPath} fill="url(#ecGrad)" />

        {/* Line */}
        <polyline points={polyline} fill="none" stroke="#3b82f6" strokeWidth="1.5"
          strokeLinejoin="round" strokeLinecap="round" />

        {/* Data points */}
        {xs.map((x, i) => {
          const pt = chartData.points[i]
          const color = TYPE_COLOR[pt.referenceType] ?? '#3b82f6'
          return (
            <circle key={i} cx={x} cy={ys[i]} r="4" fill={color} stroke="white" strokeWidth="1.5"
              style={{ cursor: 'pointer' }}
              onMouseEnter={e => {
                const svgRect = e.currentTarget.closest('svg').getBoundingClientRect()
                setTooltip({
                  x: e.clientX - svgRect.left,
                  y: e.clientY - svgRect.top,
                  pt,
                })
              }}
            />
          )
        })}
      </svg>

      {/* Tooltip */}
      {tooltip && (
        <div
          className="absolute z-10 bg-white border border-[#dbe4ef] rounded-lg shadow-lg px-3 py-2 text-xs pointer-events-none"
          style={{ left: Math.min(tooltip.x + 10, W - 160), top: Math.max(tooltip.y - 60, 0), minWidth: 150 }}
        >
          <p className="font-mono text-[10px] mb-1" style={{ color: '#94a3b8' }}>
            {new Date(tooltip.pt.recordedAtUtc).toLocaleString()}
          </p>
          <p style={{ color: '#1e3a5f' }}>
            <span style={{ color: '#64748b' }}>{t('equityCurve.tooltip.balance')}: </span>
            <strong>${Number(tooltip.pt.cashBalance).toFixed(2)}</strong>
          </p>
          <p className={pnlColor(tooltip.pt.adjustmentAmount)}>
            <span style={{ color: '#64748b' }}>{t('equityCurve.tooltip.change')}: </span>
            <strong>{Number(tooltip.pt.adjustmentAmount) >= 0 ? '+' : ''}{Number(tooltip.pt.adjustmentAmount).toFixed(2)}</strong>
          </p>
          <p style={{ color: '#64748b' }}>
            {t('equityCurve.tooltip.type')}: <span style={{ color: TYPE_COLOR[tooltip.pt.referenceType] ?? '#475569' }}>{tooltip.pt.referenceType}</span>
          </p>
        </div>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function BudgetPage() {
  const { t } = useTranslation('budget')
  const { data: status, loading, error, refresh } = useBudgetStatus()
  const [modal, setModal] = useState(null) // 'deposit' | 'withdraw' | 'reset' | null

  const handleSuccess = useCallback(() => {
    setModal(null)
    refresh()
  }, [refresh])

  const btnBase = 'text-xs px-3 py-1.5 rounded-lg border font-medium transition-colors'

  return (
    <div className="space-y-5">

      {/* Page header */}
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold" style={{ color: '#1e3a5f' }}>{t('title')}</h1>
          <p className="text-sm mt-0.5" style={{ color: '#64748b' }}>{t('subtitle')}</p>
        </div>
        <div className="flex gap-2 flex-shrink-0">
          <button
            onClick={() => setModal('deposit')}
            className={`${btnBase} border-green-300 text-green-700 hover:bg-green-50`}
          >
            + {t('actions.deposit')}
          </button>
          <button
            onClick={() => setModal('withdraw')}
            className={`${btnBase} border-amber-300 text-amber-700 hover:bg-amber-50`}
          >
            - {t('actions.withdraw')}
          </button>
          <button
            onClick={() => setModal('reset')}
            className={`${btnBase} border-red-300 text-red-600 hover:bg-red-50`}
          >
            {t('actions.reset')}
          </button>
        </div>
      </div>

      {/* Status cards */}
      {loading && !status
        ? <LoadingRow />
        : error || !status
          ? <EmptyRow msg={t('empty.noAccount')} />
          : (
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
              <StatCard
                label={t('status.initialCapital')}
                value={`$${Number(status.initialCapital).toFixed(2)}`}
              />
              <StatCard
                label={t('status.currentCash')}
                value={`$${Number(status.currentCashBalance).toFixed(2)}`}
              />
              <StatCard
                label={t('status.realizedPnL')}
                value={fmtUsdSigned(status.totalRealizedPnL)}
                valueClass={pnlColor(status.totalRealizedPnL)}
              />
              <StatCard
                label={t('status.roi')}
                value={`${Number(status.roiPercent) >= 0 ? '+' : ''}${Number(status.roiPercent).toFixed(2)}%`}
                valueClass={pnlColor(status.roiPercent)}
              />
              <StatCard
                label={t('status.currency')}
                value={status.currency}
              />
            </div>
          )}

      {/* Equity Curve */}
      <Section title={t('equityCurve.title')}>
        <EquityCurveChart t={t} />
      </Section>

      {/* Ledger */}
      <Section title={t('ledger.title')}>
        <LedgerTable t={t} />
      </Section>

      {/* Modal */}
      {modal && (
        <OperationModal
          type={modal}
          onClose={() => setModal(null)}
          onSuccess={handleSuccess}
          t={t}
        />
      )}
    </div>
  )
}
