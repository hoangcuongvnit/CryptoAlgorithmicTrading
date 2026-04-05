import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../context/SettingsContext.jsx'
import { formatTime } from '../utils/dateFormat.js'

function timeAgo(isoStr) {
  if (!isoStr) return ''
  const diff = (Date.now() - new Date(isoStr).getTime()) / 1000
  if (diff < 60) return `${Math.round(diff)}s ago`
  if (diff < 3600) return `${Math.round(diff / 60)}m ago`
  if (diff < 86400) return `${Math.round(diff / 3600)}h ago`
  return new Date(isoStr).toLocaleDateString()
}

const CATEGORY_STYLE = {
  order:        { icon: '✅', bg: 'bg-green-100', text: 'text-green-800' },
  order_rejected: { icon: '❌', bg: 'bg-red-100',   text: 'text-red-800'   },
  startup:      { icon: '🚀', bg: 'bg-blue-100',  text: 'text-blue-800'  },
  system_event: { icon: '⚙️', bg: 'bg-gray-100',  text: 'text-gray-700'  },
}

const CATEGORY_ORDER = ['all', 'order', 'order_rejected', 'startup', 'system_event']

function getCategoryLabelKey(category) {
  return {
    all: 'eventTimeline.catLabel.all',
    order: 'eventTimeline.catLabel.trade',
    order_rejected: 'eventTimeline.catLabel.rejected',
    startup: 'eventTimeline.catLabel.startup',
    system_event: 'eventTimeline.catLabel.system',
  }[category]
}

