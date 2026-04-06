import { useEffect, useState, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useLedgerSignalR } from '../hooks/useLedgerSignalR'
import { ledgerApi } from '../services/ledgerApi'

function fmt(n, digits = 4) {
  const v = Number(n)
  return v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

function sign(n) { return Number(n) >= 0 ? '+' : '' }

export default function PnLBreakdownPage() {
  const { t } = useTranslation()
  const [sessionId, setSessionId] = useState(null)
  const [breakdown, setBreakdown] = useState(null)
  const [loading, setLoading]     = useState(true)
  const [error, setError]         = useState(null)

  useEffect(() => {
    const env = import.meta.env.VITE_DEFAULT_ENVIRONMENT ?? 'TESTNET'
    ledgerApi.bootstrap(env)
      .then((d) => {
        if (d.activeSession?.id) {
          setSessionId(d.activeSession.id)
          return d.activeSession.id
        }

        return ledgerApi.getSessions(d.accountId, 'ACTIVE').then((sessions) => {
          const active = sessions.find((s) => s.status === 'ACTIVE')
          if (!active?.id) {
            throw new Error('No active session found')
          }

          setSessionId(active.id)
          return active.id
        })
      })
      .then((sid) => ledgerApi.getPnl(sid))
      .then(setBreakdown)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  const reload = useCallback(() => {
    if (!sessionId) return
    ledgerApi.getPnl(sessionId).then(setBreakdown).catch(console.error)
  }, [sessionId])

  useLedgerSignalR({ onEntry: reload })

  if (loading) return <p className="text-gray-400">{t('loading')}</p>
  if (error)   return <p className="text-red-400">{error}</p>

  const rows = breakdown ? Object.entries(breakdown) : []

  // Totals
  const totals = rows.reduce((acc, [, b]) => ({
    realized: acc.realized + Number(b.realizedPnl),
    commission: acc.commission + Number(b.commission),
    funding: acc.funding + Number(b.fundingFee),
    net: acc.net + Number(b.netPnl),
  }), { realized: 0, commission: 0, funding: 0, net: 0 })

  return (
    <div className="space-y-4">
      <h2 className="text-xl font-semibold">{t('pnl.title')}</h2>

      {rows.length === 0 ? (
        <p className="text-gray-400 text-sm">{t('pnl.empty')}</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-sm border border-gray-700 rounded-lg overflow-hidden">
              <thead className="bg-gray-800 text-gray-400 text-xs">
                <tr>
                  {['symbol', 'realizedPnl', 'commission', 'fundingFee', 'netPnl'].map((h) => (
                    <th key={h} className="px-3 py-2 text-left">{t(`pnl.col.${h}`)}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map(([symbol, b]) => (
                  <tr key={symbol} className="border-t border-gray-800 hover:bg-gray-800/50">
                    <td className="px-3 py-2 font-mono text-blue-300">{symbol}</td>
                    <td className={`px-3 py-2 font-mono ${Number(b.realizedPnl) >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                      {sign(b.realizedPnl)}{fmt(b.realizedPnl)}
                    </td>
                    <td className="px-3 py-2 font-mono text-yellow-400">{fmt(b.commission)}</td>
                    <td className="px-3 py-2 font-mono text-orange-400">{fmt(b.fundingFee)}</td>
                    <td className={`px-3 py-2 font-mono font-bold ${Number(b.netPnl) >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                      {sign(b.netPnl)}{fmt(b.netPnl)}
                    </td>
                  </tr>
                ))}
              </tbody>
              {/* Totals row */}
              <tfoot className="bg-gray-800 text-xs font-semibold border-t-2 border-gray-600">
                <tr>
                  <td className="px-3 py-2 text-gray-300">{t('pnl.total')}</td>
                  <td className={`px-3 py-2 font-mono ${totals.realized >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                    {sign(totals.realized)}{fmt(totals.realized)}
                  </td>
                  <td className="px-3 py-2 font-mono text-yellow-400">{fmt(totals.commission)}</td>
                  <td className="px-3 py-2 font-mono text-orange-400">{fmt(totals.funding)}</td>
                  <td className={`px-3 py-2 font-mono ${totals.net >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                    {sign(totals.net)}{fmt(totals.net)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          {/* Net PnL summary card */}
          <div className="bg-gray-800 border border-gray-700 rounded-lg p-4 inline-block">
            <p className="text-xs text-gray-400 mb-1">{t('pnl.totalNet')}</p>
            <p className={`text-3xl font-bold font-mono ${totals.net >= 0 ? 'text-green-400' : 'text-red-400'}`}>
              {sign(totals.net)}${fmt(totals.net, 2)}
            </p>
          </div>
        </>
      )}
    </div>
  )
}
