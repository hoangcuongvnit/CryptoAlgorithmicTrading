// Compute RSI (14-period by default)
export function computeRSI(closes, period = 14) {
  if (closes.length < period + 1) return 50

  let gains = 0
  let losses = 0
  for (let i = 1; i <= period; i++) {
    const diff = closes[i] - closes[i - 1]
    if (diff > 0) gains += diff
    else losses += Math.abs(diff)
  }

  let avgGain = gains / period
  let avgLoss = losses / period

  for (let i = period + 1; i < closes.length; i++) {
    const diff = closes[i] - closes[i - 1]
    avgGain = (avgGain * (period - 1) + Math.max(diff, 0)) / period
    avgLoss = (avgLoss * (period - 1) + Math.max(-diff, 0)) / period
  }

  if (avgLoss === 0) return 100
  const rs = avgGain / avgLoss
  return Math.round(100 - 100 / (1 + rs))
}

// Compute EMA
export function computeEMA(closes, period) {
  if (closes.length < period) return closes[closes.length - 1] ?? 0
  const multiplier = 2 / (period + 1)
  let ema = closes.slice(0, period).reduce((s, v) => s + v, 0) / period
  for (let i = period; i < closes.length; i++) {
    ema = (closes[i] - ema) * multiplier + ema
  }
  return ema
}

// Compute Bollinger Bands (20-period, 2 std dev)
export function computeBollinger(closes, period = 20, mult = 2) {
  const slice = closes.slice(-Math.max(period, 1))
  const mid = slice.reduce((s, v) => s + v, 0) / slice.length
  const variance = slice.reduce((s, v) => s + (v - mid) ** 2, 0) / slice.length
  const std = Math.sqrt(variance)
  return { mid, upper: mid + mult * std, lower: mid - mult * std }
}

// Derive a human-readable market context from indicators
export function deriveMarketContext(candles) {
  if (!candles || candles.length < 20) {
    return {
      heat: 'unknown',
      heatLabel: 'Insufficient Data',
      heatColor: 'gray',
      rsi: null,
      ema9: null,
      ema21: null,
      trend: 'unknown',
      signal: 'Not enough price history yet',
      signalType: 'neutral',
      signalIcon: '⏳',
      currentPrice: candles?.slice(-1)[0]?.close ?? 0,
    }
  }

  const closes = candles.map(c => Number(c.close))
  const currentPrice = closes[closes.length - 1]

  const rsi = computeRSI(closes)
  const ema9 = computeEMA(closes, 9)
  const ema21 = computeEMA(closes, 21)
  const bb = computeBollinger(closes)

  const isUptrend = ema9 > ema21
  const bbRange = bb.upper - bb.lower
  const pricePosition = bbRange > 0 ? (currentPrice - bb.lower) / bbRange : 0.5

  // Heat level based on RSI
  let heat, heatLabel, heatColor, heatBg
  if (rsi < 30) {
    heat = 'cold'; heatLabel = 'Oversold'; heatColor = 'blue'; heatBg = 'bg-blue-100 text-blue-800'
  } else if (rsi < 45) {
    heat = 'cool'; heatLabel = 'Cooling Down'; heatColor = 'cyan'; heatBg = 'bg-cyan-100 text-cyan-800'
  } else if (rsi < 55) {
    heat = 'neutral'; heatLabel = 'Balanced'; heatColor = 'green'; heatBg = 'bg-green-100 text-green-800'
  } else if (rsi < 70) {
    heat = 'warm'; heatLabel = 'Heating Up'; heatColor = 'yellow'; heatBg = 'bg-yellow-100 text-yellow-800'
  } else {
    heat = 'hot'; heatLabel = 'Overheated'; heatColor = 'red'; heatBg = 'bg-red-100 text-red-800'
  }

  // Signal in plain English
  let signal, signalType, signalIcon
  if (rsi < 35 && isUptrend) {
    signal = 'Price is low and rising — Strong buy opportunity'
    signalType = 'buy_strong'; signalIcon = '📈'
  } else if (rsi < 35 && !isUptrend) {
    signal = 'Price is low but still falling — Wait before buying'
    signalType = 'buy_weak'; signalIcon = '⬇️'
  } else if (rsi > 65 && !isUptrend) {
    signal = 'Market is overheated and cooling — Risk is high'
    signalType = 'sell_strong'; signalIcon = '🚨'
  } else if (rsi > 65 && isUptrend) {
    signal = 'Market is strong but may slow down — Monitor closely'
    signalType = 'sell_weak'; signalIcon = '⚠️'
  } else if (pricePosition < 0.2 && rsi < 60) {
    signal = 'Price is near support — Potential bounce incoming'
    signalType = 'bounce'; signalIcon = '🔄'
  } else if (pricePosition > 0.8 && rsi > 40) {
    signal = 'Price is near resistance — Proceed with caution'
    signalType = 'resistance'; signalIcon = '🛑'
  } else if (isUptrend) {
    signal = 'Market is trending upward — Conditions are moderate'
    signalType = 'neutral_bull'; signalIcon = '📊'
  } else {
    signal = 'Market is balanced — No strong signal right now'
    signalType = 'neutral'; signalIcon = '➖'
  }

  return {
    heat, heatLabel, heatBg, heatColor,
    rsi, ema9: Math.round(ema9 * 100) / 100, ema21: Math.round(ema21 * 100) / 100,
    trend: isUptrend ? 'uptrend' : 'downtrend',
    signal, signalType, signalIcon,
    currentPrice,
    pricePosition: Math.round(pricePosition * 100),
  }
}

// Format a price nicely
export function formatPrice(price) {
  if (!price) return '—'
  const num = Number(price)
  if (num >= 1000) return num.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  if (num >= 1) return num.toFixed(4)
  return num.toFixed(6)
}

// Format P&L with sign and color class
export function formatPnl(pnl) {
  const num = Number(pnl)
  const sign = num >= 0 ? '+' : ''
  return `${sign}$${Math.abs(num).toFixed(2)}`
}

export function pnlColorClass(pnl) {
  return Number(pnl) >= 0 ? 'text-green-600' : 'text-red-600'
}
