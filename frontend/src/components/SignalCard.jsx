import { deriveMarketContext, formatPrice } from '../utils/indicators.js'
import { useCandles } from '../hooks/useDashboard.js'

const SIGNAL_COLORS = {
  buy_strong: 'border-green-400 bg-green-50',
  buy_weak: 'border-blue-300 bg-blue-50',
  sell_strong: 'border-red-400 bg-red-50',
  sell_weak: 'border-orange-300 bg-orange-50',
  bounce: 'border-cyan-300 bg-cyan-50',
  resistance: 'border-yellow-400 bg-yellow-50',
  neutral_bull: 'border-green-200 bg-green-50',
  neutral: 'border-gray-200 bg-gray-50',
}

export function SignalCard({ symbol }) {
  const { data, loading } = useCandles(symbol, 90)
  const ctx = data?.candles ? deriveMarketContext(data.candles) : null

  if (loading && !ctx) {
    return (
      <div className="rounded-xl border-l-4 border-gray-200 bg-gray-50 p-4 animate-pulse">
        <div className="h-4 bg-gray-200 rounded w-20 mb-2" />
        <div className="h-4 bg-gray-200 rounded w-full" />
      </div>
    )
  }

  if (!ctx) return null

  const borderBg = SIGNAL_COLORS[ctx.signalType] ?? 'border-gray-200 bg-gray-50'

  return (
    <div className={`rounded-xl border-l-4 p-4 ${borderBg}`}>
      <div className="flex items-center justify-between mb-2">
        <span className="font-semibold text-gray-800">{symbol.replace('USDT', '/USDT')}</span>
        <span className="text-xs text-gray-500">${formatPrice(ctx.currentPrice)}</span>
      </div>
      <p className="text-sm text-gray-700 font-medium">
        {ctx.signalIcon} {ctx.signal}
      </p>
      <div className="flex gap-3 mt-3 text-xs text-gray-500">
        <span>RSI: <strong>{ctx.rsi ?? '—'}</strong></span>
        <span>EMA9: <strong>${formatPrice(ctx.ema9)}</strong></span>
        <span>EMA21: <strong>${formatPrice(ctx.ema21)}</strong></span>
      </div>
    </div>
  )
}
