import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { SafetyLight } from '../components/SafetyLight.jsx'
import { StatCard } from '../components/StatCard.jsx'
import { useRiskStats, useRiskConfig } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'

const V_CHUNK = 20
const V_MAX = 100

function ValidationRow({ v }) {
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
        {v.timestampUtc ? new Date(v.timestampUtc).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false, timeZone: 'UTC' }) : ''}
      </span>
    </div>
  )
}

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
    { label: t('riskConfig.virtualBalance'), value: `$${Number(config.virtualAccountBalance).toLocaleString()}` },
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

      {/* Validation History */}
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
