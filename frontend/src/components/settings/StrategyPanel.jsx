import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

function NumericField({ label, hint, value, onChange, min, step = 'any' }) {
  return (
    <div>
      <label className="block text-sm font-semibold text-gray-700 mb-0.5">{label}</label>
      {hint && <p className="text-xs text-gray-400 mb-1">{hint}</p>}
      <input
        type="number"
        value={value}
        onChange={e => onChange(e.target.value)}
        min={min}
        step={step}
        className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
    </div>
  )
}

export function StrategyPanel() {
  const { t } = useTranslation('settings')
  const [saved, setSaved] = useState(null)
  const [defaultNotional, setDefaultNotional] = useState('25')
  const [minNotional, setMinNotional] = useState('5')
  const [toast, setToast] = useState(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    fetch('/api/settings/strategy')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setSaved(data)
        setDefaultNotional(String(data.defaultOrderNotionalUsdt ?? 25))
        setMinNotional(String(data.minOrderNotionalUsdt ?? 5))
      })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }

  async function handleSave() {
    const def = parseFloat(defaultNotional)
    const min = parseFloat(minNotional)

    if (isNaN(def) || def <= 0) { showToast('error', t('strategy.errors.defaultRequired')); return }
    if (isNaN(min) || min < 0) { showToast('error', t('strategy.errors.minRequired')); return }
    if (min >= def) { showToast('error', t('strategy.errors.rangeInvalid')); return }

    setSaving(true)
    try {
      const res = await fetch('/api/settings/strategy', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          defaultOrderNotionalUsdt: def,
          minOrderNotionalUsdt: min,
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showToast('success', t('strategy.saveSuccess'))
        const updated = await fetch('/api/settings/strategy').then(r => r.json())
        setSaved(updated)
      } else {
        const text = await res.text()
        showToast('error', text || t('strategy.saveFailed'))
      }
    } catch {
      showToast('error', t('strategy.errors.networkError'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      className="rounded-xl p-6 space-y-5"
      style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
    >
      <div>
        <h2 className="text-base font-semibold text-gray-800">{t('strategy.title')}</h2>
        <p className="text-xs text-gray-400 mt-0.5">{t('strategy.subtitle')}</p>
      </div>

      {saved && (
        <div className="text-xs rounded-lg p-3 space-y-1" style={{ background: '#f8fafc', border: '1px solid #e2e8f0' }}>
          <p className="font-semibold text-gray-600 mb-1">{t('strategy.activeValues')}:</p>
          <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 font-mono text-gray-500">
            <span>{t('strategy.defaultOrderNotional')}:</span>
            <span className="text-blue-600">${saved.defaultOrderNotionalUsdt}</span>
            <span>{t('strategy.minOrderNotional')}:</span>
            <span className="text-blue-600">${saved.minOrderNotionalUsdt}</span>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <NumericField
          label={t('strategy.defaultOrderNotional')}
          hint={t('strategy.defaultOrderNotionalHint')}
          value={defaultNotional}
          onChange={setDefaultNotional}
          min="0.01" step="0.01"
        />
        <NumericField
          label={t('strategy.minOrderNotional')}
          hint={t('strategy.minOrderNotionalHint')}
          value={minNotional}
          onChange={setMinNotional}
          min="0" step="0.01"
        />
      </div>

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
        {saving ? t('strategy.saving') : t('strategy.save')}
      </button>

      {saved?.updatedBy && (
        <p className="text-xs text-gray-400">
          {saved.updatedBy}{saved.updatedAtUtc ? ` — ${new Date(saved.updatedAtUtc).toLocaleString()}` : ''}
        </p>
      )}
    </div>
  )
}
