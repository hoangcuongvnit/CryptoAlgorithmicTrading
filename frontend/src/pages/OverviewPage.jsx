import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import { SentimentGauge } from '../components/SentimentGauge.jsx'
import { SafetyLight } from '../components/SafetyLight.jsx'
import { EventTimeline } from '../components/EventTimeline.jsx'
import { PriceChangeChart } from '../components/PriceChangeChart.jsx'
import { useRiskStats, useRiskConfig, useNotifierStats } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'
import { useSettings } from '../context/SettingsContext.jsx'

// Sessions: 8h each, 3 per day, starting 00:00 UTC
const SESSION_HOURS = 8
const LIQUIDATION_MIN = 30   // before session end
const SOFT_UNWIND_MIN = 15   // before liquidation
const FORCED_FLATTEN_MIN = 2 // before session end

function fmtCountdown(seconds) {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

function computeClientSession() {
  const now = new Date()
  const sessionNumber = Math.floor(now.getUTCHours() / SESSION_HOURS) + 1
  const sessionStartHour = (sessionNumber - 1) * SESSION_HOURS
  const sessionEnd = new Date(Date.UTC(
    now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(),
    sessionStartHour + SESSION_HOURS, 0, 0, 0))
  const liquidationStart = new Date(sessionEnd.getTime() - LIQUIDATION_MIN * 60_000)
  const softUnwindStart  = new Date(liquidationStart.getTime() - SOFT_UNWIND_MIN * 60_000)
  const forcedFlattenStart = new Date(sessionEnd.getTime() - FORCED_FLATTEN_MIN * 60_000)

  let currentPhase
  if (now >= forcedFlattenStart)  currentPhase = 'ForcedFlatten'
  else if (now >= liquidationStart) currentPhase = 'LiquidationOnly'
  else if (now >= softUnwindStart)  currentPhase = 'SoftUnwind'
  else                              currentPhase = 'Open'

  const secsToEnd = Math.max(0, Math.floor((sessionEnd - now) / 1000))
  const secsToLiq = Math.max(0, Math.floor((liquidationStart - now) / 1000))

  return {
    sessionNumber,
    currentPhase,
    endCountdown: fmtCountdown(secsToEnd),
    liqCountdown: (currentPhase === 'Open' || currentPhase === 'SoftUnwind') && secsToLiq > 0
      ? fmtCountdown(secsToLiq)
      : null,
  }
}

function useClientSession() {
  const [session, setSession] = useState(computeClientSession)
  useEffect(() => {
    const id = setInterval(() => setSession(computeClientSession()), 1000)
    return () => clearInterval(id)
  }, [])
  return session
}

const PHASE_STYLE = {
  Open:            { bg: '#f0fdf4', border: '#86efac', color: '#15803d' },
  SoftUnwind:      { bg: '#fffbeb', border: '#fcd34d', color: '#b45309' },
  LiquidationOnly: { bg: '#fff7ed', border: '#fdba74', color: '#c2410c' },
  ForcedFlatten:   { bg: '#fef2f2', border: '#fca5a5', color: '#dc2626' },
  SessionClosed:   { bg: '#f8fafc', border: '#cbd5e1', color: '#64748b' },
}

function SessionCountdown() {
  const { t } = useTranslation('overview')
  const { sessionNumber, currentPhase, endCountdown } = useClientSession()
  const style = PHASE_STYLE[currentPhase] ?? PHASE_STYLE.SessionClosed

  return (
    <div className="rounded-xl border p-4 flex flex-col gap-1"
      style={{ background: style.bg, borderColor: style.border }}>
      <div className="flex items-center justify-between">
        <span className="text-sm text-gray-500">{t('session.title')}</span>
        <span className="text-xs px-2 py-0.5 rounded-full font-semibold"
          style={{ background: style.border + '55', color: style.color }}>
          {t('session.sessionLabel', { number: sessionNumber })}
        </span>
      </div>
      <div className="font-mono text-2xl font-bold" style={{ color: style.color }}>{endCountdown}</div>
      <div className="text-xs" style={{ color: style.color, opacity: 0.75 }}>
        {t(`session.phase.${currentPhase}`)}
      </div>
    </div>
  )
}

const DEFAULT_SYMBOLS = ['BTCUSDT', 'ETHUSDT', 'BNBUSDT', 'SOLUSDT', 'XRPUSDT']

export function OverviewPage() {
  const { t } = useTranslation(['overview', 'common'])
  const { systemTimezone } = useSettings()
  const { data: risk } = useRiskStats()
  const { data: config } = useRiskConfig()
  const { data: notifier, lastUpdated } = useNotifierStats()

  const { tradingMode } = useSettings()
  const symbols = config?.allowedSymbols?.length > 0 ? config.allowedSymbols : DEFAULT_SYMBOLS

  const totalOrders = (risk?.todayApproved ?? 0) + (risk?.todayRejected ?? 0)

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
        <div className="flex items-center gap-3">
          {tradingMode && (
            <span className="px-3 py-1 rounded-full text-sm font-semibold border"
              style={
                tradingMode === 'paper'
                  ? { background: '#eff6ff', border: '1px solid #93c5fd', color: '#1d4ed8' }
                  : tradingMode === 'testnet'
                  ? { background: '#f0fdf4', border: '1px solid #86efac', color: '#15803d' }
                  : { background: '#fef2f2', border: '1px solid #fca5a5', color: '#dc2626' }
              }
            >
              {tradingMode === 'paper'
                ? t('common:paperMode')
                : tradingMode === 'testnet'
                ? t('common:testnetMode')
                : t('common:liveTrading')}
            </span>
          )}
          {lastUpdated && (
            <span className="text-xs text-gray-400">
              {t('common:updatedAt', { time: lastUpdated.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false, timeZone: systemTimezone }) })}
            </span>
          )}
        </div>
      </div>

      {/* Safety Banner */}
      <SafetyLight riskStats={risk} riskConfig={config} />

      {/* Stats Row */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        <SessionCountdown />
        <StatCard
          title={t('common:todayPnl')}
          value={risk ? formatPnl(risk.dailyPnl) : '—'}
          subtitle={tradingMode === 'live' ? t('common:realized') : t('common:simulated')}
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
        <EventTimeline events={notifier?.recentNotifications ?? notifier?.recent ?? []} />
      </div>
    </div>
  )
}
