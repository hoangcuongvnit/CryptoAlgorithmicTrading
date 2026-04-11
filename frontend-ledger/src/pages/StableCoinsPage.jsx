import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ledgerApi } from '../services/ledgerApi'

const BINANCE_POLL_MS = 30_000

function fmt(n, digits = 2) {
  if (n === null || n === undefined) return '-'
  return Number(n).toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

export default function StableCoinsPage() {
  const { t } = useTranslation()
  const st = t('stableCoins', { returnObjects: true })

  const [snapshot, setSnapshot] = useState(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState(null)

  const fetchSnapshot = useCallback(async (isRefresh = false) => {
    if (isRefresh) setRefreshing(true)
    else setLoading(true)

    try {
      const payload = await ledgerApi.getBinanceAccount()
      setSnapshot(payload)
      setError(null)
    } catch (e) {
      setError(e?.message ?? st.fetchError)
    } finally {
      if (isRefresh) setRefreshing(false)
      else setLoading(false)
    }
  }, [st.fetchError])

  useEffect(() => {
    fetchSnapshot(false)
    const id = setInterval(() => fetchSnapshot(true), BINANCE_POLL_MS)
    return () => clearInterval(id)
  }, [fetchSnapshot])

  const balances = useMemo(() => {
    const supported = snapshot?.supportedStableCoins ?? []
    const map = new Map((snapshot?.stableCoinBalances ?? []).map((item) => [item.asset, item]))

    return supported.map((coin) => {
      const row = map.get(coin)
      return {
        asset: coin,
        free: row?.free ?? 0,
        locked: row?.locked ?? 0,
        total: row?.total ?? 0,
      }
    })
  }, [snapshot])

  const activeCount = balances.filter((x) => Number(x.total) > 0).length
  const usdtTotal = balances.find((x) => x.asset === 'USDT')?.total ?? 0
  const busdTotal = balances.find((x) => x.asset === 'BUSD')?.total ?? 0

  if (loading) {
    return <p className="text-gray-400">{t('loading')}</p>
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">{st.title}</h2>
          <p className="text-sm text-gray-400 mt-1">{st.subtitle}</p>
          {snapshot?.supportedStableCoins?.length > 0 && (
            <p className="text-xs text-gray-500 mt-1">{st.supported}: {snapshot.supportedStableCoins.join(', ')}</p>
          )}
        </div>

        <div className="flex items-center gap-2">
          {snapshot && (
            <span className={`text-xs px-2 py-0.5 rounded-full ${snapshot.isTestnet ? 'bg-yellow-900 text-yellow-300' : 'bg-green-900 text-green-300'}`}>
              {snapshot.isTestnet ? 'TESTNET' : 'MAINNET'}
            </span>
          )}
          <button
            onClick={() => fetchSnapshot(true)}
            disabled={refreshing}
            className="text-xs px-2 py-1 rounded bg-gray-700 hover:bg-gray-600 text-gray-300 disabled:opacity-50"
          >
            {refreshing ? st.refreshing : st.refresh}
          </button>
        </div>
      </div>

      {error && <p className="text-red-400 text-sm">{t('error')}: {error}</p>}
      {snapshot?.unavailable && <p className="text-yellow-400 text-sm">{snapshot.detail ?? st.unavailable}</p>}

      {!snapshot?.unavailable && snapshot && (
        <>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <MetricCard label={st.cards.totalStable} value={`$${fmt(snapshot.stableCoinTotal)}`} tone="text-cyan-300" />
            <MetricCard label={st.cards.activeStable} value={String(activeCount)} tone="text-green-300" />
            <MetricCard label={st.cards.usdtBusd} value={`USDT: $${fmt(usdtTotal)} | BUSD: $${fmt(busdTotal)}`} tone="text-blue-300" />
          </div>

          <div className="overflow-x-auto rounded-lg border border-gray-700">
            <table className="w-full text-sm">
              <thead className="bg-gray-800 text-gray-400 text-xs">
                <tr>
                  <th className="px-3 py-2 text-left">{st.table.asset}</th>
                  <th className="px-3 py-2 text-right">{st.table.free}</th>
                  <th className="px-3 py-2 text-right">{st.table.locked}</th>
                  <th className="px-3 py-2 text-right">{st.table.total}</th>
                  <th className="px-3 py-2 text-center">{st.table.status}</th>
                </tr>
              </thead>
              <tbody>
                {balances.map((row) => {
                  const hasBalance = Number(row.total) > 0
                  return (
                    <tr key={row.asset} className="border-t border-gray-800 hover:bg-gray-800/40">
                      <td className="px-3 py-2 font-mono text-blue-300">{row.asset}</td>
                      <td className="px-3 py-2 text-right font-mono">{fmt(row.free, 6)}</td>
                      <td className="px-3 py-2 text-right font-mono">{fmt(row.locked, 6)}</td>
                      <td className="px-3 py-2 text-right font-mono">{fmt(row.total, 6)}</td>
                      <td className="px-3 py-2 text-center">
                        <span className={`inline-flex rounded-full px-2 py-0.5 text-[11px] ${hasBalance ? 'bg-green-900 text-green-300' : 'bg-gray-700 text-gray-300'}`}>
                          {hasBalance ? st.status.hasBalance : st.status.zero}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <p className="text-xs text-gray-500 text-right">
            {st.asOf}: {snapshot.asOfUtc ? new Date(snapshot.asOfUtc).toLocaleString() : '-'}
          </p>
        </>
      )}
    </div>
  )
}

function MetricCard({ label, value, tone = 'text-white' }) {
  return (
    <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
      <p className="text-xs text-gray-400 mb-1">{label}</p>
      <p className={`text-xl font-bold font-mono ${tone}`}>{value}</p>
    </div>
  )
}
