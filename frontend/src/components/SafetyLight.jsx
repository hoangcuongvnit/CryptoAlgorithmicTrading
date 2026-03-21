export function SafetyLight({ riskStats, riskConfig, compact = false }) {
  if (!riskStats) return null

  const isBreached = riskStats.drawdownUsedPercent >= 100
  const hasCooldown = riskStats.cooldowns?.length > 0

  let status, color, bgColor, description, icon
  if (isBreached) {
    status = 'TRADING HALTED'
    color = 'text-red-700'
    bgColor = 'bg-red-50 border-red-200'
    description = 'Daily loss limit reached. All trading is paused until midnight UTC.'
    icon = '🔴'
  } else if (hasCooldown) {
    status = 'COOLDOWN ACTIVE'
    color = 'text-yellow-700'
    bgColor = 'bg-yellow-50 border-yellow-200'
    description = `Waiting before placing new trades on ${riskStats.cooldowns.map(c => c.symbol.replace('USDT', '')).join(', ')}.`
    icon = '🟡'
  } else {
    status = 'ALL CLEAR'
    color = 'text-green-700'
    bgColor = 'bg-green-50 border-green-200'
    description = 'System is healthy. Trading is active.'
    icon = '🟢'
  }

  if (compact) {
    return (
      <span className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-sm font-semibold border ${bgColor} ${color}`}>
        {icon} {status}
      </span>
    )
  }

  const drawdownPct = Math.min(Number(riskStats.drawdownUsedPercent ?? 0), 100)
  const barColor = drawdownPct >= 100 ? 'bg-red-500' : drawdownPct >= 70 ? 'bg-orange-400' : drawdownPct >= 40 ? 'bg-yellow-400' : 'bg-green-400'

  return (
    <div className={`rounded-xl border-2 p-5 ${bgColor}`}>
      <div className="flex items-center gap-3 mb-3">
        <span className="text-4xl">{icon}</span>
        <div>
          <p className={`text-xl font-bold ${color}`}>{status}</p>
          <p className="text-sm text-gray-600">{description}</p>
        </div>
      </div>

      <div className="mt-4">
        <div className="flex justify-between text-sm text-gray-600 mb-1">
          <span>Daily Loss Used</span>
          <span className="font-semibold">{drawdownPct.toFixed(1)}% of ${Number(riskStats.maxDrawdownUsd ?? 0).toFixed(0)} limit</span>
        </div>
        <div className="h-3 bg-white rounded-full border overflow-hidden">
          <div
            className={`h-full rounded-full transition-all duration-700 ${barColor}`}
            style={{ width: `${drawdownPct}%` }}
          />
        </div>
      </div>

      {riskStats.cooldowns?.length > 0 && (
        <div className="mt-4">
          <p className="text-sm font-medium text-gray-700 mb-2">Active Cooldowns</p>
          {riskStats.cooldowns.map(c => (
            <div key={c.symbol} className="flex justify-between text-sm bg-white rounded-lg px-3 py-2 mb-1 border border-yellow-200">
              <span className="font-medium">{c.symbol.replace('USDT', '')}</span>
              <span className="text-gray-500">Wait {Math.round(c.remainingSeconds)}s</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
