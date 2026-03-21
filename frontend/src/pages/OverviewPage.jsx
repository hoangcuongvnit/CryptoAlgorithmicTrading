import { useState } from 'react'
import { StatCard } from '../components/StatCard.jsx'
import { SentimentGauge } from '../components/SentimentGauge.jsx'
import { SafetyLight } from '../components/SafetyLight.jsx'
import { EventTimeline } from '../components/EventTimeline.jsx'
import { useRiskStats, useRiskConfig, useNotifierStats } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'

const DEFAULT_SYMBOLS = ['BTCUSDT', 'ETHUSDT', 'BNBUSDT', 'SOLUSDT', 'XRPUSDT']

export function OverviewPage() {
  const { data: risk, loading: riskLoading } = useRiskStats()
  const { data: config } = useRiskConfig()
  const { data: notifier, lastUpdated } = useNotifierStats()

  const symbols = config?.allowedSymbols?.length > 0 ? config.allowedSymbols : DEFAULT_SYMBOLS
  const mode = config?.paperTradingOnly !== false ? 'Paper' : 'Live'

  const totalOrders = (risk?.todayApproved ?? 0) + (risk?.todayRejected ?? 0)

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-800">Trading Overview</h1>
        <div className="flex items-center gap-3">
          <span className={`px-3 py-1 rounded-full text-sm font-medium ${mode === 'Paper' ? 'bg-blue-100 text-blue-700' : 'bg-orange-100 text-orange-700'}`}>
            {mode === 'Paper' ? '📝 Paper Mode' : '💰 Live Trading'}
          </span>
          {lastUpdated && (
            <span className="text-xs text-gray-400">
              Updated {lastUpdated.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })}
            </span>
          )}
        </div>
      </div>

      {/* Safety Banner */}
      <SafetyLight riskStats={risk} riskConfig={config} />

      {/* Stats Row */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title="Today's P&L"
          value={risk ? formatPnl(risk.dailyPnl) : '—'}
          subtitle={mode === 'Paper' ? 'Simulated' : 'Realized'}
          icon="💵"
          colorClass={risk ? pnlColorClass(risk.dailyPnl) : 'text-gray-400'}
        />
        <StatCard
          title="Trades Today"
          value={totalOrders}
          subtitle={`${risk?.todayApproved ?? 0} approved`}
          icon="📊"
          colorClass="text-gray-800"
        />
        <StatCard
          title="Drawdown Used"
          value={risk ? `${Number(risk.drawdownUsedPercent).toFixed(1)}%` : '—'}
          subtitle={`of $${Number(risk?.maxDrawdownUsd ?? 0).toFixed(0)} max`}
          icon="🛡️"
          colorClass={
            !risk ? 'text-gray-400' :
            risk.drawdownUsedPercent >= 80 ? 'text-red-600' :
            risk.drawdownUsedPercent >= 50 ? 'text-orange-500' :
            'text-green-600'
          }
        />
        <StatCard
          title="Notifications"
          value={notifier?.todayTotal ?? '—'}
          subtitle="sent today"
          icon="🔔"
          colorClass="text-gray-800"
        />
      </div>

      {/* Market Sentiment Gauges */}
      <div>
        <h2 className="text-lg font-semibold text-gray-700 mb-3">Market Sentiment</h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
          {symbols.map(sym => (
            <SentimentGauge key={sym} symbol={sym} />
          ))}
        </div>
      </div>

      {/* Recent Events */}
      <div>
        <h2 className="text-lg font-semibold text-gray-700 mb-3">Recent Activity</h2>
        <EventTimeline events={notifier?.recent} maxItems={10} />
      </div>
    </div>
  )
}
