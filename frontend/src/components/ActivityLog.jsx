import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'

const CHUNK = 20
const MAX_ROWS = 100

function timeAgo(isoStr) {
  if (!isoStr) return ''
  const diff = (Date.now() - new Date(isoStr).getTime()) / 1000
  if (diff < 60) return `${Math.round(diff)}s ago`
  if (diff < 3600) return `${Math.round(diff / 60)}m ago`
  if (diff < 86400) return `${Math.round(diff / 3600)}h ago`
  return new Date(isoStr).toLocaleDateString()
}

function formatTime(isoStr) {
  if (!isoStr) return ''
  return new Date(isoStr).toLocaleTimeString('en-US', {
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false, timeZone: 'UTC',
  })
}

const CATEGORY_BG = {
  RISK_EVALUATION: { icon: '🛡️', bg: 'bg-purple-100', text: 'text-purple-800' },
  ORDER:           { icon: '📦', bg: 'bg-blue-100',   text: 'text-blue-800'   },
  SYSTEM:          { icon: '⚙️', bg: 'bg-gray-100',   text: 'text-gray-700'   },
  PRICE:           { icon: '💹', bg: 'bg-green-100',  text: 'text-green-800'  },
  SIGNAL:          { icon: '📡', bg: 'bg-yellow-100', text: 'text-yellow-800' },
}

const STATUS_BG = {
  SUCCESS:  { bg: 'bg-green-100', text: 'text-green-700'  },
  REJECTED: { bg: 'bg-red-100',   text: 'text-red-700'    },
  FAILED:   { bg: 'bg-red-100',   text: 'text-red-700'    },
  SKIPPED:  { bg: 'bg-gray-100',  text: 'text-gray-600'   },
}

const SEVERITY_BORDER = {
  INFO:  'border-l-2 border-l-gray-200',
  WARN:  'border-l-2 border-l-orange-400',
  ERROR: 'border-l-2 border-l-red-500',
}

