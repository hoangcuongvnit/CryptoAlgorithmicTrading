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

export function useSession() {
  const fn = useCallback(() => fetchJson('/api/trading/session'), [])
  return usePolling(fn, 30000)
}

export function useRiskConfig() {
  const fn = useCallback(() => fetchJson('/api/risk/config'), [])
  return usePolling(fn, 60000)
}

export function useNotifierStats() {
  const fn = useCallback(() => fetchJson('/api/notifier/stats?limit=30'), [])
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

export function useLatestReconciliationDrift() {
  const fn = useCallback(() => fetchJson('/api/trading/reconciliation/latest'), [])
  return usePolling(fn, 15000)
}

export function useEffectiveBalance() {
  const fn = useCallback(() => fetchJson('/api/trading/balance/effective'), [])
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
    () => mergeAndSort(riskData?.recentValidations ?? [], notifierData?.recentNotifications ?? notifierData?.recent ?? []),
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

export function useSessionDailyReport(date, mode) {
  const fn = useCallback(() => {
    const params = new URLSearchParams()
    if (date) params.set('date', date)
    if (mode) params.set('mode', mode)
    return fetchJson(`/api/trading/report/sessions/daily?${params}`)
  }, [date, mode])
  return usePolling(fn, 30000)
}

export function useSessionRangeReport(from, to, mode) {
  const fn = useCallback(() => {
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to)   params.set('to', to)
    if (mode) params.set('mode', mode)
    return fetchJson(`/api/trading/report/sessions/range?${params}`)
  }, [from, to, mode])
  return usePolling(fn, 30000)
}

export function useSessionSymbols(sessionId, mode) {
  const fn = useCallback(() => {
    if (!sessionId) return Promise.resolve([])
    const params = mode ? `?mode=${mode}` : ''
    return fetchJson(`/api/trading/report/sessions/${encodeURIComponent(sessionId)}/symbols${params}`)
  }, [sessionId, mode])
  return usePolling(fn, 30000)
}

export function useSessionEquityCurve(from, to, mode) {
  const fn = useCallback(() => {
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to)   params.set('to', to)
    if (mode) params.set('mode', mode)
    return fetchJson(`/api/trading/report/sessions/equity-curve?${params}`)
  }, [from, to, mode])
  return usePolling(fn, 30000)
}

export function useRiskEvaluations({ symbol, outcome, from, to, sessionId, page, pageSize } = {}) {
  const fn = useCallback(() => {
    const params = new URLSearchParams()
    if (symbol)    params.set('symbol', symbol)
    if (outcome)   params.set('outcome', outcome)
    if (from)      params.set('from', from)
    if (to)        params.set('to', to)
    if (sessionId) params.set('sessionId', sessionId)
    params.set('page', String(page ?? 1))
    params.set('pageSize', String(pageSize ?? 20))
    return fetchJson(`/api/risk-evaluations?${params}`)
  }, [symbol, outcome, from, to, sessionId, page, pageSize])
  return usePolling(fn, 15000)
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

export function useCapitalFlow({ from, to, mode = 'live' } = {}) {
  const fn = useCallback(() => {
    const params = new URLSearchParams({ mode })
    if (from) params.set('from', from)
    if (to)   params.set('to', to)
    return fetchJson(`/api/trading/report/capital-flow?${params}`)
  }, [from, to, mode])
  return usePolling(fn, 30000)
}

// ── Shutdown / Close-All ─────────────────────────────────────────────────

async function postJson(url, body) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) throw Object.assign(new Error(data?.error ?? `HTTP ${res.status}`), { data })
  return data
}

export function useCloseAllStatus() {
  const fn = useCallback(() => fetchJson('/api/control/close-all/status'), [])
  return usePolling(fn, 5000)
}

export function useCloseAllHistory(limit = 10) {
  const fn = useCallback(() => fetchJson(`/api/control/close-all/history?limit=${limit}`), [limit])
  return usePolling(fn, 30000)
}

export async function apiCloseAllNow({ reason, requestedBy, idempotencyKey }) {
  return postJson('/api/control/close-all', {
    reason,
    requestedBy,
    confirmationToken: 'CLOSE ALL',
    idempotencyKey,
  })
}

export async function apiScheduleCloseAll({ executeAtUtc, reason, requestedBy, idempotencyKey }) {
  return postJson('/api/control/close-all/schedule', {
    executeAtUtc,
    reason,
    requestedBy,
    confirmationToken: 'CLOSE ALL',
    idempotencyKey,
  })
}

export async function apiCancelCloseAll(operationId) {
  return postJson('/api/control/close-all/cancel', { operationId })
}

export async function apiResumeTrading({ reason, requestedBy }) {
  return postJson('/api/control/trading/resume', {
    reason,
    requestedBy,
    confirmationToken: 'RESUME TRADING',
  })
}

export function useSymbolTimeline(symbol, minutesBack) {
  const fn = useCallback(() => {
    if (!symbol || !minutesBack) return Promise.resolve(null)
    const params = new URLSearchParams({ symbol, minutesBack: String(minutesBack) })
    return fetchJson(`/api/timeline/symbol?${params}`)
  }, [symbol, minutesBack])
  return usePolling(fn, 30000)
}

export function useTimelineEvents(symbol, { startDate, endDate, eventType, eventCategory, limit = 100, offset = 0 } = {}) {
  const fn = useCallback(() => {
    if (!symbol) return Promise.resolve(null)
    const params = new URLSearchParams({ symbol, limit: String(limit), offset: String(offset) })
    if (startDate) params.set('startDate', startDate)
    if (endDate) params.set('endDate', endDate)
    if (eventType) params.set('eventType', eventType)
    if (eventCategory) params.set('eventCategory', eventCategory)
    return fetchJson(`/api/timeline/events?${params}`)
  }, [symbol, startDate, endDate, eventType, eventCategory, limit, offset])
  return usePolling(fn, 30000)
}

export function useTimelineSummary(symbol, date) {
  const fn = useCallback(() => {
    if (!symbol) return Promise.resolve(null)
    const params = new URLSearchParams({ symbol })
    if (date) params.set('date', date)
    return fetchJson(`/api/timeline/summary?${params}`)
  }, [symbol, date])
  return usePolling(fn, 60000)
}

export function useTimelineDashboard(days = 7) {
  const fn = useCallback(() => {
    return fetchJson(`/api/timeline/dashboard?days=${days}`)
  }, [days])
  return usePolling(fn, 60000)
}

