import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import { useSymbolTimeline, useRiskConfig, useTimelineEvents, useTimelineSummary } from '../hooks/useDashboard.js'
import { useSettings } from '../context/SettingsContext.jsx'
import { formatTime, formatDateTime, todayInTz } from '../utils/dateFormat.js'

const TIME_WINDOWS = [
  { value: 5,   labelKey: '5' },
  { value: 10,  labelKey: '10' },
  { value: 15,  labelKey: '15' },
  { value: 30,  labelKey: '30' },
  { value: 60,  labelKey: '60' },
  { value: 120, labelKey: '120' },
  { value: 300, labelKey: '300' },
]

function timeAgo(isoStr) {
  if (!isoStr) return ''
  const diff = (Date.now() - new Date(isoStr).getTime()) / 1000
  if (diff < 60) return `${Math.round(diff)}s`
  if (diff < 3600) return `${Math.round(diff / 60)}m`
  if (diff < 86400) return `${Math.round(diff / 3600)}h`
  return new Date(isoStr).toLocaleDateString()
}

function getEventStyle(eventType, outcome, side) {
  if (eventType === 'RISK_EVALUATION') {
    if (outcome === 'Safe') return { dot: 'bg-green-500', border: 'border-green-200', bg: 'bg-green-50', icon: '✅', badge: 'bg-green-100 text-green-700' }
    if (outcome === 'Rejected') return { dot: 'bg-red-500', border: 'border-red-200', bg: 'bg-red-50', icon: '❌', badge: 'bg-red-100 text-red-700' }
    return { dot: 'bg-yellow-500', border: 'border-yellow-200', bg: 'bg-yellow-50', icon: '⚠️', badge: 'bg-yellow-100 text-yellow-700' }
  }
  if (eventType === 'ORDER') {
    if (outcome === 'FAILED') return { dot: 'bg-red-500', border: 'border-red-200', bg: 'bg-red-50', icon: '💥', badge: 'bg-red-100 text-red-700' }
    const isBuy = side?.toLowerCase() === 'buy'
    return isBuy
      ? { dot: 'bg-blue-500', border: 'border-blue-200', bg: 'bg-blue-50', icon: '📈', badge: 'bg-blue-100 text-blue-700' }
      : { dot: 'bg-purple-500', border: 'border-purple-200', bg: 'bg-purple-50', icon: '📉', badge: 'bg-purple-100 text-purple-700' }
  }
  return { dot: 'bg-gray-400', border: 'border-gray-200', bg: 'bg-gray-50', icon: '📋', badge: 'bg-gray-100 text-gray-600' }
}

const CATEGORY_COLORS = {
  TRADING:        { dot: 'bg-blue-500',   badge: 'bg-blue-100 text-blue-700',   icon: '💱' },
  TRADING_SIGNAL: { dot: 'bg-indigo-500', badge: 'bg-indigo-100 text-indigo-700', icon: '📡' },
  RISK:           { dot: 'bg-orange-500', badge: 'bg-orange-100 text-orange-700', icon: '🛡️' },
  POSITION:       { dot: 'bg-green-500',  badge: 'bg-green-100 text-green-700',  icon: '📊' },
  STRATEGY:       { dot: 'bg-purple-500', badge: 'bg-purple-100 text-purple-700', icon: '🧠' },
  MARKET:         { dot: 'bg-cyan-500',   badge: 'bg-cyan-100 text-cyan-700',    icon: '📈' },
  LIQUIDATION:    { dot: 'bg-red-500',    badge: 'bg-red-100 text-red-700',      icon: '🔥' },
  SESSION:        { dot: 'bg-gray-500',   badge: 'bg-gray-100 text-gray-700',    icon: '⏱️' },
  PRICE_DATA:     { dot: 'bg-slate-400',  badge: 'bg-slate-100 text-slate-600',  icon: '💹' },
  NOTIFICATION:   { dot: 'bg-yellow-500', badge: 'bg-yellow-100 text-yellow-700', icon: '🔔' },
  ANALYSIS:       { dot: 'bg-teal-500',   badge: 'bg-teal-100 text-teal-700',   icon: '🔬' },
  UNKNOWN:        { dot: 'bg-gray-300',   badge: 'bg-gray-100 text-gray-500',   icon: '❓' },
}

