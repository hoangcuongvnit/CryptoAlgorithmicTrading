import { useTranslation } from 'react-i18next'
import { useRiskStats, useNotifierStats } from '../hooks/useDashboard.js'

function getHealth(lastUpdated, error) {
  if (error) return { labelKey: 'error', icon: '🔴', color: '#dc2626', bg: '#fef2f2', border: '#fca5a5' }
  if (!lastUpdated) return { labelKey: 'noData', icon: '⚫', color: '#94a3b8', bg: '#f8fafc', border: '#e2e8f0' }
  const ageMs = Date.now() - lastUpdated.getTime()
  if (ageMs < 2 * 60 * 1000) return { labelKey: 'healthy', icon: '🟢', color: '#16a34a', bg: '#f0fdf4', border: '#86efac' }
  if (ageMs < 10 * 60 * 1000) return { labelKey: 'degraded', icon: '🟡', color: '#d97706', bg: '#fffbeb', border: '#fcd34d' }
  return { labelKey: 'stale', icon: '🔴', color: '#dc2626', bg: '#fef2f2', border: '#fca5a5' }
}

function formatAge(lastUpdated, tFn) {
  if (!lastUpdated) return tFn('health.never')
  const diff = (Date.now() - lastUpdated.getTime()) / 1000
  if (diff < 60) return `${Math.round(diff)}s ago`
  if (diff < 3600) return `${Math.round(diff / 60)}m ago`
  return `${Math.round(diff / 3600)}h ago`
}

function ServiceCard({ name, icon, desc, health, lastUpdated, indirect }) {
  const { t } = useTranslation('guidance')
  const healthLabel = t(`health.status.${health.labelKey}`)

  return (
    <div
      className="rounded-xl p-4"
      style={{ background: health.bg, border: `1px solid ${health.border}` }}
    >
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center gap-2">
          <span className="text-lg">{icon}</span>
          <span className="text-sm font-semibold text-gray-800">{name}</span>
        </div>
        <span className="text-xs font-medium" style={{ color: health.color }}>
          {health.icon} {healthLabel}
        </span>
      </div>
      <p className="text-xs text-gray-500 mb-2 leading-snug">{desc}</p>
      <p className="text-xs text-gray-400">
        {indirect ? t('health.monitoringInferred') : t('health.lastData', { time: formatAge(lastUpdated, t) })}
      </p>
    </div>
  )
}

export function SystemHealthPanel() {
  const { t } = useTranslation('guidance')
  const { lastUpdated: riskUpdated, error: riskError } = useRiskStats()
  const { lastUpdated: notifierUpdated, error: notifierError } = useNotifierStats()

  const riskHealth = getHealth(riskUpdated, riskError)
  const notifierHealth = getHealth(notifierUpdated, notifierError)
  const systemHealth = getHealth(riskUpdated ?? notifierUpdated, riskError && notifierError)

  const services = [
    {
      name: t('health.services.riskGuard.name'),
      icon: '🛡️',
      desc: t('health.services.riskGuard.desc'),
      health: riskHealth,
      lastUpdated: riskUpdated,
      indirect: false,
    },
    {
      name: t('health.services.notifier.name'),
      icon: '🔔',
      desc: t('health.services.notifier.desc'),
      health: notifierHealth,
      lastUpdated: notifierUpdated,
      indirect: false,
    },
    {
      name: t('health.services.ingestorAnalyzerStrategy.name'),
      icon: '📡',
      desc: t('health.services.ingestorAnalyzerStrategy.desc'),
      health: systemHealth,
      lastUpdated: riskUpdated ?? notifierUpdated,
      indirect: true,
    },
    {
      name: t('health.services.executor.name'),
      icon: '⚡',
      desc: t('health.services.executor.desc'),
      health: systemHealth,
      lastUpdated: riskUpdated ?? notifierUpdated,
      indirect: true,
    },
  ]

  const allHealthy = [riskHealth, notifierHealth].every(h => h.labelKey === 'healthy')
  const anyBad = [riskHealth, notifierHealth].some(h => h.labelKey === 'error' || h.labelKey === 'stale')

  return (
    <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
      <div className="flex items-center justify-between flex-wrap gap-2 mb-1">
        <h2 className="text-lg font-semibold" style={{ color: '#0f172a' }}>{t('health.title')}</h2>
        <span className={`text-sm font-medium px-3 py-1 rounded-full ${
          anyBad ? 'bg-red-100 text-red-700' :
          allHealthy ? 'bg-green-100 text-green-700' :
          'bg-yellow-100 text-yellow-700'
        }`}>
          {anyBad ? t('health.actionRequired') : allHealthy ? t('health.allHealthy') : t('health.degraded')}
        </span>
      </div>
      <p className="text-sm text-gray-500 mb-4">{t('health.subtitle')}</p>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        {services.map(s => <ServiceCard key={s.name} {...s} />)}
      </div>

      <div className="mt-4 pt-4 border-t border-gray-100">
        <p className="text-xs text-gray-400 leading-relaxed">{t('health.legend')}</p>
      </div>
    </div>
  )
}
