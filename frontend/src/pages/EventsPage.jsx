import { useTranslation } from 'react-i18next'
import { ActivityLog } from '../components/ActivityLog.jsx'
import { StatCard } from '../components/StatCard.jsx'
import { useActivities } from '../hooks/useDashboard.js'

export function EventsPage() {
  const { t } = useTranslation('events')
  const { activities, loading, lastUpdated, refresh } = useActivities()

  const riskChecks = activities.filter(e => e.category === 'RISK_EVALUATION').length
  const approved   = activities.filter(e => e.category === 'RISK_EVALUATION' && e.status === 'SUCCESS').length
  const rejected   = activities.filter(e => e.status === 'REJECTED').length
  const orders     = activities.filter(e => e.category === 'ORDER' && e.status === 'SUCCESS').length

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
          {lastUpdated && (
            <p className="text-xs text-gray-400 mt-0.5">
              {t('lastUpdated', { time: lastUpdated.toLocaleTimeString('en-US', { hour12: false, timeZone: 'UTC' }) })}
            </p>
          )}
        </div>
        <button
          onClick={refresh}
          className="px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
        >
          {t('refresh')}
        </button>
      </div>

      {/* Summary stats */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatCard title={t('stats.riskChecks')}  value={riskChecks} icon="🛡️" colorClass="text-purple-600" />
        <StatCard title={t('stats.approved')}     value={approved}   icon="✅" colorClass="text-green-600"  />
        <StatCard title={t('stats.rejected')}     value={rejected}   icon="❌" colorClass="text-red-600"    />
        <StatCard title={t('stats.ordersFilled')} value={orders}     icon="📦" colorClass="text-blue-600"   />
      </div>

      {/* Live activity stream */}
      <div
        className="rounded-xl p-4"
        style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}
      >
        <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3">
          {t('liveStream.title')}
        </h2>
        <ActivityLog activities={activities} loading={loading} />
      </div>
    </div>
  )
}
