import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ledgerApi } from '../services/ledgerApi'

function fmt(n) {
  return Number(n).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

export default function SessionsPage() {
  const { t } = useTranslation()
  const [accountId, setAccountId]         = useState(null)
  const [sessions, setSessions]           = useState([])
  const [statusFilter, setStatusFilter]   = useState('ALL')
  const [loading, setLoading]             = useState(true)
  const [error, setError]                 = useState(null)
  const [resetting, setResetting]         = useState(false)
  const [resetForm, setResetForm]         = useState({ balance: '', name: '' })

  // Bootstrap on mount
  useEffect(() => {
    const env = import.meta.env.VITE_DEFAULT_ENVIRONMENT ?? 'TESTNET'
    ledgerApi.bootstrap(env)
      .then((d) => { setAccountId(d.accountId); return d.accountId })
      .then((id) => ledgerApi.getSessions(id, 'ALL'))
      .then(setSessions)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  const reloadSessions = () => {
    if (!accountId) return
    ledgerApi.getSessions(accountId, statusFilter).then(setSessions).catch(console.error)
  }

  useEffect(() => { reloadSessions() }, [statusFilter, accountId])

  const handleReset = async (e) => {
    e.preventDefault()
    const bal = parseFloat(resetForm.balance)
    if (!bal || bal <= 0) { alert(t('sessions.invalidBalance')); return }
    setResetting(true)
    try {
      await ledgerApi.resetSession(accountId, bal, resetForm.name || 'DEFAULT')
      setResetForm({ balance: '', name: '' })
      reloadSessions()
    } catch (err) {
      alert(err.message)
    } finally {
      setResetting(false)
    }
  }

  const visible = sessions.filter((s) => statusFilter === 'ALL' || s.status === statusFilter)

  return (
    <div className="space-y-6">
      <h2 className="text-xl font-semibold">{t('sessions.title')}</h2>

      {/* Status filter */}
      <div className="flex gap-2">
        {['ALL', 'ACTIVE', 'ARCHIVED'].map((s) => (
          <button
            key={s}
            onClick={() => setStatusFilter(s)}
            className={`text-xs px-3 py-1.5 rounded-full border ${
              statusFilter === s
                ? 'bg-blue-600 border-blue-500 text-white'
                : 'border-gray-600 text-gray-400 hover:border-gray-400'
            }`}
          >
            {s}
          </button>
        ))}
      </div>

      {error   && <p className="text-red-400 text-sm">{error}</p>}
      {loading && <p className="text-gray-400 text-sm">{t('loading')}</p>}

      {/* Sessions table */}
      <div className="overflow-x-auto">
        <table className="w-full text-sm border border-gray-700 rounded-lg overflow-hidden">
          <thead className="bg-gray-800 text-gray-400 text-xs">
            <tr>
              {['algorithm', 'status', 'initialBalance', 'startTime', 'endTime'].map((h) => (
                <th key={h} className="px-3 py-2 text-left">{t(`sessions.col.${h}`)}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {visible.map((s) => (
              <tr key={s.id} className="border-t border-gray-800 hover:bg-gray-800/50">
                <td className="px-3 py-2 font-mono text-blue-300">{s.algorithmName}</td>
                <td className="px-3 py-2">
                  <span className={`text-xs px-1.5 py-0.5 rounded ${s.status === 'ACTIVE' ? 'bg-green-900 text-green-300' : 'bg-gray-700 text-gray-400'}`}>
                    {s.status}
                  </span>
                </td>
                <td className="px-3 py-2 font-mono">${fmt(s.initialBalance)}</td>
                <td className="px-3 py-2 text-gray-400 text-xs">{new Date(s.startTime).toLocaleString()}</td>
                <td className="px-3 py-2 text-gray-400 text-xs">{s.endTime ? new Date(s.endTime).toLocaleString() : '—'}</td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr><td colSpan={5} className="px-3 py-6 text-center text-gray-500">{t('sessions.empty')}</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Session reset form */}
      <div className="bg-gray-800 border border-gray-700 rounded-lg p-4 max-w-md">
        <h3 className="text-sm font-medium mb-3">{t('sessions.resetTitle')}</h3>
        <form onSubmit={handleReset} className="space-y-3">
          <input
            type="number"
            step="0.01"
            min="0.01"
            placeholder={t('sessions.newBalance')}
            value={resetForm.balance}
            onChange={(e) => setResetForm((p) => ({ ...p, balance: e.target.value }))}
            className="w-full bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-gray-200 placeholder-gray-500"
            required
          />
          <input
            type="text"
            placeholder={t('sessions.algorithmName')}
            value={resetForm.name}
            onChange={(e) => setResetForm((p) => ({ ...p, name: e.target.value }))}
            className="w-full bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-gray-200 placeholder-gray-500"
          />
          <button
            type="submit"
            disabled={resetting || !accountId}
            className="w-full bg-red-700 hover:bg-red-600 disabled:opacity-40 text-white text-sm font-medium py-2 rounded"
          >
            {resetting ? t('sessions.resetting') : t('sessions.resetBtn')}
          </button>
        </form>
      </div>
    </div>
  )
}
