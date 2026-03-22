import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useActivities, useRiskStats, useRiskConfig } from '../hooks/useDashboard.js'
import { SystemFlowTimeline } from '../components/SystemFlowTimeline.jsx'
import { RuleStatusGrid } from '../components/RuleStatusGrid.jsx'
import { AdvancedRulesSection } from '../components/AdvancedRulesSection.jsx'
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

      {/* Section B2: Advanced Signal Quality & Safety Rules (Phases 1–4) */}
      <AdvancedRulesSection />

      {/* Section B3: Session-Locked Campaign Rules */}
      <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
        <div className="flex flex-wrap items-start justify-between gap-3 mb-4">
          <div>
            <h2 className="text-lg font-semibold mb-1" style={{ color: '#0f172a' }}>{t('campaign.title')}</h2>
            <p className="text-sm text-gray-500">{t('campaign.subtitle')}</p>
          </div>
          <span className="text-xs font-semibold px-2.5 py-1 rounded-full" style={{ color: '#155e75', background: '#ecfeff', border: '1px solid #a5f3fc' }}>
            {t('campaign.badge')}
          </span>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          <div>
            <h3 className="text-sm font-semibold mb-2" style={{ color: '#0f172a' }}>{t('campaign.sessionTableTitle')}</h3>
            <div className="overflow-x-auto rounded-lg border border-gray-200">
              <table className="min-w-full text-sm">
                <thead className="bg-gray-50 text-gray-600">
                  <tr>
                    <th className="text-left px-3 py-2 font-medium">{t('campaign.table.session')}</th>
                    <th className="text-left px-3 py-2 font-medium">{t('campaign.table.window')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 text-gray-700">
                  {[1, 2, 3, 4, 5, 6].map(index => (
                    <tr key={index}>
                      <td className="px-3 py-2">{t(`campaign.sessions.s${index}.label`)}</td>
                      <td className="px-3 py-2">{t(`campaign.sessions.s${index}.time`)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div>
            <h3 className="text-sm font-semibold mb-2" style={{ color: '#0f172a' }}>{t('campaign.mustHaveTitle')}</h3>
            <ul className="space-y-2 text-sm text-gray-700 list-disc pl-5">
              {[1, 2, 3, 4, 5].map(item => (
                <li key={item}>{t(`campaign.mustHave.${item}`)}</li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mt-5 pt-4 border-t border-gray-100">
          <h3 className="text-sm font-semibold mb-2" style={{ color: '#0f172a' }}>{t('campaign.documentsTitle')}</h3>
          <p className="text-xs text-gray-500 mb-2">{t('campaign.documentsHint')}</p>
          <div className="flex flex-wrap gap-2">
            {[1, 2, 3, 4].map(item => (
              <span
                key={item}
                className="text-xs px-2.5 py-1 rounded-md border border-gray-200 bg-gray-50 text-gray-700"
              >
                {t(`campaign.documents.items.${item}`)}
              </span>
            ))}
          </div>
        </div>
      </div>

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
