import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { StatCard } from '../components/StatCard.jsx'
import { SentimentGauge } from '../components/SentimentGauge.jsx'
import { SafetyLight } from '../components/SafetyLight.jsx'
import { EventTimeline } from '../components/EventTimeline.jsx'
import { PriceChangeChart } from '../components/PriceChangeChart.jsx'
import { useRiskStats, useRiskConfig, useNotifierStats, useSession } from '../hooks/useDashboard.js'
import { formatPnl, pnlColorClass } from '../utils/indicators.js'
import { useSettings } from '../context/SettingsContext.jsx'

function useCountdown(targetUtc) {
  const [seconds, setSeconds] = useState(null)

  useEffect(() => {
    if (!targetUtc) { setSeconds(null); return }
    const target = new Date(targetUtc).getTime()
    const tick = () => {
      const diff = Math.max(0, Math.floor((target - Date.now()) / 1000))
      setSeconds(diff)
    }
    tick()
    const id = setInterval(tick, 1000)
    return () => clearInterval(id)
  }, [targetUtc])

  if (seconds === null) return null
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

const PHASE_STYLE = {
  Open:            { bg: '#f0fdf4', border: '#86efac', color: '#15803d' },
  SoftUnwind:      { bg: '#fffbeb', border: '#fcd34d', color: '#b45309' },
  LiquidationOnly: { bg: '#fff7ed', border: '#fdba74', color: '#c2410c' },
  ForcedFlatten:   { bg: '#fef2f2', border: '#fca5a5', color: '#dc2626' },
  SessionClosed:   { bg: '#f8fafc', border: '#cbd5e1', color: '#64748b' },
}

function SessionCountdown({ session }) {
  const { t } = useTranslation('overview')
  const phase = session?.currentPhase ?? 'SessionClosed'
  const endCountdown = useCountdown(session?.sessionEndUtc)
  const liqCountdown = useCountdown(
    phase === 'Open' || phase === 'SoftUnwind' ? session?.liquidationStartUtc : null
  )
  const style = PHASE_STYLE[phase] ?? PHASE_STYLE.SessionClosed

  return (
    <div className="rounded-xl border p-4 flex flex-wrap items-center gap-4"
      style={{ background: style.bg, borderColor: style.border }}>
      <div className="flex items-center gap-2">
        <span className="text-sm font-medium" style={{ color: style.color }}>
          {t('session.title')}
        </span>
        {session?.sessionNumber && (
          <span className="text-xs px-2 py-0.5 rounded-full font-semibold"
            style={{ background: style.border + '55', color: style.color }}>
            {t('session.sessionLabel', { number: session.sessionNumber })}
          </span>
        )}
        <span className="text-xs px-2 py-0.5 rounded-full border font-semibold"
          style={{ background: '#fff', borderColor: style.border, color: style.color }}>
          {t(`session.phase.${phase}`)}
        </span>
      </div>
      <div className="flex items-center gap-4 ml-auto">
        {liqCountdown && (
          <div className="text-center">
            <div className="text-xs text-gray-500">{t('session.liquidationIn')}</div>
            <div className="font-mono text-base font-bold" style={{ color: '#b45309' }}>{liqCountdown}</div>
          </div>
        )}
        {endCountdown && phase !== 'SessionClosed' && (
          <div className="text-center">
            <div className="text-xs text-gray-500">{t('session.endsIn')}</div>
            <div className="font-mono text-lg font-bold" style={{ color: style.color }}>{endCountdown}</div>
          </div>
        )}
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
  const { data: session } = useSession()

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

      {/* Session Countdown */}
      {session && <SessionCountdown session={session} />}

      {/* Stats Row */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
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
        <EventTimeline events={notifier?.recent} maxItems={10} />
      </div>
    </div>
  )
}
