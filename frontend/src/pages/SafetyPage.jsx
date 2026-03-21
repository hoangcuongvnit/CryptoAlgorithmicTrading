import { SafetyLight } from '../components/SafetyLight.jsx'
import { StatCard } from '../components/StatCard.jsx'
import { useRiskStats, useRiskConfig } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'

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
  const { data: risk } = useRiskStats()
  const { data: config } = useRiskConfig()

  const totalToday = (risk?.todayApproved ?? 0) + (risk?.todayRejected ?? 0)
  const approvalRate = totalToday > 0
    ? Math.round((risk.todayApproved / totalToday) * 100)
    : null

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold text-gray-800">Safety & Risk Guard</h1>

      {/* Main Safety Status */}
      <SafetyLight riskStats={risk} riskConfig={config} />

      {/* Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title="Today's P&L"
          value={risk ? formatPnl(risk.dailyPnl) : '—'}
          icon="💵"
          colorClass={risk ? pnlColorClass(risk.dailyPnl) : 'text-gray-400'}
        />
        <StatCard
          title="Approved"
          value={risk?.todayApproved ?? '—'}
          subtitle="orders passed"
          icon="✅"
          colorClass="text-green-600"
        />
        <StatCard
          title="Rejected"
          value={risk?.todayRejected ?? '—'}
          subtitle="orders blocked"
          icon="🛑"
          colorClass="text-red-600"
        />
        <StatCard
          title="Approval Rate"
          value={approvalRate !== null ? `${approvalRate}%` : '—'}
          subtitle={`of ${totalToday} checks`}
          icon="📊"
          colorClass={approvalRate !== null && approvalRate < 50 ? 'text-red-500' : 'text-gray-800'}
        />
      </div>

      {/* Risk Settings */}
      {config && (
        <div className="bg-white rounded-xl border border-gray-100 shadow-sm p-5">
          <h2 className="text-lg font-semibold text-gray-700 mb-4">Risk Configuration</h2>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
            {[
              { label: 'Min Risk/Reward', value: config.minRiskReward },
              { label: 'Max Position Size', value: `${config.maxPositionSizePercent}%` },
              { label: 'Max Order Value', value: `$${config.maxOrderNotional}` },
              { label: 'Max Daily Loss', value: `${config.maxDrawdownPercent}%` },
              { label: 'Cooldown Period', value: `${config.cooldownSeconds}s` },
              { label: 'Virtual Balance', value: `$${Number(config.virtualAccountBalance).toLocaleString()}` },
            ].map(item => (
              <div key={item.label} className="bg-gray-50 rounded-lg p-3">
                <p className="text-xs text-gray-500">{item.label}</p>
                <p className="text-base font-semibold text-gray-800 mt-0.5">{item.value}</p>
              </div>
            ))}
          </div>
          <div className="mt-4">
            <p className="text-xs text-gray-500 mb-2">Allowed Trading Pairs</p>
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
        <h2 className="text-lg font-semibold text-gray-700 mb-3">
          Recent Safety Checks
          {risk?.recentValidations?.length > 0 && (
            <span className="ml-2 text-sm font-normal text-gray-400">
              (last {Math.min(risk.recentValidations.length, 50)})
            </span>
          )}
        </h2>
        {!risk?.recentValidations?.length ? (
          <div className="text-center py-8 text-gray-400">No validation history yet</div>
        ) : (
          <div className="space-y-2">
            {risk.recentValidations.slice(0, 50).map((v, i) => (
              <ValidationRow key={i} v={v} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
