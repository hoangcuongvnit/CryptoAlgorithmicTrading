import { useTranslation } from 'react-i18next'

const STATUS_CONFIG = {
  pass:    { bg: '#f0fdf4', border: '#86efac', text: '#16a34a', dotBg: '#dcfce7' },
  warning: { bg: '#fffbeb', border: '#fcd34d', text: '#d97706', dotBg: '#fef9c3' },
  block:   { bg: '#fef2f2', border: '#fca5a5', text: '#dc2626', dotBg: '#fee2e2' },
}

const STATUS_ICONS = { pass: '🟢', warning: '🟡', block: '🔴' }

function RuleCard({ rule }) {
  const { t } = useTranslation('guidance')
  const s = STATUS_CONFIG[rule.status] ?? STATUS_CONFIG.pass
  const statusLabel = t(`rules.status.${rule.status}`, { defaultValue: rule.status })

  return (
    <div
      className="rounded-xl p-4 flex flex-col"
      style={{ background: s.bg, border: `1px solid ${s.border}` }}
    >
      <div className="flex items-start justify-between gap-2 mb-2">
        <div className="flex items-center gap-2">
          <span className="text-lg">{rule.icon}</span>
          <span className="text-sm font-semibold text-gray-800">{rule.label}</span>
        </div>
        <span
          className="text-xs font-medium px-2 py-0.5 rounded-full shrink-0"
          style={{ color: s.text, background: s.dotBg }}
        >
          {STATUS_ICONS[rule.status]} {statusLabel}
        </span>
      </div>

      <p className="text-xs text-gray-600 leading-snug mb-3 flex-1">{rule.meaning}</p>

      <div className="space-y-1 text-xs">
        <div className="flex justify-between">
          <span className="text-gray-400">{t('rules.configured')}</span>
          <span className="font-medium text-gray-700">{rule.configuredValue}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-gray-400">{t('rules.current')}</span>
          <span className="font-medium" style={{ color: s.text }}>{rule.currentValue}</span>
        </div>
      </div>

      <p className="text-xs mt-2.5 pt-2.5 border-t border-gray-200 text-gray-500 leading-snug">
        <span className="font-medium">{t('rules.ifViolated')} </span>{rule.impact}
      </p>
    </div>
  )
}

export function RuleStatusGrid({ config, riskStats }) {
  const { t } = useTranslation('guidance')

  if (!config) {
    return (
      <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
        <h2 className="text-lg font-semibold mb-4" style={{ color: '#0f172a' }}>{t('rules.title')}</h2>
        <div className="text-center py-8 text-gray-400 text-sm">{t('rules.loading')}</div>
      </div>
    )
  }

  const drawdownUsed = Number(riskStats?.drawdownUsedPercent ?? 0)
  const drawdownMax = Number(config.maxDrawdownPercent ?? 10)
  const drawdownRatio = drawdownMax > 0 ? drawdownUsed / drawdownMax : 0
  const isPaper = config.paperTradingOnly !== false

  const rules = [
    {
      icon: '📉',
      label: t('rules.maxDrawdown.label'),
      meaning: t('rules.maxDrawdown.meaning'),
      configuredValue: `${drawdownMax}${t('rules.maxDrawdown.configSuffix')}`,
      currentValue: riskStats ? t('rules.maxDrawdown.currentUsed', { pct: drawdownUsed.toFixed(1) }) : t('rules.noData'),
      status: drawdownRatio >= 1 ? 'block' : drawdownRatio >= 0.8 ? 'warning' : 'pass',
      impact: t('rules.maxDrawdown.impact'),
    },
    {
      icon: '📐',
      label: t('rules.maxPositionSize.label'),
      meaning: t('rules.maxPositionSize.meaning'),
      configuredValue: `${config.maxPositionSizePercent ?? '—'}${t('rules.maxPositionSize.configSuffix')}`,
      currentValue: t('rules.maxPositionSize.currentValue'),
      status: 'pass',
      impact: t('rules.maxPositionSize.impact'),
    },
    {
      icon: '⚖️',
      label: t('rules.minRiskReward.label'),
      meaning: t('rules.minRiskReward.meaning'),
      configuredValue: `${config.minRiskReward ?? '—'}${t('rules.minRiskReward.configSuffix')}`,
      currentValue: t('rules.minRiskReward.currentValue'),
      status: 'pass',
      impact: t('rules.minRiskReward.impact'),
    },
    {
      icon: '⏳',
      label: t('rules.cooldown.label'),
      meaning: t('rules.cooldown.meaning'),
      configuredValue: `${config.cooldownSeconds ?? '—'}${t('rules.cooldown.configSuffix')}`,
      currentValue: t('rules.cooldown.currentValue'),
      status: 'pass',
      impact: t('rules.cooldown.impact'),
    },
    {
      icon: isPaper ? '🧪' : '💰',
      label: t('rules.tradingMode.label'),
      meaning: isPaper ? t('rules.tradingMode.paperMeaning') : t('rules.tradingMode.liveMeaning'),
      configuredValue: isPaper ? t('rules.tradingMode.paperConfig') : t('rules.tradingMode.liveConfig'),
      currentValue: isPaper ? t('rules.tradingMode.paperCurrent') : t('rules.tradingMode.liveCurrent'),
      status: isPaper ? 'pass' : 'warning',
      impact: isPaper ? t('rules.tradingMode.paperImpact') : t('rules.tradingMode.liveImpact'),
    },
    {
      icon: '💵',
      label: t('rules.maxOrderValue.label'),
      meaning: t('rules.maxOrderValue.meaning'),
      configuredValue: `$${config.maxOrderNotional ?? '—'}${t('rules.maxOrderValue.configSuffix')}`,
      currentValue: t('rules.maxOrderValue.currentValue'),
      status: 'pass',
      impact: t('rules.maxOrderValue.impact'),
    },
  ]

  return (
    <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
      <h2 className="text-lg font-semibold mb-1" style={{ color: '#0f172a' }}>{t('rules.title')}</h2>
      <p className="text-sm text-gray-500 mb-4">{t('rules.subtitle')}</p>
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        {rules.map(rule => <RuleCard key={rule.label} rule={rule} />)}
      </div>
    </div>
  )
}
