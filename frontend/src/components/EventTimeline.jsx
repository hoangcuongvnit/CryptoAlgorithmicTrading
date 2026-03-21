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

export function EventTimeline({ events, maxItems = 30 }) {
  const { t } = useTranslation('common')
  const { systemTimezone } = useSettings()

  if (!events || events.length === 0) {
    return (
      <div className="text-center py-10 text-gray-400">
        <p className="text-4xl mb-2">📭</p>
        <p>{t('eventTimeline.noEvents')}</p>
      </div>
    )
  }

  const catLabelKey = (category) => ({
    order: 'eventTimeline.catLabel.trade',
    order_rejected: 'eventTimeline.catLabel.rejected',
    startup: 'eventTimeline.catLabel.startup',
    system_event: 'eventTimeline.catLabel.system',
  }[category])

  const items = events.slice(0, maxItems)

  return (
    <div className="space-y-2">
      {items.map((evt, idx) => {
        const style = CATEGORY_STYLE[evt.category] ?? { icon: '📋', bg: 'bg-gray-100', text: 'text-gray-600' }
        const labelKey = catLabelKey(evt.category)
        const label = labelKey ? t(labelKey) : evt.category
        return (
          <div key={idx} className="flex items-start gap-3 p-3 bg-white rounded-lg border border-gray-100 hover:border-gray-200 transition-colors">
            <span className="text-lg mt-0.5">{style.icon}</span>
            <div className="flex-1 min-w-0">
              <p className="text-sm text-gray-800">{evt.summary}</p>
              <div className="flex items-center gap-2 mt-1">
                <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${style.bg} ${style.text}`}>
                  {label}
                </span>
                <span className="text-xs text-gray-400">{formatTime(evt.timestampUtc, systemTimezone, { seconds: true })}</span>
                <span className="text-xs text-gray-300">({timeAgo(evt.timestampUtc)})</span>
              </div>
            </div>
          </div>
        )
      })}
    </div>
  )
}
