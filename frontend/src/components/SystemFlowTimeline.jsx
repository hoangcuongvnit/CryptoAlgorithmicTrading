import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useRiskStats, useNotifierStats, useTradingStats } from '../hooks/useDashboard.js'

const STEP_IDS = ['ingestor', 'analyzer', 'strategy', 'riskguard', 'executor', 'notifier']
const STEP_ICONS = {
  ingestor: '📡',
  analyzer: '🔬',
  strategy: '🧠',
  riskguard: '🛡️',
  executor: '⚡',
  notifier: '🔔',
}
const STEP_COLORS = {
  ingestor: '#3b82f6',
  analyzer: '#8b5cf6',
  strategy: '#f59e0b',
  riskguard: '#ef4444',
  executor: '#10b981',
  notifier: '#6366f1',
}

export function SystemFlowTimeline() {
  const { t } = useTranslation('guidance')
  const [tooltip, setTooltip] = useState(null)
  const { data: risk } = useRiskStats()
  const { data: notifier } = useNotifierStats()
  const { data: trading } = useTradingStats()

  const totalSignals = (risk?.todayApproved ?? 0) + (risk?.todayRejected ?? 0)

  const stepMetrics = {
    ingestor: totalSignals > 0 ? t('flow.metrics.active') : null,
    analyzer: totalSignals > 0 ? t('flow.metrics.active') : null,
    strategy: totalSignals > 0 ? t('flow.metrics.signalsSent', { count: totalSignals }) : null,
    riskguard: risk ? t('flow.metrics.riskChecked', { approved: risk.todayApproved, rejected: risk.todayRejected }) : null,
    executor: trading ? t('flow.metrics.tradesExecuted', { count: trading.totalTrades }) : null,
    notifier: notifier ? t('flow.metrics.alertsSent', { count: notifier.todayTotal }) : null,
  }

  const steps = STEP_IDS.map(id => ({
    id,
    icon: STEP_ICONS[id],
    color: STEP_COLORS[id],
    label: t(`flow.steps.${id}.label`),
    desc: t(`flow.steps.${id}.desc`),
    risk: t(`flow.steps.${id}.risk`),
    metric: stepMetrics[id],
  }))

  return (
    <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
      <h2 className="text-lg font-semibold mb-1" style={{ color: '#0f172a' }}>{t('flow.title')}</h2>
      <p className="text-sm text-gray-500 mb-5">
        {t('flow.subtitle')}
        <span className="hidden md:inline"> {t('flow.hoverHint')}</span>
      </p>

      {/* Desktop: horizontal timeline */}
      <div className="hidden md:flex items-stretch gap-1">
        {steps.map((step, i) => (
          <div key={step.id} className="flex items-center flex-1">
            <div
              className="flex-1 relative rounded-xl p-3 text-center cursor-default transition-all hover:shadow-md"
              style={{ border: `1px solid ${step.color}30`, background: `${step.color}08` }}
              onMouseEnter={() => setTooltip(step.id)}
              onMouseLeave={() => setTooltip(null)}
            >
              <div className="text-2xl mb-1">{step.icon}</div>
              <div className="text-xs font-semibold text-gray-800 leading-tight">{step.label}</div>
              <p className="text-xs text-gray-500 mt-1 leading-snug">{step.desc}</p>
              {step.metric && (
                <p className="text-xs font-semibold mt-1.5 leading-snug" style={{ color: step.color }}>{step.metric}</p>
              )}

              {tooltip === step.id && (
                <div
                  className="absolute z-20 bottom-full left-1/2 -translate-x-1/2 mb-2 w-48 rounded-lg p-2.5 text-left shadow-xl pointer-events-none"
                  style={{ background: '#1e293b', color: '#e2e8f0' }}
                >
                  <p className="text-xs font-semibold mb-1" style={{ color: '#fb923c' }}>{t('flow.tooltipTitle')}</p>
                  <p className="text-xs leading-snug">{step.risk}</p>
                  <div
                    className="absolute top-full left-1/2 -translate-x-1/2"
                    style={{ borderLeft: '5px solid transparent', borderRight: '5px solid transparent', borderTop: '5px solid #1e293b' }}
                  />
                </div>
              )}
            </div>
            {i < steps.length - 1 && (
              <div className="text-gray-300 text-xs px-1 shrink-0">→</div>
            )}
          </div>
        ))}
      </div>

      {/* Mobile: vertical timeline */}
      <div className="md:hidden space-y-2">
        {steps.map((step, i) => (
          <div key={step.id}>
            <div
              className="flex items-start gap-3 rounded-lg p-3"
              style={{ background: `${step.color}08`, border: `1px solid ${step.color}30` }}
            >
              <span className="text-xl shrink-0 mt-0.5">{step.icon}</span>
              <div>
                <div className="text-sm font-semibold text-gray-800">{step.label}</div>
                <p className="text-xs text-gray-500 mt-0.5 leading-snug">{step.desc}</p>
                {step.metric && (
                  <p className="text-xs font-semibold mt-1 leading-snug" style={{ color: step.color }}>{step.metric}</p>
                )}
                <p className="text-xs mt-1.5 leading-snug" style={{ color: '#ea580c' }}>⚠ {step.risk}</p>
              </div>
            </div>
            {i < steps.length - 1 && (
              <div className="text-center text-gray-300 text-xs py-0.5">↓</div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
