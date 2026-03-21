import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { usePriceComparison } from '../hooks/useDashboard.js'

const COIN_COLORS = {
  BTCUSDT: '#F59E0B',
  ETHUSDT: '#3B82F6',
  BNBUSDT: '#EAB308',
  SOLUSDT: '#8B5CF6',
  XRPUSDT: '#10B981',
}
const FALLBACK_COLORS = ['#F59E0B', '#3B82F6', '#EAB308', '#8B5CF6', '#10B981', '#EC4899', '#06B6D4']

const W = 700
const H = 220
const PAD = { left: 52, right: 12, top: 14, bottom: 34 }
const CHART_W = W - PAD.left - PAD.right
const CHART_H = H - PAD.top - PAD.bottom

function buildPath(points) {
  if (!points.length) return ''
  return points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ')
}

export function PriceChangeChart({ symbols }) {
  const { t } = useTranslation('overview')
  const { data, loading, lastUpdated } = usePriceComparison(symbols)

  const { seriesBySymbol, yMin, yMax, tStart, tEnd } = useMemo(() => {
    if (!data?.comparison?.length) return { seriesBySymbol: {}, yMin: -0.5, yMax: 0.5, tStart: 0, tEnd: 1 }

    // Group comparison points by symbol
    const grouped = {}
    for (const pt of data.comparison) {
      if (!grouped[pt.symbol]) grouped[pt.symbol] = []
      grouped[pt.symbol].push({ ms: new Date(pt.time).getTime(), close: Number(pt.close) })
    }

    // Build % change series
    const seriesBySymbol = {}
    let globalMin = Infinity
    let globalMax = -Infinity
    let tMin = Infinity
    let tMax = -Infinity

    for (const sym of symbols) {
      const pts = grouped[sym]
      if (!pts?.length) continue
      pts.sort((a, b) => a.ms - b.ms)
      const first = pts[0].close
      if (!first) continue
      const series = pts.map(p => {
        const pct = ((p.close - first) / first) * 100
        if (pct < globalMin) globalMin = pct
        if (pct > globalMax) globalMax = pct
        if (p.ms < tMin) tMin = p.ms
        if (p.ms > tMax) tMax = p.ms
        return { ms: p.ms, pct }
      })
      seriesBySymbol[sym] = series
    }

    if (globalMin === Infinity) return { seriesBySymbol: {}, yMin: -0.5, yMax: 0.5, tStart: 0, tEnd: 1 }

    const range = Math.max(globalMax - globalMin, 0.05)
    const pad = range * 0.15
    return {
      seriesBySymbol,
      yMin: globalMin - pad,
      yMax: globalMax + pad,
      tStart: tMin,
      tEnd: Math.max(tMax, tMin + 1),
    }
  }, [data, symbols.join(',')])

  // SVG paths
  const paths = useMemo(() => {
    const tRange = tEnd - tStart
    const yRange = yMax - yMin
    if (!tRange || !yRange) return {}
    const result = {}
    for (const sym of symbols) {
      const series = seriesBySymbol[sym]
      if (!series?.length) continue
      const points = series.map(p => ({
        x: PAD.left + ((p.ms - tStart) / tRange) * CHART_W,
        y: PAD.top + CHART_H * (1 - (p.pct - yMin) / yRange),
      }))
      result[sym] = buildPath(points)
    }
    return result
  }, [seriesBySymbol, yMin, yMax, tStart, tEnd, symbols.join(',')])

  // Y-axis ticks
  const yTicks = useMemo(() => {
    const range = yMax - yMin
    const step = range < 0.5 ? 0.1 : range < 2 ? 0.5 : range < 5 ? 1 : range < 10 ? 2 : 5
    const ticks = []
    const start = Math.ceil((yMin + 1e-9) / step) * step
    for (let v = start; v <= yMax + 1e-9; v = Math.round((v + step) * 1000) / 1000) {
      const y = PAD.top + CHART_H * (1 - (v - yMin) / (yMax - yMin))
      ticks.push({ label: `${v >= 0 ? '+' : ''}${v.toFixed(1)}%`, y })
    }
    return ticks
  }, [yMin, yMax])

  // X-axis labels (6 evenly spaced)
  const xLabels = useMemo(() => {
    if (!tStart || !tEnd) return []
    const tRange = tEnd - tStart
    return Array.from({ length: 7 }, (_, i) => {
      const ms = tStart + (tRange * i) / 6
      const x = PAD.left + ((ms - tStart) / tRange) * CHART_W
      const label = new Date(ms).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' })
      return { x, label }
    })
  }, [tStart, tEnd])

  // Zero line Y position
  const zeroY = yMin <= 0 && yMax >= 0
    ? PAD.top + CHART_H * (1 - (0 - yMin) / (yMax - yMin))
    : null

  // Latest % change per symbol for legend
  const latestPct = useMemo(() => {
    const result = {}
    for (const sym of symbols) {
      const s = seriesBySymbol[sym]
      if (s?.length) result[sym] = s[s.length - 1].pct
    }
    return result
  }, [seriesBySymbol, symbols.join(',')])

  const hasData = Object.keys(paths).length > 0

  return (
    <div className="rounded-xl p-4" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)' }}>
      <div className="flex items-center justify-between mb-3">
        <div>
          <h2 className="text-lg font-semibold" style={{ color: '#0f172a' }}>{t('priceChart.title')}</h2>
          <p className="text-xs text-gray-400 mt-0.5">{t('priceChart.subtitle')}</p>
        </div>
        {lastUpdated && (
          <span className="text-xs text-gray-400 shrink-0">
            {t('priceChart.updatedAt', { time: lastUpdated.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }) })}
          </span>
        )}
      </div>

      {loading && !hasData ? (
        <div className="h-48 flex items-center justify-center">
          <div className="space-y-2 w-full px-4">
            {[80, 60, 90, 50].map((w, i) => (
              <div key={i} className="h-2 bg-gray-100 rounded animate-pulse" style={{ width: `${w}%` }} />
            ))}
          </div>
        </div>
      ) : !hasData ? (
        <div className="h-48 flex items-center justify-center text-gray-400">
          <div className="text-center">
            <p className="text-3xl mb-2">📉</p>
            <p className="text-sm">{t('priceChart.noData')}</p>
          </div>
        </div>
      ) : (
        <>
          <svg
            viewBox={`0 0 ${W} ${H}`}
            className="w-full"
            style={{ height: 'auto', maxHeight: 240 }}
            aria-label={t('priceChart.title')}
          >
            {/* Horizontal grid lines */}
            {yTicks.map((tick, i) => (
              <g key={i}>
                <line
                  x1={PAD.left} y1={tick.y}
                  x2={PAD.left + CHART_W} y2={tick.y}
                  stroke="#f1f5f9" strokeWidth="1"
                />
                <text
                  x={PAD.left - 5} y={tick.y}
                  textAnchor="end" dominantBaseline="middle"
                  fontSize="9.5" fill="#94a3b8"
                >
                  {tick.label}
                </text>
              </g>
            ))}

            {/* Zero baseline */}
            {zeroY !== null && (
              <line
                x1={PAD.left} y1={zeroY}
                x2={PAD.left + CHART_W} y2={zeroY}
                stroke="#cbd5e1" strokeWidth="1" strokeDasharray="4,3"
              />
            )}

            {/* Chart border */}
            <rect
              x={PAD.left} y={PAD.top}
              width={CHART_W} height={CHART_H}
              fill="none" stroke="#e2e8f0" strokeWidth="1"
            />

            {/* Series lines */}
            {symbols.map((sym, idx) => {
              const d = paths[sym]
              if (!d) return null
              const color = COIN_COLORS[sym] ?? FALLBACK_COLORS[idx % FALLBACK_COLORS.length]
              return (
                <path
                  key={sym}
                  d={d}
                  fill="none"
                  stroke={color}
                  strokeWidth="2"
                  strokeLinejoin="round"
                  strokeLinecap="round"
                />
              )
            })}

            {/* X-axis labels */}
            {xLabels.map((lbl, i) => (
              <text
                key={i}
                x={lbl.x} y={PAD.top + CHART_H + 18}
                textAnchor="middle" fontSize="9" fill="#94a3b8"
              >
                {lbl.label}
              </text>
            ))}
          </svg>

          {/* Legend */}
          <div className="flex flex-wrap gap-x-4 gap-y-1.5 mt-2 pt-2 border-t border-gray-50">
            {symbols.map((sym, idx) => {
              const pct = latestPct[sym]
              if (pct === undefined) return null
              const color = COIN_COLORS[sym] ?? FALLBACK_COLORS[idx % FALLBACK_COLORS.length]
              const sign = pct >= 0 ? '+' : ''
              return (
                <div key={sym} className="flex items-center gap-1.5">
                  <span style={{ display: 'inline-block', width: 14, height: 3, borderRadius: 2, background: color }} />
                  <span className="text-xs text-gray-500 font-medium">{sym.replace('USDT', '')}</span>
                  <span className={`text-xs font-bold ${pct >= 0 ? 'text-green-600' : 'text-red-500'}`}>
                    {sign}{pct.toFixed(2)}%
                  </span>
                </div>
              )
            })}
          </div>
        </>
      )}
    </div>
  )
}
