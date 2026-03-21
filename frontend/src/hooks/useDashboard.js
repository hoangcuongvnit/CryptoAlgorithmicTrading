import { useCallback } from 'react'
import { usePolling } from './usePolling.js'

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

export function useCandles(symbol, minutesBack = 60) {
  const fn = useCallback(async () => {
    const end = new Date()
    const start = new Date(end.getTime() - minutesBack * 60 * 1000)
    const url = `/api/dashboard/candles?symbol=${symbol}&interval=1m&startUtc=${start.toISOString()}&endUtc=${end.toISOString()}`
    return fetchJson(url)
  }, [symbol, minutesBack])
  return usePolling(fn, 30000)
}