export function EventTimeline({ events, maxItems = 30 }) {
  const { t } = useTranslation('common')
  const { systemTimezone } = useSettings()
  const [page, setPage] = useState(1)
  const [selectedCategory, setSelectedCategory] = useState('all')
  const [copiedKey, setCopiedKey] = useState('')
  const [expandedKeys, setExpandedKeys] = useState(new Set())

  const PAGE_SIZE = 10

  const sortedItems = useMemo(() => {
    if (!events || events.length === 0) return []
    return [...events]
      .sort((a, b) => new Date(b.timestampUtc) - new Date(a.timestampUtc))
      .slice(0, maxItems)
  }, [events, maxItems])

  const availableCategories = useMemo(() => {
    const categories = new Set(
      sortedItems
        .map(item => item.category)
        .filter(Boolean)
    )

    const orderedCategories = CATEGORY_ORDER.filter(category => category === 'all' || categories.has(category))
    const extraCategories = [...categories].filter(category => !CATEGORY_ORDER.includes(category))
    return [...orderedCategories, ...extraCategories]
  }, [sortedItems])

  const filteredItems = useMemo(() => {
    if (selectedCategory === 'all') return sortedItems
    return sortedItems.filter(item => item.category === selectedCategory)
  }, [sortedItems, selectedCategory])

  const pageCount = Math.max(1, Math.ceil(filteredItems.length / PAGE_SIZE))

  useEffect(() => {
    setPage(1)
  }, [events])

  useEffect(() => {
    setPage(prevPage => Math.min(prevPage, pageCount))
  }, [pageCount])

  useEffect(() => {
    setPage(1)
  }, [selectedCategory])

  useEffect(() => {
    if (!copiedKey) return undefined
    const timer = setTimeout(() => setCopiedKey(''), 1200)
    return () => clearTimeout(timer)
  }, [copiedKey])

  if (sortedItems.length === 0) {
    return (
      <div className="text-center py-10 text-gray-400">
        <p className="text-4xl mb-2">📭</p>
        <p>{t('eventTimeline.noEvents')}</p>
      </div>
    )
  }

  const start = (page - 1) * PAGE_SIZE
  const end = start + PAGE_SIZE
  const items = filteredItems.slice(start, end)

  if (filteredItems.length === 0) {
    return (
      <div className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs font-medium uppercase tracking-wide text-gray-400">{t('eventTimeline.filterLabel')}</span>
          {availableCategories.map(category => {
            const labelKey = getCategoryLabelKey(category)
            return (
              <button
                key={category}
                type="button"
                onClick={() => setSelectedCategory(category)}
                className={`text-xs px-3 py-1.5 rounded-full border transition-colors ${
                  selectedCategory === category
                    ? 'bg-slate-800 text-white border-slate-800'
                    : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                }`}
              >
                {labelKey ? t(labelKey) : category}
              </button>
            )
          })}
        </div>
        <div className="text-center py-10 text-gray-400">
          <p className="text-4xl mb-2">🔎</p>
          <p>{t('eventTimeline.noFilteredEvents')}</p>
        </div>
      </div>
    )
  }

  const handleCopy = async (evt, idx) => {
    const copyText = [evt.summary, evt.message].filter(Boolean).join('\n').trim()
    const rowKey = `${evt.timestampUtc}-${evt.category}-${idx}`
    if (!copyText) return

    try {
      await navigator.clipboard.writeText(copyText)
      setCopiedKey(rowKey)
    } catch {
      setCopiedKey('')
    }
  }

  const toggleExpand = (rowKey) => {
    setExpandedKeys(prev => {
      const next = new Set(prev)
      next.has(rowKey) ? next.delete(rowKey) : next.add(rowKey)
      return next
    })
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium uppercase tracking-wide text-gray-400">{t('eventTimeline.filterLabel')}</span>
        {availableCategories.map(category => {
          const labelKey = getCategoryLabelKey(category)
          return (
            <button
              key={category}
              type="button"
              onClick={() => setSelectedCategory(category)}
              className={`text-xs px-3 py-1.5 rounded-full border transition-colors ${
                selectedCategory === category
                  ? 'bg-slate-800 text-white border-slate-800'
                  : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
              }`}
            >
              {labelKey ? t(labelKey) : category}
            </button>
          )
        })}
      </div>

      <div className="space-y-2">
      {items.map((evt, idx) => {
        const rowKey = `${evt.timestampUtc}-${evt.category}-${start + idx}`
        const style = CATEGORY_STYLE[evt.category] ?? { icon: '📋', bg: 'bg-gray-100', text: 'text-gray-600' }
        const labelKey = getCategoryLabelKey(evt.category)
        const label = labelKey ? t(labelKey) : evt.category
        const isExpanded = expandedKeys.has(rowKey)
        const hasDetail = evt.message && evt.message !== evt.summary
        return (
          <div key={rowKey} className="flex items-start gap-3 p-3 bg-white rounded-lg border border-gray-100 hover:border-gray-200 transition-colors">
            <span className="text-lg mt-0.5">{style.icon}</span>
            <div className="flex-1 min-w-0">
              <p className="text-sm text-gray-800 whitespace-pre-wrap">{evt.summary}</p>
              {isExpanded && hasDetail && (
                <p className="text-xs text-gray-600 mt-1.5 whitespace-pre-wrap bg-gray-50 rounded p-2 border border-gray-100">{evt.message}</p>
              )}
              <div className="flex items-center flex-wrap gap-2 mt-1">
                <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${style.bg} ${style.text}`}>
                  {label}
                </span>
                <span className="text-xs text-gray-400">{formatTime(evt.timestampUtc, systemTimezone, { seconds: true })}</span>
                <span className="text-xs text-gray-300">({timeAgo(evt.timestampUtc)})</span>
                <div className="ml-auto flex gap-1">
                  {hasDetail && (
                    <button
                      type="button"
                      onClick={() => toggleExpand(rowKey)}
                      className="text-xs px-2 py-1 rounded border border-gray-200 text-gray-600 hover:bg-gray-50"
                    >
                      {isExpanded ? t('eventTimeline.collapse') : t('eventTimeline.expand')}
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => handleCopy(evt, start + idx)}
                    className="text-xs px-2 py-1 rounded border border-gray-200 text-gray-600 hover:bg-gray-50"
                  >
                    {copiedKey === rowKey ? t('eventTimeline.copied') : t('eventTimeline.copy')}
                  </button>
                </div>
              </div>
            </div>
          </div>
        )
      })}
      </div>

      {pageCount > 1 && (
        <div className="pt-2 flex items-center justify-center gap-2">
          {Array.from({ length: pageCount }, (_, i) => i + 1).map(p => (
            <button
              key={p}
              type="button"
              onClick={() => setPage(p)}
              className={`text-xs px-3 py-1 rounded border transition-colors ${
                page === p
                  ? 'bg-slate-800 text-white border-slate-800'
                  : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
              }`}
            >
              {t('eventTimeline.page', { page: p })}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
