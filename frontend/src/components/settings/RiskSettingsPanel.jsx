import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

function NumericField({ label, hint, value, onChange, min, max, step = 'any' }) {
  return (
    <div>
      <label className="block text-sm font-semibold text-gray-700 mb-0.5">{label}</label>
      {hint && <p className="text-xs text-gray-400 mb-1">{hint}</p>}
      <input
        type="number"
        value={value}
        onChange={e => onChange(e.target.value)}
        min={min}
        max={max}
        step={step}
        className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
    </div>
  )
}

export function RiskSettingsPanel() {
  const { t } = useTranslation('settings')
  const [saved, setSaved] = useState(null)
  const [active, setActive] = useState(null)
  const [maxDrawdown, setMaxDrawdown] = useState('5')
  const [minRR, setMinRR] = useState('2')
  const [minOrderNotional, setMinOrderNotional] = useState('5')
  const [maxOrderNotional, setMaxOrderNotional] = useState('200')
  const [cooldown, setCooldown] = useState('30')
  const [toast, setToast] = useState(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    // Load saved settings from DB
    fetch('/api/settings/risk')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setSaved(data)
        setMaxDrawdown(String(data.maxDrawdownPercent))
        setMinRR(String(data.minRiskReward))
        setMinOrderNotional(String(data.minOrderNotional ?? 5))
        setMaxOrderNotional(String(data.maxOrderNotional ?? 200))
        setCooldown(String(data.cooldownSeconds))
      })
      .catch(() => {})

    // Load live values from RiskGuard
    fetch('/api/risk/config')
      .then(r => r.ok ? r.json() : null)
      .then(data => { if (data) setActive(data) })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }

  async function handleSave() {
    const mxd = parseFloat(maxDrawdown)
    const mrr = parseFloat(minRR)
    const mnon = parseFloat(minOrderNotional)
    const mxon = parseFloat(maxOrderNotional)
    const cd = parseInt(cooldown)

    if (isNaN(mxd) || mxd < 0.1 || mxd > 100) { showToast('error', 'Max Drawdown must be between 0.1 and 100'); return }
    if (isNaN(mrr) || mrr < 0.5 || mrr > 10) { showToast('error', 'Min Risk/Reward must be between 0.5 and 10'); return }
    if (isNaN(mnon) || mnon < 0) { showToast('error', 'Min Order Value must be >= 0'); return }
    if (isNaN(mxon) || mxon <= 0) { showToast('error', 'Max Order Value must be > 0'); return }
    if (mnon >= mxon) { showToast('error', 'Min Order Value must be less than Max Order Value'); return }
    if (isNaN(cd) || cd < 0 || cd > 3600) { showToast('error', 'Cooldown must be between 0 and 3600'); return }

    setSaving(true)
    try {
      const res = await fetch('/api/settings/risk', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          maxDrawdownPercent: mxd,
          minRiskReward: mrr,
          minOrderNotional: mnon,
          maxOrderNotional: mxon,
          cooldownSeconds: cd,
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showToast('success', t('risk.saveSuccess'))
        const [updatedSaved, updatedActive] = await Promise.all([
          fetch('/api/settings/risk').then(r => r.json()),
          fetch('/api/risk/config').then(r => r.json()).catch(() => null),
        ])
        setSaved(updatedSaved)
        if (updatedActive) setActive(updatedActive)
      } else {
        const text = await res.text()
        showToast('error', text || t('risk.saveFailed'))
      }
    } catch {
      showToast('error', t('risk.errors.networkError'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      className="rounded-xl p-6 space-y-5"
      style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
    >
      {/* Header */}
      <div>
        <h2 className="text-base font-semibold text-gray-800">{t('risk.title')}</h2>
        <p className="text-xs text-gray-400 mt-0.5">{t('risk.subtitle')}</p>
      </div>

      {/* Live warning */}
      <div className="text-xs px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-amber-700">
        ⚡ {t('risk.liveWarning')}
      </div>

      {/* Active vs Saved comparison */}
      {active && (
        <div
          className="text-xs rounded-lg p-3 space-y-1"
          style={{ background: '#f8fafc', border: '1px solid #e2e8f0' }}
        >
          <p className="font-semibold text-gray-600 mb-1">{t('risk.activeValues')} (RiskGuard):</p>
          <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 font-mono text-gray-500">
            <span>Drawdown:</span><span className="text-blue-600">{active.maxDrawdownPercent}%</span>
            <span>R:R:</span><span className="text-blue-600">{active.minRiskReward}</span>
            <span>Min Order:</span><span className="text-blue-600">${active.minOrderNotional ?? 5}</span>
            <span>Max Order:</span><span className="text-blue-600">${active.maxOrderNotional ?? 200}</span>
            <span>Cooldown:</span><span className="text-blue-600">{active.cooldownSeconds}s</span>
          </div>
        </div>
      )}

      {/* Fields */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <NumericField
          label={t('risk.maxDrawdownPercent')}
          hint={t('risk.maxDrawdownHint')}
          value={maxDrawdown}
          onChange={setMaxDrawdown}
          min="0.1" max="100" step="0.1"
        />
        <NumericField
          label={t('risk.minRiskReward')}
          hint={t('risk.minRiskRewardHint')}
          value={minRR}
          onChange={setMinRR}
          min="0.5" max="10" step="0.1"
        />
        <NumericField
          label={t('risk.minOrderNotional')}
          hint={t('risk.minOrderNotionalHint')}
          value={minOrderNotional}
          onChange={setMinOrderNotional}
          min="0" step="0.01"
        />
        <NumericField
          label={t('risk.maxOrderNotional')}
          hint={t('risk.maxOrderNotionalHint')}
          value={maxOrderNotional}
          onChange={setMaxOrderNotional}
          min="0.01" step="0.01"
        />
        <NumericField
          label={t('risk.cooldownSeconds')}
          hint={t('risk.cooldownHint')}
          value={cooldown}
          onChange={setCooldown}
          min="0" max="3600" step="1"
        />
      </div>

      {/* Toast */}
      {toast && (
        <div className={`text-sm px-4 py-2 rounded-lg border ${
          toast.type === 'success'
            ? 'bg-green-50 text-green-700 border-green-200'
            : 'bg-red-50 text-red-700 border-red-200'
        }`}>
          {toast.message}
        </div>
      )}

      <button
        onClick={handleSave}
        disabled={saving}
        className="w-full py-2.5 text-sm font-semibold text-white rounded-lg transition-opacity disabled:opacity-40"
        style={{ background: '#2f6fed' }}
      >
        {saving ? t('risk.saving') : t('risk.save')}
      </button>

      {saved?.updatedBy && (
        <p className="text-xs text-gray-400">
          {saved.updatedBy}{saved.updatedAtUtc ? ` — ${new Date(saved.updatedAtUtc).toLocaleString()}` : ''}
        </p>
      )}
    </div>
  )
}
