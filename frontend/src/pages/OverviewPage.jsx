import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import { SentimentGauge } from '../components/SentimentGauge.jsx'
import { SafetyLight } from '../components/SafetyLight.jsx'
import { EventTimeline } from '../components/EventTimeline.jsx'
import { PriceChangeChart } from '../components/PriceChangeChart.jsx'
import { useRiskStats, useRiskConfig, useNotifierStats } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'

const DEFAULT_SYMBOLS = ['BTCUSDT', 'ETHUSDT', 'BNBUSDT', 'SOLUSDT', 'XRPUSDT']

export function OverviewPage() {
  const { t } = useTranslation(['overview', 'common'])
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
        <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
        <div className="flex items-center gap-3">
          <span className={`px-3 py-1 rounded-full text-sm font-medium ${mode === 'Paper' ? 'bg-blue-100 text-blue-700' : 'bg-orange-100 text-orange-700'}`}>
            {mode === 'Paper' ? t('common:paperMode') : t('common:liveTrading')}
          </span>
          {lastUpdated && (
            <span className="text-xs text-gray-400">
              {t('common:updatedAt', { time: lastUpdated.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }) })}
            </span>
          )}
        </div>
      </div>

      {/* Safety Banner */}
      <SafetyLight riskStats={risk} riskConfig={config} />

      {/* Stats Row */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title={t('common:todayPnl')}
          value={risk ? formatPnl(risk.dailyPnl) : '—'}
          subtitle={mode === 'Paper' ? t('common:simulated') : t('common:realized')}
          icon="💵"
          colorClass={risk ? pnlColorClass(risk.dailyPnl) : 'text-gray-400'}
        />
        <StatCard
          title={t('stats.tradesTotal')}
          value={totalOrders}
          subtitle={t('stats.approved', { count: risk?.todayApproved ?? 0 })}
          icon="📊"
          colorClass="text-gray-800"
        />
        <StatCard
          title={t('stats.drawdownUsed')}
          value={risk ? `${Number(risk.drawdownUsedPercent).toFixed(1)}%` : '—'}
          subtitle={t('stats.drawdownOf', { max: Number(risk?.maxDrawdownUsd ?? 0).toFixed(0) })}
          icon="🛡️"
          colorClass={
            !risk ? 'text-gray-400' :
            risk.drawdownUsedPercent >= 80 ? 'text-red-600' :
            risk.drawdownUsedPercent >= 50 ? 'text-orange-500' :
            'text-green-600'
          }
        />
        <StatCard
          title={t('common:notifications')}
          value={notifier?.todayTotal ?? '—'}
          subtitle={t('common:sentToday')}
          icon="🔔"
          colorClass="text-gray-800"
        />
      </div>

      {/* Market Sentiment Gauges */}
      <div>
        <h2 className="text-lg font-semibold mb-3" style={{ color: '#0f172a' }}>{t('marketSentiment')}</h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
          {symbols.map(sym => (
            <SentimentGauge key={sym} symbol={sym} />
          ))}
        </div>
      </div>

      {/* 1-Hour Price Change Chart */}
      <PriceChangeChart symbols={symbols} />

      {/* Recent Events */}
      <div>
        <h2 className="text-lg font-semibold mb-3" style={{ color: '#0f172a' }}>{t('recentActivity')}</h2>
        <EventTimeline events={notifier?.recent} maxItems={10} />
      </div>
    </div>
  )
}