const SEVERITY_COLORS = {
  WARNING: 'text-amber-600 bg-amber-50',
  ERROR:   'text-red-600 bg-red-50',
  INFO:    'text-blue-600 bg-blue-50',
  DEBUG:   'text-gray-500 bg-gray-50',
}

function MongoEventItem({ event, systemTimezone }) {
  const [expanded, setExpanded] = useState(false)
  const style = CATEGORY_COLORS[event.event_category] ?? CATEGORY_COLORS.UNKNOWN
  const severityStyle = SEVERITY_COLORS[event.severity] ?? SEVERITY_COLORS.INFO
  const payload = event.payload ?? {}
  const metadata = event.metadata ?? {}

  return (
    <div className="flex gap-3">
      <div className="flex flex-col items-center">
        <div className={`w-2.5 h-2.5 rounded-full shrink-0 mt-1.5 ${style.dot}`} />
        <div className="w-px flex-1 bg-gray-100 mt-1" />
      </div>
      <div className="flex-1 mb-3 rounded-lg border border-gray-100 bg-white p-3 shadow-sm">
        <div className="flex items-start justify-between gap-2 flex-wrap">
          <div className="flex items-center gap-1.5 flex-wrap">
            <span className="text-base">{style.icon}</span>
            <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${style.badge}`}>
              {event.event_category}
            </span>
            <span className="text-xs font-mono text-gray-600">{event.event_type}</span>
            <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${severityStyle}`}>
              {event.severity}
            </span>
            {event.source_service && (
              <span className="text-xs text-gray-400">from {event.source_service}</span>
            )}
          </div>
          <div className="text-right shrink-0">
            <div className="text-xs font-mono text-gray-600">
              {formatTime(event.timestamp, systemTimezone)}
            </div>
          </div>
        </div>

        <div className="mt-2 flex flex-wrap gap-2">
          {Object.entries(payload).slice(0, 6).map(([k, v]) => v != null && (
            <span key={k} className="text-xs text-gray-600">
              <span className="text-gray-400">{k}:</span> <span className="font-medium">{String(v)}</span>
            </span>
          ))}
        </div>

        {(Object.keys(metadata).length > 0 || event.tags?.length > 0) && (
          <>
            <button
              onClick={() => setExpanded(e => !e)}
              className="mt-1.5 text-xs text-blue-500 hover:text-blue-700"
            >
              {expanded ? '▲ less' : '▼ more'}
            </button>
            {expanded && (
              <div className="mt-2 pt-2 border-t border-gray-100 space-y-1">
                {Object.entries(metadata).map(([k, v]) => v != null && (
                  <div key={k} className="text-xs text-gray-500">
                    <span className="text-gray-400">{k}:</span> {String(v)}
                  </div>
                ))}
                {event.tags?.length > 0 && (
                  <div className="flex gap-1 flex-wrap mt-1">
                    {event.tags.map(tag => (
                      <span key={tag} className="text-xs px-1.5 py-0.5 bg-gray-100 text-gray-500 rounded-full">{tag}</span>
                    ))}
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
}

const EVENT_CATEGORIES = ['ALL', 'TRADING', 'POSITION', 'RISK', 'TRADING_SIGNAL', 'STRATEGY', 'MARKET']

function MongoTimelineTab({ symbol, t, systemTimezone }) {
  const today = todayInTz(systemTimezone)
  const [date, setDate] = useState(today)
  const [category, setCategory] = useState('ALL')

  const { data: eventsData, loading } = useTimelineEvents(symbol, {
    startDate: date,
    endDate: date,
    eventCategory: category === 'ALL' ? undefined : category,
    limit: 200,
  })
  const { data: summaryData } = useTimelineSummary(symbol, date)

  const events = eventsData?.data ?? []
  const summary = summaryData?.summary

  return (
    <div className="space-y-4">
      {/* Controls */}
      <div className="flex flex-wrap gap-3 items-center">
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">{t('dateLabel')}</label>
          <input
            type="date"
            value={date}
            max={today}
            onChange={e => setDate(e.target.value)}
            className="px-3 py-1.5 text-sm border border-gray-300 rounded-lg bg-white focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">{t('categoryLabel')}</label>
          <div className="flex gap-1 flex-wrap">
            {EVENT_CATEGORIES.map(cat => (
              <button
                key={cat}
                onClick={() => setCategory(cat)}
                className={`px-2.5 py-1 text-xs font-medium rounded-lg border transition-colors ${
                  category === cat
                    ? 'bg-blue-600 text-white border-blue-600'
                    : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'
                }`}
              >
                {cat === 'ALL' ? t('allCategories') : cat}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Daily summary strip */}
      {summary && (
        <div className="rounded-lg border border-gray-100 bg-gray-50 p-3 flex flex-wrap gap-4 text-sm">
          <span><span className="text-gray-500">{t('totalEvents')}: </span><strong>{summary.total_events}</strong></span>
          <span><span className="text-gray-500">{t('ordersPlaced')}: </span><strong>{summary.trading_metrics?.orders_placed ?? 0}</strong></span>
          <span><span className="text-gray-500">{t('filled')}: </span><strong>{summary.trading_metrics?.orders_filled ?? 0}</strong></span>
          <span><span className="text-gray-500">{t('riskApproved')}: </span><strong className="text-green-600">{summary.risk?.approvals ?? 0}</strong></span>
          <span><span className="text-gray-500">{t('riskRejected')}: </span><strong className="text-red-500">{summary.risk?.rejections ?? 0}</strong></span>
          <span><span className="text-gray-500">{t('signalsStrong')}: </span><strong className="text-indigo-600">{summary.signals?.strong ?? 0}</strong></span>
        </div>
      )}

      {/* Export link */}
      {events.length > 0 && (
        <div className="text-right">
          <a
            href={`/api/timeline/export?symbol=${symbol}&startDate=${date}&endDate=${date}&format=csv`}
            className="text-xs text-blue-600 hover:text-blue-800 font-medium"
            download
          >
            ↓ {t('exportCsv')}
          </a>
        </div>
      )}

      {/* Event list */}
      {loading && events.length === 0 && (
        <div className="text-center py-10 text-gray-400">{t('loading')}</div>
      )}
      {!loading && events.length === 0 && (
        <div className="text-center py-10 text-gray-400">
          <p className="text-3xl mb-2">📭</p>
          <p>{t('noMongoEvents')}</p>
        </div>
      )}
      {events.length > 0 && (
        <div className="rounded-xl border border-gray-100 bg-white p-4 shadow-sm">
          <p className="text-xs text-gray-400 mb-4">{eventsData?.total ?? events.length} {t('eventsFound')}</p>
          {events.map((event, i) => (
            <MongoEventItem key={event.id ?? i} event={event} systemTimezone={systemTimezone} />
          ))}
        </div>
      )}
    </div>
  )
}

function getRuleResultStyle(result) {
  if (result === 'Pass') return 'text-green-700 bg-green-50'
  if (result === 'Fail') return 'text-red-700 bg-red-50'
  if (result === 'Adjusted') return 'text-yellow-700 bg-yellow-50'
  return 'text-gray-600 bg-gray-50'
}

function RiskEvaluationDetails({ details, t }) {
  const rules = details?.ruleResults ?? []
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-3 gap-3 text-sm">
        <div>
          <span className="text-gray-500">{t('requestedQty')}: </span>
          <span className="font-medium">{details?.requestedQuantity ?? '—'}</span>
        </div>
        <div>
          <span className="text-gray-500">{t('adjustedQty')}: </span>
          <span className="font-medium">{details?.adjustedQuantity ?? '—'}</span>
        </div>
        <div>
          <span className="text-gray-500">{t('latency')}: </span>
          <span className="font-medium">{details?.latencyMs != null ? `${details.latencyMs}ms` : '—'}</span>
        </div>
      </div>
      {details?.finalReason && (
        <div className="text-sm text-red-600 bg-red-50 rounded px-3 py-2">
          <span className="font-medium">{t('reason')}: </span>{details.finalReason}
        </div>
      )}
      {rules.length > 0 && (
        <div>
          <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">{t('ruleResults')}</p>
          <div className="space-y-1">
            {rules.map((rule, i) => (
              <div key={i} className="flex items-start gap-2 text-xs rounded px-2 py-1.5 bg-white border border-gray-100">
                <span className="text-gray-400 w-4 shrink-0 text-right">{rule.sequenceOrder ?? i + 1}.</span>
                <span className="font-medium text-gray-700 w-36 shrink-0">{rule.ruleName}</span>
                <span className={`px-1.5 py-0.5 rounded text-xs font-semibold shrink-0 ${getRuleResultStyle(rule.result)}`}>
                  {rule.result}
                </span>
                {rule.reasonMessage && <span className="text-gray-500 flex-1">{rule.reasonMessage}</span>}
                {(rule.actualValue || rule.thresholdValue) && (
                  <span className="text-gray-400 shrink-0">
                    {rule.actualValue}{rule.thresholdValue ? ` / ${rule.thresholdValue}` : ''}
                  </span>
                )}
                {rule.durationMs != null && (
                  <span className="text-gray-300 shrink-0">{rule.durationMs.toFixed(1)}ms</span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function OrderDetails({ details, t }) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 text-sm">
      <div>
        <span className="text-gray-500">{t('quantity')}: </span>
        <span className="font-medium">{details?.quantity ?? '—'}</span>
      </div>
      <div>
        <span className="text-gray-500">{t('filledAt')}: </span>
        <span className="font-medium">{details?.filledPrice ?? '—'}</span>
      </div>
      <div>
        <span className="text-gray-500">{t('filledQty')}: </span>
        <span className="font-medium">{details?.filledQty ?? '—'}</span>
      </div>
      <div>
        <span className="text-gray-500">{t('stopLoss')}: </span>
        <span className="font-medium text-red-600">{details?.stopLoss ?? '—'}</span>
      </div>
      <div>
        <span className="text-gray-500">{t('takeProfit')}: </span>
        <span className="font-medium text-green-600">{details?.takeProfit ?? '—'}</span>
      </div>
      <div>
        <span className="text-gray-500">{t('strategy')}: </span>
        <span className="font-medium">{details?.strategy ?? '—'}</span>
      </div>
      <div>
        <span className="text-gray-500">{t('status')}: </span>
        <span className={`font-medium ${details?.status === 'OPEN' ? 'text-blue-600' : details?.status === 'CLOSED' ? 'text-gray-600' : 'text-red-600'}`}>
          {details?.status ?? '—'}
        </span>
      </div>
      {details?.isPaper && (
        <div className="col-span-2 sm:col-span-3">
          <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-amber-100 text-amber-700 text-xs rounded-full font-medium">
            🧪 {t('paperTrade')}
          </span>
        </div>
      )}
      {details?.errorMessage && (
        <div className="col-span-2 sm:col-span-3 text-red-600 bg-red-50 rounded px-3 py-2">
          <span className="font-medium">{t('reason')}: </span>{details.errorMessage}
        </div>
      )}
    </div>
  )
}

function TimelineEventItem({ event, index, expanded, onToggle, t, systemTimezone }) {
  const style = getEventStyle(event.eventType, event.outcome, event.side)
  const outcomeLabel = t(`outcome.${event.outcome}`, { defaultValue: event.outcome })
  const typeLabel = t(`eventType.${event.eventType}`, { defaultValue: event.eventType })

  return (
    <div className="flex gap-4">
      {/* Left: dot + line */}
      <div className="flex flex-col items-center">
        <div className={`w-3 h-3 rounded-full shrink-0 mt-1.5 ${style.dot}`} />
        <div className="w-px flex-1 bg-gray-200 mt-1" />
      </div>

      {/* Right: card */}
      <div className={`flex-1 mb-4 rounded-xl border p-4 ${style.border} ${style.bg}`}>
        {/* Header row */}
        <div className="flex items-start justify-between gap-2 flex-wrap">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-lg">{style.icon}</span>
            <span className={`text-xs px-2 py-0.5 rounded-full font-semibold ${style.badge}`}>{typeLabel}</span>
            <span className={`text-xs px-2 py-0.5 rounded-full font-semibold ${style.badge}`}>{outcomeLabel}</span>
            {event.side && (
              <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                event.side.toLowerCase() === 'buy' ? 'bg-blue-100 text-blue-700' : 'bg-purple-100 text-purple-700'
              }`}>
                {t(`side.${event.side}`, { defaultValue: event.side })}
              </span>
            )}
          </div>
          <div className="text-right shrink-0">
            <div className="text-xs font-mono text-gray-600">
              {formatTime(event.timestampUtc, systemTimezone, { seconds: true })}
            </div>
            <div className="text-xs text-gray-400">{timeAgo(event.timestampUtc)}</div>
          </div>
        </div>

        {/* Summary */}
        <p className="mt-2 text-sm text-gray-700">{event.summary}</p>

        {/* Expand/collapse toggle */}
        <button
          onClick={() => onToggle(index)}
          className="mt-2 text-xs text-blue-600 hover:text-blue-800 font-medium"
        >
          {expanded ? t('hideDetails') : t('showDetails')}
        </button>

        {/* Expanded details */}
        {expanded && (
          <div className="mt-3 pt-3 border-t border-gray-200">
            {event.eventType === 'RISK_EVALUATION'
              ? <RiskEvaluationDetails details={event.details} t={t} />
              : <OrderDetails details={event.details} t={t} />
            }
          </div>
        )}
      </div>
    </div>
  )
}

function PriceSummaryCard({ priceSummary, symbol, t, systemTimezone }) {
  if (!priceSummary) return null
  const isUp = priceSummary.closePrice != null && priceSummary.openPrice != null
    && priceSummary.closePrice >= priceSummary.openPrice
  const changePercent = priceSummary.openPrice && priceSummary.closePrice
    ? (((priceSummary.closePrice - priceSummary.openPrice) / priceSummary.openPrice) * 100).toFixed(2)
    : null

  return (
    <div
      className="rounded-xl p-4"
      style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}
    >
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide">{t('priceSummary')}</h2>
        <span className="text-xs text-gray-400">{symbol}</span>
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-5 gap-4">
        <div>
          <p className="text-xs text-gray-500">{t('open')}</p>
          <p className="text-sm font-semibold text-gray-800">{priceSummary.openPrice?.toFixed(4) ?? '—'}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500">{t('high')}</p>
          <p className="text-sm font-semibold text-green-600">{priceSummary.highPrice?.toFixed(4) ?? '—'}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500">{t('low')}</p>
          <p className="text-sm font-semibold text-red-500">{priceSummary.lowPrice?.toFixed(4) ?? '—'}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500">{t('close')}</p>
          <p className={`text-sm font-semibold ${isUp ? 'text-green-600' : 'text-red-500'}`}>
            {priceSummary.closePrice?.toFixed(4) ?? '—'}
            {changePercent && (
              <span className="ml-1 text-xs">({isUp ? '+' : ''}{changePercent}%)</span>
            )}
          </p>
        </div>
        <div>
          <p className="text-xs text-gray-500">{t('ticks')}</p>
          <p className="text-sm font-semibold text-gray-600">{priceSummary.totalTicks?.toLocaleString() ?? '—'}</p>
        </div>
      </div>
    </div>
  )
}

export function SymbolTimelinePage() {
  const { t } = useTranslation('timeline')
  const { systemTimezone } = useSettings()
  const { data: riskConfig } = useRiskConfig()

  const [symbol, setSymbol] = useState('BTCUSDT')
  const [minutesBack, setMinutesBack] = useState(60)
  const [expandedEvents, setExpandedEvents] = useState({})
  const [activeTab, setActiveTab] = useState('live')  // 'live' | 'mongo'

  const { data, loading, lastUpdated, refresh } = useSymbolTimeline(symbol, minutesBack)

  const allowedSymbols = riskConfig?.allowedSymbols ?? []

  const toggleEvent = (index) => {
    setExpandedEvents(prev => ({ ...prev, [index]: !prev[index] }))
  }

  const events = data?.events ?? []
  const stats = data?.stats
  const priceSummary = data?.priceSummary

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
          <p className="text-sm text-gray-500 mt-0.5">{t('subtitle')}</p>
          {lastUpdated && activeTab === 'live' && (
            <p className="text-xs text-gray-400 mt-0.5">
              {formatDateTime(lastUpdated, systemTimezone)}
            </p>
          )}
        </div>
        {activeTab === 'live' && (
          <button
            onClick={refresh}
            disabled={!symbol || loading}
            className="px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors disabled:opacity-50"
          >
            {loading ? t('refreshing') : '↻ Refresh'}
          </button>
        )}
      </div>

      {/* Symbol selector (shared) */}
      <div
        className="rounded-xl p-4 flex flex-wrap gap-4 items-end"
        style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}
      >
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">{t('selectSymbol')}</label>
          <select
            value={symbol}
            onChange={e => { setSymbol(e.target.value); setExpandedEvents({}) }}
            className="px-3 py-2 text-sm border border-gray-300 rounded-lg bg-white focus:outline-none focus:ring-2 focus:ring-blue-500 min-w-[140px]"
          >
            <option value="">— {t('selectSymbol')} —</option>
            {allowedSymbols.map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>

        {/* Tab switcher */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">{t('viewMode')}</label>
          <div className="flex rounded-lg border border-gray-200 overflow-hidden">
            <button
              onClick={() => setActiveTab('live')}
              className={`px-4 py-1.5 text-xs font-medium transition-colors ${
                activeTab === 'live'
                  ? 'bg-blue-600 text-white'
                  : 'bg-white text-gray-600 hover:bg-gray-50'
              }`}
            >
              {t('tabLive')}
            </button>
            <button
              onClick={() => setActiveTab('mongo')}
              className={`px-4 py-1.5 text-xs font-medium transition-colors border-l border-gray-200 ${
                activeTab === 'mongo'
                  ? 'bg-blue-600 text-white'
                  : 'bg-white text-gray-600 hover:bg-gray-50'
              }`}
            >
              {t('tabHistory')}
            </button>
          </div>
        </div>

        {/* Time window (live tab only) */}
        {activeTab === 'live' && (
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">{t('timeWindow')}</label>
            <div className="flex gap-1">
              {TIME_WINDOWS.map(w => (
                <button
                  key={w.value}
                  onClick={() => { setMinutesBack(w.value); setExpandedEvents({}) }}
                  className={`px-3 py-1.5 text-xs font-medium rounded-lg border transition-colors ${
                    minutesBack === w.value
                      ? 'bg-blue-600 text-white border-blue-600'
                      : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'
                  }`}
                >
                  {t(`timeLabels.${w.labelKey}`)}
                </button>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Empty state */}
      {!symbol && (
        <div className="text-center py-16 text-gray-400">
          <p className="text-5xl mb-3">⏱️</p>
          <p className="text-lg">{t('noSelection')}</p>
        </div>
      )}

      {/* MongoDB history tab */}
      {symbol && activeTab === 'mongo' && (
        <MongoTimelineTab symbol={symbol} t={t} systemTimezone={systemTimezone} />
      )}

      {/* Live tab content */}
      {symbol && activeTab === 'live' && data && (
        <>
          <PriceSummaryCard priceSummary={priceSummary} symbol={symbol} t={t} systemTimezone={systemTimezone} />

          {stats && (
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
              <StatCard title={t('stats.evaluations')} value={stats.totalEvaluations}   icon="🛡️" colorClass="text-purple-600" />
              <StatCard title={t('stats.approved')}    value={stats.approvedEvaluations} icon="✅" colorClass="text-green-600" />
              <StatCard title={t('stats.rejected')}    value={stats.rejectedEvaluations} icon="❌" colorClass="text-red-600" />
              <StatCard title={t('stats.orders')}      value={stats.totalOrders}         icon="📋" colorClass="text-blue-600" />
              <StatCard title={t('stats.buys')}        value={stats.buyOrders}           icon="📈" colorClass="text-blue-500" />
              <StatCard title={t('stats.sells')}       value={stats.sellOrders}          icon="📉" colorClass="text-purple-500" />
            </div>
          )}

          <div className="rounded-lg px-4 py-3 bg-amber-50 border border-amber-200 text-xs text-amber-700">
            ℹ️ {t('note')}
          </div>

          <div
            className="rounded-xl p-6"
            style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}
          >
            {events.length === 0 ? (
              <div className="text-center py-10 text-gray-400">
                <p className="text-4xl mb-2">📭</p>
                <p>{t('noData')}</p>
              </div>
            ) : (
              <div>
                {events.map((event, index) => (
                  <TimelineEventItem
                    key={index}
                    event={event}
                    index={index}
                    expanded={!!expandedEvents[index]}
                    onToggle={toggleEvent}
                    t={t}
                    systemTimezone={systemTimezone}
                  />
                ))}
                <div className="flex gap-4">
                  <div className="w-3 h-3 rounded-full bg-gray-300 shrink-0" />
                  <div className="text-xs text-gray-400 pb-2">{t('timeWindow')}: {t(`timeLabels.${String(minutesBack)}`)}</div>
                </div>
              </div>
            )}
          </div>
        </>
      )}

      {symbol && activeTab === 'live' && loading && !data && (
        <div className="text-center py-16 text-gray-400">
          <p className="text-lg">{t('loading')}</p>
        </div>
      )}
    </div>
  )
}