function ActivityRow({ event }) {
  const { t } = useTranslation('common')
  const [expanded, setExpanded] = useState(false)

  const catStyle = CATEGORY_BG[event.category] ?? CATEGORY_BG.SYSTEM
  const statusStyle = STATUS_BG[event.status] ?? STATUS_BG.SUCCESS
  const severityClass = SEVERITY_BORDER[event.severity] ?? SEVERITY_BORDER.INFO
  const hasDetails = Boolean(event.details)

  const catLabelKey = {
    RISK_EVALUATION: 'activityLog.catLabel.riskCheck',
    ORDER: 'activityLog.catLabel.order',
    SYSTEM: 'activityLog.catLabel.system',
    PRICE: 'activityLog.catLabel.price',
    SIGNAL: 'activityLog.catLabel.signal',
  }[event.category] ?? 'activityLog.catLabel.system'

  const statusLabelKey = `activityLog.statusLabel.${event.status}`

  return (
    <div
      className={`bg-white rounded-lg ${severityClass} transition-shadow hover:shadow-sm`}
      style={{ border: '1px solid #e8edf4' }}
    >
      <div
        className={`flex items-center gap-3 p-3 ${hasDetails ? 'cursor-pointer' : ''}`}
        onClick={() => hasDetails && setExpanded(e => !e)}
      >
        <span className="text-base shrink-0">{catStyle.icon}</span>

        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${catStyle.bg} ${catStyle.text}`}>
              {t(catLabelKey)}
            </span>
            {event.symbol && (
              <span className="text-xs font-mono font-semibold text-gray-700">{event.symbol}</span>
            )}
            <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${statusStyle.bg} ${statusStyle.text}`}>
              {t(statusLabelKey, { defaultValue: event.status })}
            </span>
            <span className="text-xs text-gray-400 ml-auto shrink-0">
              {formatTime(event.timestampUtc)} UTC · {timeAgo(event.timestampUtc)}
            </span>
          </div>
          <p className="text-sm text-gray-700 mt-1 leading-snug">{event.message}</p>
        </div>

        {hasDetails && (
          <span className="text-gray-300 text-xs shrink-0 ml-1">{expanded ? '▲' : '▼'}</span>
        )}
      </div>

      {expanded && event.details && (
        <div className="px-4 pb-3 border-t border-gray-50">
          <div className="mt-2 bg-gray-50 rounded-lg p-3 text-xs space-y-1.5">
            {event.details.side && (
              <div>
                <span className="text-gray-400 font-medium">{t('activityLog.side')}: </span>
                <span className="text-gray-800">{event.details.side}</span>
              </div>
            )}
            {event.details.approved !== undefined && (
              <div>
                <span className="text-gray-400 font-medium">{t('activityLog.decision')}: </span>
                <span className={event.details.approved ? 'text-green-600 font-semibold' : 'text-red-600 font-semibold'}>
                  {event.details.approved ? t('activityLog.decisionApproved') : t('activityLog.decisionRejected')}
                </span>
              </div>
            )}
            {event.details.rejectionReason && (
              <div>
                <span className="text-gray-400 font-medium">{t('activityLog.reason')}: </span>
                <span className="text-red-700">{event.details.rejectionReason}</span>
              </div>
            )}
            <div className="pt-1.5 border-t border-gray-200 text-gray-400">
              <span className="font-medium">{t('activityLog.service')}: </span>
              <span className="text-gray-600">{event.service}</span>
              <span className="font-medium ml-3">{t('activityLog.action')}: </span>
              <span className="text-gray-600">{event.action}</span>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export function ActivityLog({ activities = [], loading = false }) {
  const { t } = useTranslation('common')
  const [categoryFilter, setCategoryFilter] = useState('ALL')
  const [statusFilter, setStatusFilter] = useState('ALL')
  const [search, setSearch] = useState('')
  const [visible, setVisible] = useState(CHUNK)
  const sentinelRef = useRef(null)

  const filtered = activities.filter(e => {
    if (categoryFilter !== 'ALL' && e.category !== categoryFilter) return false
    if (statusFilter !== 'ALL' && e.status !== statusFilter) return false
    if (search) {
      const q = search.toLowerCase()
      if (
        !e.message?.toLowerCase().includes(q) &&
        !e.symbol?.toLowerCase().includes(q) &&
        !e.service?.toLowerCase().includes(q)
      ) return false
    }
    return true
  })

  // Reset visible count when filters change
  useEffect(() => { setVisible(CHUNK) }, [categoryFilter, statusFilter, search])

  // Load more rows when sentinel scrolls into view
  useEffect(() => {
    const el = sentinelRef.current
    if (!el) return
    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting && visible < Math.min(filtered.length, MAX_ROWS)) {
        setVisible(v => Math.min(v + CHUNK, MAX_ROWS))
      }
    }, { threshold: 0.1 })
    observer.observe(el)
    return () => observer.disconnect()
  }, [visible, filtered.length])

  const displayed = filtered.slice(0, visible)
  const canLoadMore = visible < Math.min(filtered.length, MAX_ROWS)

  return (
    <div className="space-y-3">
      {/* Filter bar */}
      <div className="flex flex-wrap gap-2 items-center">
        <select
          value={categoryFilter}
          onChange={e => setCategoryFilter(e.target.value)}
          className="text-sm px-3 py-1.5 border border-gray-200 rounded-lg bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-200"
        >
          <option value="ALL">{t('activityLog.allCategories')}</option>
          <option value="RISK_EVALUATION">{t('activityLog.riskEvaluation')}</option>
          <option value="ORDER">{t('activityLog.orders')}</option>
          <option value="SYSTEM">{t('activityLog.system')}</option>
        </select>

        <select
          value={statusFilter}
          onChange={e => setStatusFilter(e.target.value)}
          className="text-sm px-3 py-1.5 border border-gray-200 rounded-lg bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-200"
        >
          <option value="ALL">{t('activityLog.allStatuses')}</option>
          <option value="SUCCESS">{t('activityLog.success')}</option>
          <option value="REJECTED">{t('activityLog.rejected')}</option>
          <option value="FAILED">{t('activityLog.failed')}</option>
        </select>

        <input
          type="text"
          placeholder={t('activityLog.searchPlaceholder')}
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="text-sm px-3 py-1.5 border border-gray-200 rounded-lg bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-200 flex-1 min-w-40"
        />

        <span className="text-xs text-gray-400 shrink-0">
          {displayed.length} / {activities.length}
        </span>
      </div>

      {/* Event list */}
      {loading && activities.length === 0 ? (
        <div className="text-center py-10 text-gray-400">
          <p className="text-2xl mb-2">⏳</p>
          <p className="text-sm">{t('activityLog.loadingStream')}</p>
        </div>
      ) : displayed.length === 0 ? (
        <div className="text-center py-10 text-gray-400">
          <p className="text-4xl mb-2">📭</p>
          <p className="text-sm">
            {activities.length === 0
              ? t('activityLog.noEvents')
              : t('activityLog.noEventsMatch')}
          </p>
        </div>
      ) : (
        <div className="space-y-2">
          {displayed.map(evt => <ActivityRow key={evt.eventId} event={evt} />)}
          <div ref={sentinelRef} className="h-1" />
          {canLoadMore && (
            <div className="text-center py-2 text-xs text-gray-400">
              {t('activityLog.loadingMore')}
            </div>
          )}
          {!canLoadMore && filtered.length > MAX_ROWS && (
            <div className="text-center py-2 text-xs text-gray-400">
              {t('activityLog.maxRowsReached', { max: MAX_ROWS })}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
