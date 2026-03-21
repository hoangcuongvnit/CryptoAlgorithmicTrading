import { deriveMarketContext, formatPrice } from '../utils/indicators.js'
import { useCandles } from '../hooks/useDashboard.js'

const HEAT_EMOJIS = {
  cold: '🧊',
  cool: '❄️',
  neutral: '✅',
  warm: '🌡️',
  hot: '🔥',
  unknown: '⏳',
}

export function SentimentGauge({ symbol }) {
  const { data, loading } = useCandles(symbol, 60)

  const ctx = data?.candles ? deriveMarketContext(data.candles) : null

  if (loading && !ctx) {
    return (
      <div className="bg-white rounded-xl border border-gray-100 shadow-sm p-4 animate-pulse">
        <div className="h-4 bg-gray-200 rounded w-24 mb-2" />
        <div className="h-8 bg-gray-200 rounded w-16" />
      </div>
    )
  }

  if (!ctx) {
    return (
      <div className="bg-white rounded-xl border border-gray-100 shadow-sm p-4">
        <p className="text-sm font-semibold text-gray-600">{symbol}</p>
        <p className="text-xs text-gray-400 mt-1">No data</p>
      </div>
    )
  }

  const emoji = HEAT_EMOJIS[ctx.heat] ?? '❓'

  return (
    <div className="bg-white rounded-xl border border-gray-100 shadow-sm p-4 hover:shadow-md transition-shadow">
      <div className="flex items-center justify-between mb-2">
        <p className="text-sm font-semibold text-gray-700">{symbol.replace('USDT', '')}</p>
        <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${ctx.heatBg}`}>
          {emoji} {ctx.heatLabel}
        </span>
      </div>
      <p className="text-lg font-bold text-gray-800">${formatPrice(ctx.currentPrice)}</p>
      {ctx.rsi !== null && (
        <div className="mt-2">
          <div className="flex justify-between text-xs text-gray-400 mb-1">
            <span>Cold</span>
            <span>RSI {ctx.rsi}</span>
            <span>Hot</span>
          </div>
          <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-500 ${
                ctx.rsi < 30 ? 'bg-blue-400' :
                ctx.rsi < 50 ? 'bg-cyan-400' :
                ctx.rsi < 70 ? 'bg-yellow-400' : 'bg-red-500'
              }`}
              style={{ width: `${Math.min(ctx.rsi, 100)}%` }}
            />
          </div>
        </div>
      )}
      <p className="text-xs text-gray-500 mt-2">
        {ctx.trend === 'uptrend' ? '↗ Uptrend' : ctx.trend === 'downtrend' ? '↘ Downtrend' : '—'}
      </p>
    </div>
  )
}
