import { useCallback, useMemo } from 'react'
import { usePolling } from './usePolling.js'
import { mergeAndSort } from '../utils/activityNormalizer.js'

const BASE = ''

async function fetchJson(url) {
  const res = await fetch(BASE + url)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}

export function useRiskStats() {
  const fn = useCallback(() => fetchJson('/api/risk/stats'), [])
  return usePolling(fn, 15000)
}

export function useRiskConfig() {
  const fn = useCallback(() => fetchJson('/api/risk/config'), [])
  return usePolling(fn, 60000)
}

export function useNotifierStats() {
  const fn = useCallback(() => fetchJson('/api/notifier/stats'), [])
  return usePolling(fn, 20000)
}

export function useOrders() {
  const fn = useCallback(() => fetchJson('/api/live/orders?limit=20'), [])
  return usePolling(fn, 20000)
}

export function useTradingStats() {
  const fn = useCallback(() => fetchJson('/api/trading/stats'), [])
  return usePolling(fn, 10000)
}

export function useOpenPositions() {
  const fn = useCallback(() => fetchJson('/api/trading/positions'), [])
  return usePolling(fn, 5000)
}

export function useTradingOrders(symbol, limit = 50) {
  const fn = useCallback(() => {
    const params = new URLSearchParams({ limit: String(limit) })
    if (symbol) params.set('symbol', symbol)
    return fetchJson(`/api/trading/orders?${params}`)
  }, [symbol, limit])
  return usePolling(fn, 15000)
}

export function useActivities() {
  const { data: riskData, loading: riskLoading, error: riskError, lastUpdated: riskUpdated, refresh: refreshRisk } = useRiskStats()
  const { data: notifierData, loading: notifierLoading, error: notifierError, lastUpdated: notifierUpdated, refresh: refreshNotifier } = useNotifierStats()

  const activities = useMemo(
    () => mergeAndSort(riskData?.recentValidations ?? [], notifierData?.recent ?? []),
    [riskData, notifierData]
  )

  return {
    activities,
    loading: riskLoading || notifierLoading,
    error: riskError || notifierError,
    lastUpdated: notifierUpdated ?? riskUpdated,
    refresh: () => { refreshRisk(); refreshNotifier() },
  }
}

export function useCandles(symbol, minutesBack = 60) {
  const fn = useCallback(async () => {
    const end = new Date()
    const start = new Date(end.getTime() - minutesBack * 60 * 1000)
    const url = `/api/dashboard/candles?symbol=${symbol}&interval=1m&startUtc=${start.toISOString()}&endUtc=${end.toISOString()}`
    return fetchJson(url)
  }, [symbol, minutesBack])
  return usePolling(fn, 30000)
}

export function useDailyReport(date) {
  const fn = useCallback(() => {
    const params = date ? `?date=${encodeURIComponent(date)}` : ''
    return fetchJson(`/api/trading/report/daily${params}`)
  }, [date])
  return usePolling(fn, 30000)
}

export function useDailySymbolBreakdown(date) {
  const fn = useCallback(() => {
    const params = date ? `?date=${encodeURIComponent(date)}` : ''
    return fetchJson(`/api/trading/report/daily/symbols${params}`)
  }, [date])
  return usePolling(fn, 30000)
}

export function useDailyTimeAnalytics(date) {
  const fn = useCallback(() => {
    const params = date ? `?date=${encodeURIComponent(date)}` : ''
    return fetchJson(`/api/trading/report/time-analytics${params}`)
  }, [date])
  return usePolling(fn, 30000)
}

export function useDailyHourly(date) {
  const fn = useCallback(() => {
    const params = date ? `?date=${encodeURIComponent(date)}` : ''
    return fetchJson(`/api/trading/report/hourly${params}`)
  }, [date])
  return usePolling(fn, 30000)
}

export function useSystemSettings() {
  const fn = useCallback(() => fetchJson('/api/settings/system'), [])
  return usePolling(fn, 60000)
}

export function usePriceComparison(symbols, minutesBack = 60) {
  const symbolsKey = symbols.join(',')
  const fn = useCallback(async () => {
    const end = new Date()
    const start = new Date(end.getTime() - minutesBack * 60 * 1000)
    const syms = symbolsKey.split(',')
    const symbolParams = syms.map(s => `symbols=${encodeURIComponent(s)}`).join('&')
    const url = `/api/dashboard/candles?symbol=${syms[0]}&${symbolParams}&interval=1m&startUtc=${start.toISOString()}&endUtc=${end.toISOString()}`
    return fetchJson(url)
  }, [symbolsKey, minutesBack])
  return usePolling(fn, 30000)
}
