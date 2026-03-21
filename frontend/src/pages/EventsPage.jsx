import { EventTimeline } from '../components/EventTimeline.jsx'
import { StatCard } from '../components/StatCard.jsx'
import { useNotifierStats } from '../hooks/useDashboard.js'

export function EventsPage() {
  const { data: notifier, lastUpdated, refresh } = useNotifierStats()

  const categories = notifier?.todayByCategory ?? {}
  const total = notifier?.todayTotal ?? 0

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-800">Event History</h1>
        <button
          onClick={refresh}
          className="px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
        >
          ↻ Refresh
        </button>
      </div>

      {/* Today's Summary */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatCard title="Total Today" value={total} icon="📋" colorClass="text-gray-800" />
        <StatCard title="Trades" value={categories.order ?? 0} icon="✅" colorClass="text-green-600" />
        <StatCard title="Rejected" value={categories.order_rejected ?? 0} icon="❌" colorClass="text-red-600" />
        <StatCard title="System Events" value={categories.system_event ?? 0} icon="⚙️" colorClass="text-gray-600" />
      </div>

      {/* Legend */}
      <div className="flex flex-wrap gap-3">
        {[
          { icon: '✅', label: 'Trade Executed', bg: 'bg-green-100' },
          { icon: '❌', label: 'Trade Rejected', bg: 'bg-red-100' },
          { icon: '🚀', label: 'System Startup', bg: 'bg-blue-100' },
          { icon: '⚙️', label: 'System Event', bg: 'bg-gray-100' },
        ].map(item => (
          <div key={item.label} className={`flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm ${item.bg}`}>
            <span>{item.icon}</span>
            <span className="text-gray-700">{item.label}</span>
          </div>
        ))}
      </div>

      {/* Full Timeline */}
      <div className="bg-gray-50 rounded-xl p-4">
        {lastUpdated && (
          <p className="text-xs text-gray-400 mb-3">
            Last updated: {lastUpdated.toLocaleTimeString('en-US', { hour12: false, timeZone: 'UTC' })} UTC
          </p>
        )}
        <EventTimeline events={notifier?.recent} maxItems={100} />
      </div>
    </div>
  )
}
