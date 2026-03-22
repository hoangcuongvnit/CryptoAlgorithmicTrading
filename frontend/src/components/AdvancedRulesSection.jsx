import { useTranslation } from 'react-i18next'

const PHASE_COLORS = {
  1: { accent: '#2563eb', bg: '#eff6ff', border: '#bfdbfe', badge: '#dbeafe', badgeText: '#1d4ed8' },
  2: { accent: '#7c3aed', bg: '#f5f3ff', border: '#ddd6fe', badge: '#ede9fe', badgeText: '#6d28d9' },
  3: { accent: '#0891b2', bg: '#ecfeff', border: '#a5f3fc', badge: '#cffafe', badgeText: '#0e7490' },
  4: { accent: '#059669', bg: '#ecfdf5', border: '#a7f3d0', badge: '#d1fae5', badgeText: '#047857' },
}

const STATUS_STYLES = {
  active:   { bg: '#f0fdf4', border: '#86efac', dot: '🟢', text: '#15803d' },
  config:   { bg: '#fefce8', border: '#fde68a', dot: '🟡', text: '#a16207' },
  disabled: { bg: '#f8fafc', border: '#e2e8f0', dot: '⚪', text: '#64748b' },
}

function RuleChip({ ruleKey, status }) {
  const { t } = useTranslation('guidance')
  const s = STATUS_STYLES[status] ?? STATUS_STYLES.disabled
  const label = t(`advancedRules.rules.${ruleKey}.label`)
  const note  = t(`advancedRules.rules.${ruleKey}.note`)

  return (
    <div
      className="rounded-lg p-3 flex flex-col gap-1"
      style={{ background: s.bg, border: `1px solid ${s.border}` }}
    >
      <div className="flex items-center justify-between gap-1">
        <span className="text-xs font-semibold text-gray-800 leading-tight">{label}</span>
        <span className="text-xs shrink-0" style={{ color: s.text }}>{s.dot}</span>
      </div>
      <p className="text-xs text-gray-500 leading-snug">{note}</p>
    </div>
  )
}

function PhaseCard({ phaseNum, rules }) {
  const { t } = useTranslation('guidance')
  const c = PHASE_COLORS[phaseNum]
  const title    = t(`advancedRules.phases.p${phaseNum}.title`)
  const goal     = t(`advancedRules.phases.p${phaseNum}.goal`)
  const timeline = t(`advancedRules.phases.p${phaseNum}.timeline`)

  return (
    <div
      className="rounded-xl p-4 flex flex-col gap-3"
      style={{ background: c.bg, border: `1px solid ${c.border}` }}
    >
      <div className="flex items-start justify-between gap-2">
        <div>
          <div className="flex items-center gap-2 mb-0.5">
            <span
              className="text-xs font-bold px-2 py-0.5 rounded-full"
              style={{ background: c.badge, color: c.badgeText }}
            >
              {t('advancedRules.phaseLabel', { num: phaseNum })}
            </span>
            <span className="text-xs text-gray-400">{timeline}</span>
          </div>
          <h3 className="text-sm font-semibold" style={{ color: '#0f172a' }}>{title}</h3>
          <p className="text-xs text-gray-500 mt-0.5">{goal}</p>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
        {rules.map(r => <RuleChip key={r.key} ruleKey={r.key} status={r.status} />)}
      </div>
    </div>
  )
}

const PHASES = [
  {
    num: 1,
    rules: [
      { key: 'adx',     status: 'active'  },
      { key: 'volume',  status: 'active'  },
      { key: 'spread',  status: 'active'  },
    ],
  },
  {
    num: 2,
    rules: [
      { key: 'atrSl',      status: 'config'   },
      { key: 'partialTp',  status: 'config'   },
      { key: 'kelly',      status: 'config'   },
      { key: 'shortSafety',status: 'active'   },
    ],
  },
  {
    num: 3,
    rules: [
      { key: 'regime',    status: 'active' },
      { key: 'consensus', status: 'config' },
    ],
  },
  {
    num: 4,
    rules: [
      { key: 'bbSqueeze', status: 'active' },
    ],
  },
]

export function AdvancedRulesSection() {
  const { t } = useTranslation('guidance')

  return (
    <div
      className="rounded-xl p-5"
      style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}
    >
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-3 mb-4">
        <div>
          <h2 className="text-lg font-semibold mb-1" style={{ color: '#0f172a' }}>
            {t('advancedRules.title')}
          </h2>
          <p className="text-sm text-gray-500">{t('advancedRules.subtitle')}</p>
        </div>
        <div className="flex gap-3 flex-wrap text-xs">
          {['active', 'config', 'disabled'].map(s => (
            <span key={s} className="flex items-center gap-1 text-gray-500">
              <span>{STATUS_STYLES[s].dot}</span>
              {t(`advancedRules.legend.${s}`)}
            </span>
          ))}
        </div>
      </div>

      {/* Phase grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {PHASES.map(p => <PhaseCard key={p.num} phaseNum={p.num} rules={p.rules} />)}
      </div>

      {/* Config hint */}
      <p className="text-xs text-gray-400 mt-4 pt-3 border-t border-gray-100">
        {t('advancedRules.configHint')}
      </p>
    </div>
  )
}
