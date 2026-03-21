import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useActivities, useRiskStats, useRiskConfig } from '../hooks/useDashboard.js'
import { SystemFlowTimeline } from '../components/SystemFlowTimeline.jsx'
import { RuleStatusGrid } from '../components/RuleStatusGrid.jsx'
import { SystemHealthPanel } from '../components/SystemHealthPanel.jsx'
import { QuickStartChecklist } from '../components/QuickStartChecklist.jsx'
import { ActivityLog } from '../components/ActivityLog.jsx'

export function GuidancePage({ onNavigate }) {
  const { t } = useTranslation('guidance')
  const { activities, loading } = useActivities()
  const { data: riskStats } = useRiskStats()
  const { data: riskConfig } = useRiskConfig()

  const [severityFilter, setSeverityFilter] = useState('ALL')
  const [symbolFilter, setSymbolFilter] = useState('ALL')

  const symbols = [...new Set(activities.filter(a => a.symbol).map(a => a.symbol))].sort()

  const filteredActivities = activities.filter(a => {
    if (severityFilter !== 'ALL' && a.severity !== severityFilter) return false
    if (symbolFilter !== 'ALL' && a.symbol !== symbolFilter) return false
    return true
  })

  return (
    <div className="space-y-8">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
        <p className="text-sm text-gray-500 mt-1">{t('subtitle')}</p>
      </div>

      {/* Section A: How the System Works */}
      <SystemFlowTimeline />

      {/* Section B: Rules & Safety */}
      <RuleStatusGrid config={riskConfig} riskStats={riskStats} />

      {/* Section C: Live Activity Now */}
      <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
        <h2 className="text-lg font-semibold mb-1" style={{ color: '#0f172a' }}>{t('liveActivity.title')}</h2>
        <p className="text-sm text-gray-500 mb-4">{t('liveActivity.desc')}</p>

        {/* Severity + symbol filters */}
        <div className="flex flex-wrap gap-2 mb-3 pb-3 border-b border-gray-100">
          <select
            value={severityFilter}
            onChange={e => setSeverityFilter(e.target.value)}
            className="text-sm px-3 py-1.5 border border-gray-200 rounded-lg bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-200"
          >
            <option value="ALL">{t('filters.allSeverities')}</option>
            <option value="INFO">{t('filters.info')}</option>
            <option value="WARN">{t('filters.warning')}</option>
            <option value="ERROR">{t('filters.error')}</option>
          </select>

          <select
            value={symbolFilter}
            onChange={e => setSymbolFilter(e.target.value)}
            className="text-sm px-3 py-1.5 border border-gray-200 rounded-lg bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-200"
          >
            <option value="ALL">{t('filters.allSymbols')}</option>
            {symbols.map(s => (
              <option key={s} value={s}>{s.replace('USDT', '/USDT')}</option>
            ))}
          </select>

          {(severityFilter !== 'ALL' || symbolFilter !== 'ALL') && (
            <button
              onClick={() => { setSeverityFilter('ALL'); setSymbolFilter('ALL') }}
              className="text-xs px-3 py-1.5 rounded-lg text-gray-500 hover:text-gray-700 border border-gray-200 bg-white"
            >
              {t('filters.clearFilters')}
            </button>
          )}
        </div>

        <ActivityLog activities={filteredActivities} loading={loading} />
      </div>

      {/* Section D: System Health */}
      <SystemHealthPanel />

      {/* Section E: What Should I Do Next */}
      <QuickStartChecklist onNavigate={onNavigate} />
    </div>
  )
}
