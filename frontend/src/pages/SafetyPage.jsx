import { useState, useEffect, useRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { SafetyLight } from '../components/SafetyLight.jsx'
import { StatCard } from '../components/StatCard.jsx'
import { useRiskStats, useRiskConfig, useRiskEvaluations } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'
import { useSettings } from '../context/SettingsContext.jsx'
import { formatTime } from '../utils/dateFormat.js'

const V_CHUNK = 20
const V_MAX = 100

// ── Outcome helpers ────────────────────────────────────────────────────────

const OUTCOME_STYLES = {
  Safe:     { bg: 'bg-green-100',  text: 'text-green-700',  border: 'border-green-200' },
  Risk:     { bg: 'bg-amber-100',  text: 'text-amber-700',  border: 'border-amber-200' },
  Rejected: { bg: 'bg-red-100',    text: 'text-red-700',    border: 'border-red-200'   },
}
const RULE_RESULT_STYLES = {
  Pass:     { bg: 'bg-green-50',  text: 'text-green-700'  },
  Adjusted: { bg: 'bg-amber-50',  text: 'text-amber-700'  },
  Fail:     { bg: 'bg-red-50',    text: 'text-red-700'    },
  Skipped:  { bg: 'bg-gray-50',   text: 'text-gray-400'   },
}

function OutcomeBadge({ outcome }) {
  const { t } = useTranslation('safety')
  const s = OUTCOME_STYLES[outcome] ?? OUTCOME_STYLES.Rejected
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-semibold border ${s.bg} ${s.text} ${s.border}`}>
      {t(`evaluationHistory.outcomes.${outcome}`, { defaultValue: outcome })}
    </span>
  )
}

function RuleResultBadge({ result }) {
  const { t } = useTranslation('safety')
  const s = RULE_RESULT_STYLES[result] ?? RULE_RESULT_STYLES.Skipped
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${s.bg} ${s.text}`}>
      {t(`evaluationDetail.ruleResults.${result}`, { defaultValue: result })}
    </span>
  )
}

// ── Recent Safety Checks (legacy in-memory list) ───────────────────────────

function ValidationRow({ v }) {
  const { systemTimezone } = useSettings()
  return (
    <div className={`flex items-center gap-3 p-3 rounded-lg border text-sm ${
      v.approved ? 'bg-green-50 border-green-100' : 'bg-red-50 border-red-100'
    }`}>
      <span className="text-lg">{v.approved ? '✅' : '❌'}</span>
      <div className="flex-1">
        <span className="font-medium">{v.symbol?.replace('USDT', '/USDT')}</span>
        <span className={`ml-2 text-xs px-1.5 py-0.5 rounded ${
          v.side?.toLowerCase() === 'buy' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
        }`}>
          {v.side?.toUpperCase()}
        </span>
        {!v.approved && v.rejectionReason && (
          <span className="ml-2 text-xs text-red-600">→ {v.rejectionReason}</span>
        )}
      </div>
      <span className="text-xs text-gray-400 flex-shrink-0">
        {formatTime(v.timestampUtc, systemTimezone, { seconds: true })}
      </span>
    </div>
  )
}

// ── Evaluation Detail Drawer ───────────────────────────────────────────────

function EvaluationDetailDrawer({ evaluationId, onClose }) {
  const { t } = useTranslation('safety')
  const { systemTimezone } = useSettings()
  const [detail, setDetail] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!evaluationId) return
    setLoading(true)
    setError(null)
    fetch(`/api/risk-evaluations/${evaluationId}`)
      .then(r => r.ok ? r.json() : Promise.reject(`HTTP ${r.status}`))
      .then(data => { setDetail(data); setLoading(false) })
      .catch(err => { setError(String(err)); setLoading(false) })
  }, [evaluationId])

  return (
    <>
      {/* Overlay */}
      <div
        className="fixed inset-0 bg-black bg-opacity-30 z-40"
        onClick={onClose}
      />
      {/* Drawer */}
      <div className="fixed top-0 right-0 h-full w-full max-w-xl bg-white z-50 shadow-2xl flex flex-col overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-200 bg-gray-50 flex-shrink-0">
          <h3 className="text-base font-semibold text-gray-800">{t('evaluationDetail.title')}</h3>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 text-xl leading-none"
            aria-label={t('evaluationDetail.close')}
          >
            ✕
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-5 space-y-5">
          {loading && (
            <div className="text-center py-12 text-gray-400">Loading…</div>
          )}
          {error && (
            <div className="text-center py-6 text-red-500 text-sm">{error}</div>
          )}
          {detail && (
            <>
              {/* Final Decision */}
              <section>
                <h4 className="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-2">
                  {t('evaluationDetail.decision')}
                </h4>
                <div className="rounded-lg p-4 border" style={{ background: '#f8fafc', borderColor: '#e2e8f0' }}>
                  <div className="flex items-center gap-3 mb-2">
                    <OutcomeBadge outcome={detail.outcome} />
                    <span className="text-xs text-gray-400">{detail.evaluationLatencyMs}ms</span>
                  </div>
                  {detail.finalReasonMessage && (
                    <p className="text-sm text-gray-700">{detail.finalReasonMessage}</p>
                  )}
                  <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-400">
                    <span>{t('evaluationDetail.timestamp')}: {formatTime(detail.evaluatedAtUtc, systemTimezone, { seconds: true })}</span>
                    {detail.correlationId && (
                      <span className="truncate max-w-[200px]">{t('evaluationDetail.correlationId')}: {detail.correlationId}</span>
                    )}
                  </div>
                </div>
              </section>

              {/* Order Context */}
              <section>
                <h4 className="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-2">
                  {t('evaluationDetail.orderContext')}
                </h4>
                <div className="grid grid-cols-2 gap-2 text-sm">
                  {[
                    [t('evaluationHistory.table.symbol'),   detail.symbol?.replace('USDT', '/USDT')],
                    [t('evaluationHistory.table.side'),     detail.side?.toUpperCase()],
                    [t('evaluationDetail.quantity'),        detail.requestedQuantity],
                    [t('evaluationDetail.entryPrice'),      detail.marketPriceAtEvaluation != null ? `$${Number(detail.marketPriceAtEvaluation).toLocaleString()}` : '—'],
                    [t('evaluationDetail.adjustedQty'),     detail.adjustedQuantity ?? '—'],
                    [t('evaluationDetail.session'),         detail.sessionId ?? '—'],
                  ].map(([label, value]) => (
                    <div key={label} className="rounded p-2" style={{ background: '#f3f7fb' }}>
                      <p className="text-xs text-gray-400">{label}</p>
                      <p className="font-medium text-gray-800 truncate">{value}</p>
                    </div>
                  ))}
                </div>
              </section>

              {/* Rule-by-Rule Results */}
              {detail.ruleResults?.length > 0 && (
                <section>
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-2">
                    {t('evaluationDetail.rules')}
                  </h4>
                  <div className="space-y-2">
                    {detail.ruleResults.map((r, i) => (
                      <div key={i} className="rounded-lg border p-3 text-sm" style={{ borderColor: '#e2e8f0' }}>
                        <div className="flex items-center justify-between gap-2 mb-1">
                          <span className="font-medium text-gray-700 truncate">{r.ruleName}</span>
                          <div className="flex items-center gap-2 flex-shrink-0">
                            <RuleResultBadge result={r.result} />
                            {r.durationMs > 0 && (
                              <span className="text-xs text-gray-300">{r.durationMs}ms</span>
                            )}
                          </div>
                        </div>
                        {r.reasonMessage && (
                          <p className="text-xs text-gray-500 mt-1">{r.reasonMessage}</p>
                        )}
                        {(r.thresholdValue || r.actualValue) && (
                          <div className="flex gap-4 mt-1 text-xs text-gray-400">
                            {r.thresholdValue && <span>Threshold: {r.thresholdValue}</span>}
                            {r.actualValue && <span>Actual: {r.actualValue}</span>}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </section>
              )}

              {/* Evaluation ID */}
              <p className="text-xs text-gray-300 break-all">
                {t('evaluationDetail.evaluationId')}: {detail.evaluationId}
              </p>
            </>
          )}
        </div>
      </div>
    </>
  )
}

// ── Evaluation History Table ───────────────────────────────────────────────

function EvaluationHistorySection() {
  const { t } = useTranslation('safety')
  const { systemTimezone } = useSettings()
  const [filters, setFilters] = useState({ symbol: '', outcome: '', from: '', to: '' })
  const [page, setPage] = useState(1)
  const [selectedId, setSelectedId] = useState(null)
  const PAGE_SIZE = 20

  const activeFilters = {
    symbol:  filters.symbol  || undefined,
    outcome: filters.outcome || undefined,
    from:    filters.from    || undefined,
    to:      filters.to      || undefined,
    page,
    pageSize: PAGE_SIZE,
  }

  const { data, loading } = useRiskEvaluations(activeFilters)
  const items = data?.items ?? []
  const totalCount = data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  const handleFilterChange = useCallback((key, value) => {
    setFilters(prev => ({ ...prev, [key]: value }))
    setPage(1)
  }, [])

  const clearFilters = () => {
    setFilters({ symbol: '', outcome: '', from: '', to: '' })
    setPage(1)
  }

  const hasFilters = Object.values(filters).some(Boolean)

  return (
    <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold" style={{ color: '#0f172a' }}>
          {t('evaluationHistory.title')}
          {totalCount > 0 && (
            <span className="ml-2 text-sm font-normal text-gray-400">
              {t('evaluationHistory.totalCount', { count: totalCount })}
            </span>
          )}
        </h2>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-2 mb-4">
        <input
          type="text"
          placeholder={t('evaluationHistory.filters.symbol')}
          value={filters.symbol}
          onChange={e => handleFilterChange('symbol', e.target.value.toUpperCase())}
          className="border border-gray-200 rounded-lg px-3 py-1.5 text-sm w-32 focus:outline-none focus:border-blue-400"
        />
        <select
          value={filters.outcome}
          onChange={e => handleFilterChange('outcome', e.target.value)}
          className="border border-gray-200 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:border-blue-400"
        >
          <option value="">{t('evaluationHistory.filters.allOutcomes')}</option>
          <option value="Safe">{t('evaluationHistory.outcomes.Safe')}</option>
          <option value="Risk">{t('evaluationHistory.outcomes.Risk')}</option>
          <option value="Rejected">{t('evaluationHistory.outcomes.Rejected')}</option>
        </select>
        <input
          type="datetime-local"
          value={filters.from}
          onChange={e => handleFilterChange('from', e.target.value)}
          className="border border-gray-200 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:border-blue-400"
        />
        <input
          type="datetime-local"
          value={filters.to}
          onChange={e => handleFilterChange('to', e.target.value)}
          className="border border-gray-200 rounded-lg px-3 py-1.5 text-sm focus:outline-none focus:border-blue-400"
        />
        {hasFilters && (
          <button
            onClick={clearFilters}
            className="px-3 py-1.5 text-sm text-gray-500 hover:text-gray-700 border border-gray-200 rounded-lg hover:bg-gray-50"
          >
            {t('evaluationHistory.filters.clear')}
          </button>
        )}
      </div>

      {/* Table */}
      {loading && items.length === 0 ? (
        <div className="text-center py-8 text-gray-400 text-sm">Loading…</div>
      ) : items.length === 0 ? (
        <div className="text-center py-8 text-gray-400 text-sm">{t('evaluationHistory.noResults')}</div>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 text-left text-xs text-gray-400 uppercase tracking-wide">
                  <th className="pb-2 pr-3">{t('evaluationHistory.table.time')}</th>
                  <th className="pb-2 pr-3">{t('evaluationHistory.table.symbol')}</th>
                  <th className="pb-2 pr-3">{t('evaluationHistory.table.side')}</th>
                  <th className="pb-2 pr-3">{t('evaluationHistory.table.entryPrice')}</th>
                  <th className="pb-2 pr-3">{t('evaluationHistory.table.outcome')}</th>
                  <th className="pb-2 pr-3 max-w-[200px]">{t('evaluationHistory.table.reason')}</th>
                  <th className="pb-2 pr-3">{t('evaluationHistory.table.latency')}</th>
                  <th className="pb-2"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50">
                {items.map(ev => (
                  <tr key={ev.evaluationId} className="hover:bg-gray-50 transition-colors">
                    <td className="py-2 pr-3 text-xs text-gray-400 whitespace-nowrap">
                      {formatTime(ev.evaluatedAtUtc, systemTimezone, { seconds: true })}
                    </td>
                    <td className="py-2 pr-3 font-medium text-gray-800 whitespace-nowrap">
                      {ev.symbol?.replace('USDT', '/USDT')}
                    </td>
                    <td className="py-2 pr-3">
                      <span className={`px-1.5 py-0.5 rounded text-xs font-medium ${
                        ev.side?.toLowerCase() === 'buy' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
                      }`}>
                        {ev.side?.toUpperCase()}
                      </span>
                    </td>
                    <td className="py-2 pr-3 text-gray-600 whitespace-nowrap">
                      {ev.marketPriceAtEvaluation != null
                        ? `$${Number(ev.marketPriceAtEvaluation).toLocaleString()}`
                        : '—'}
                    </td>
                    <td className="py-2 pr-3">
                      <OutcomeBadge outcome={ev.outcome} />
                    </td>
                    <td className="py-2 pr-3 max-w-[200px]">
                      <span className="text-xs text-gray-500 truncate block" title={ev.finalReasonMessage}>
                        {ev.finalReasonMessage ?? '—'}
                      </span>
                    </td>
                    <td className="py-2 pr-3 text-xs text-gray-400 whitespace-nowrap">
                      {ev.evaluationLatencyMs}ms
                    </td>
                    <td className="py-2">
                      <button
                        onClick={() => setSelectedId(ev.evaluationId)}
                        className="text-xs text-blue-600 hover:text-blue-800 hover:underline whitespace-nowrap"
                      >
                        {t('evaluationHistory.table.viewDetails')}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between mt-4 pt-3 border-t border-gray-100 text-sm text-gray-500">
              <span>{t('evaluationHistory.totalCount', { count: totalCount })}</span>
              <div className="flex gap-2">
                <button
                  disabled={page <= 1}
                  onClick={() => setPage(p => p - 1)}
                  className="px-3 py-1 rounded border border-gray-200 disabled:opacity-40 hover:bg-gray-50"
                >
                  ←
                </button>
                <span className="px-2 py-1">{page} / {totalPages}</span>
                <button
                  disabled={page >= totalPages}
                  onClick={() => setPage(p => p + 1)}
                  className="px-3 py-1 rounded border border-gray-200 disabled:opacity-40 hover:bg-gray-50"
                >
                  →
                </button>
              </div>
            </div>
          )}
        </>
      )}

      {/* Detail Drawer */}
      {selectedId && (
        <EvaluationDetailDrawer
          evaluationId={selectedId}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────

export function SafetyPage() {
  const { t } = useTranslation(['safety', 'common'])
  const { data: risk } = useRiskStats()
  const { data: config } = useRiskConfig()
  const [visible, setVisible] = useState(V_CHUNK)
  const sentinelRef = useRef(null)

  const validations = risk?.recentValidations ?? []

  useEffect(() => { setVisible(V_CHUNK) }, [risk?.recentValidations?.length])

  useEffect(() => {
    const el = sentinelRef.current
    if (!el) return
    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting && visible < Math.min(validations.length, V_MAX)) {
        setVisible(v => Math.min(v + V_CHUNK, V_MAX))
      }
    }, { threshold: 0.1 })
    observer.observe(el)
    return () => observer.disconnect()
  }, [visible, validations.length])

  const totalToday = (risk?.todayApproved ?? 0) + (risk?.todayRejected ?? 0)
  const approvalRate = totalToday > 0
    ? Math.round((risk.todayApproved / totalToday) * 100)
    : null

  const riskConfigRows = config ? [
    { label: t('riskConfig.minRiskReward'), value: config.minRiskReward },
    { label: t('riskConfig.maxPositionSize'), value: `${config.maxPositionSizePercent}%` },
    { label: t('riskConfig.maxOrderValue'), value: `$${config.maxOrderNotional}` },
    { label: t('riskConfig.maxDailyLoss'), value: `${config.maxDrawdownPercent}%` },
    { label: t('riskConfig.cooldownPeriod'), value: `${config.cooldownSeconds}s` },
    {
      label: t('riskConfig.effectiveBalance'),
      value: config.effectiveBalance?.isAvailable
        ? `$${Number(config.effectiveBalance.amount).toLocaleString()}`
        : '—'
    },
    {
      label: t('riskConfig.balanceSource'),
      value: config.effectiveBalance?.source
        ? `${config.effectiveBalance.source} (${config.effectiveBalance.environment ?? 'n/a'})`
        : 'N/A'
    },
  ] : []

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>

      {/* Main Safety Status */}
      <SafetyLight riskStats={risk} riskConfig={config} />

      {/* Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title={t('common:todayPnl')}
          value={risk ? formatPnl(risk.dailyPnl) : '—'}
          icon="💵"
          colorClass={risk ? pnlColorClass(risk.dailyPnl) : 'text-gray-400'}
        />
        <StatCard
          title={t('stats.approved')}
          value={risk?.todayApproved ?? '—'}
          subtitle={t('stats.ordersPassed')}
          icon="✅"
          colorClass="text-green-600"
        />
        <StatCard
          title={t('stats.rejected')}
          value={risk?.todayRejected ?? '—'}
          subtitle={t('stats.ordersBlocked')}
          icon="🛑"
          colorClass="text-red-600"
        />
        <StatCard
          title={t('stats.approvalRate')}
          value={approvalRate !== null ? `${approvalRate}%` : '—'}
          subtitle={t('stats.ofChecks', { total: totalToday })}
          icon="📊"
          colorClass={approvalRate !== null && approvalRate < 50 ? 'text-red-500' : 'text-gray-800'}
        />
      </div>

      {/* Risk Settings */}
      {config && (
        <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}>
          <h2 className="text-lg font-semibold mb-4" style={{ color: '#0f172a' }}>{t('riskConfig.title')}</h2>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
            {riskConfigRows.map(item => (
              <div key={item.label} className="rounded-lg p-3" style={{ background: '#f3f7fb' }}>
                <p className="text-xs text-gray-500">{item.label}</p>
                <p className="text-base font-semibold text-gray-800 mt-0.5">{item.value}</p>
              </div>
            ))}
          </div>
          <div className="mt-4">
            <p className="text-xs text-gray-500 mb-2">{t('riskConfig.allowedPairs')}</p>
            <div className="flex flex-wrap gap-2">
              {config.allowedSymbols?.map(s => (
                <span key={s} className="px-2 py-1 bg-blue-50 text-blue-700 text-xs rounded-lg font-medium">
                  {s.replace('USDT', '/USDT')}
                </span>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Evaluation History (DB-persisted, paginated, filterable) */}
      <EvaluationHistorySection />

      {/* Recent Safety Checks (in-memory ring buffer) */}
      <div>
        <h2 className="text-lg font-semibold mb-3" style={{ color: '#0f172a' }}>
          {t('validationHistory.title')}
          {validations.length > 0 && (
            <span className="ml-2 text-sm font-normal text-gray-400">
              {t('validationHistory.last', { count: Math.min(visible, validations.length) })}
            </span>
          )}
        </h2>
        {!validations.length ? (
          <div className="text-center py-8 text-gray-400">{t('validationHistory.noHistory')}</div>
        ) : (
          <div className="overflow-y-auto max-h-[480px] pr-1">
            <div className="space-y-2">
              {validations.slice(0, visible).map((v, i) => (
                <ValidationRow key={i} v={v} />
              ))}
              <div ref={sentinelRef} className="h-1" />
              {visible >= V_MAX && validations.length > V_MAX && (
                <div className="text-center py-2 text-xs text-gray-400">
                  {t('validationHistory.maxReached', { max: V_MAX })}
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
