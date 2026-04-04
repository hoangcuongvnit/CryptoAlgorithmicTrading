import { useEffect, useState } from 'react'
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

export function OrderAmountLimitsPanel() {
  const { t } = useTranslation('settings')
  const [saved, setSaved] = useState(null)
  const [minOrderAmount, setMinOrderAmount] = useState('5')
  const [maxOrderAmount, setMaxOrderAmount] = useState('1000')
  const [toast, setToast] = useState(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    fetch('/api/settings/order-amount-limit')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setSaved(data)
        if (data.minOrderAmount !== undefined && data.minOrderAmount !== null) {
          setMinOrderAmount(String(data.minOrderAmount))
        }
        if (data.maxOrderAmount !== undefined && data.maxOrderAmount !== null) {
          setMaxOrderAmount(String(data.maxOrderAmount))
        }
      })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }

  async function handleSave() {
    if (minOrderAmount === '' || minOrderAmount === null) {
      showToast('error', t('orderAmountLimit.errors.minRequired'))
      return
    }
    if (maxOrderAmount === '' || maxOrderAmount === null) {
      showToast('error', t('orderAmountLimit.errors.maxRequired'))
      return
    }

    const min = parseFloat(minOrderAmount)
    const max = parseFloat(maxOrderAmount)

    if (Number.isNaN(min) || min <= 0) {
      showToast('error', t('orderAmountLimit.errors.minRequired'))
      return
    }
    if (Number.isNaN(max) || max <= 0) {
      showToast('error', t('orderAmountLimit.errors.maxRequired'))
      return
    }
    if (min > max) {
      showToast('error', t('orderAmountLimit.errors.rangeInvalid'))
      return
    }

    setSaving(true)
    try {
      const res = await fetch('/api/settings/order-amount-limit', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          minOrderAmount: min,
          maxOrderAmount: max,
          updatedBy: 'admin',
        }),
      })

      if (res.ok) {
        showToast('success', t('orderAmountLimit.saveSuccess'))
        const updated = await fetch('/api/settings/order-amount-limit').then(r => r.json())
        setSaved(updated)
      } else {
        const text = await res.text()
        showToast('error', text || t('orderAmountLimit.saveFailed'))
      }
    } catch {
      showToast('error', t('orderAmountLimit.errors.networkError'))
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
        <h2 className="text-base font-semibold text-gray-800">{t('orderAmountLimit.title')}</h2>
        <p className="text-xs text-gray-400 mt-0.5">{t('orderAmountLimit.subtitle')}</p>
      </div>

      {saved && (
        <div className="text-xs rounded-lg p-3 space-y-1" style={{ background: '#f8fafc', border: '1px solid #e2e8f0' }}>
          <p className="font-semibold text-gray-600 mb-1">{t('orderAmountLimit.activeValues')}:</p>
          <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 font-mono text-gray-500">
            <span>{t('orderAmountLimit.minOrderAmount')}:</span><span className="text-blue-600">{saved.minOrderAmount}</span>
            <span>{t('orderAmountLimit.maxOrderAmount')}:</span><span className="text-blue-600">{saved.maxOrderAmount}</span>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <NumericField
          label={t('orderAmountLimit.minOrderAmount')}
          hint={t('orderAmountLimit.minOrderAmountHint')}
          value={minOrderAmount}
          onChange={setMinOrderAmount}
          min="0"
          step="0.00000001"
        />
        <NumericField
          label={t('orderAmountLimit.maxOrderAmount')}
          hint={t('orderAmountLimit.maxOrderAmountHint')}
          value={maxOrderAmount}
          onChange={setMaxOrderAmount}
          min="0"
          step="0.00000001"
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
        {saving ? t('orderAmountLimit.saving') : t('orderAmountLimit.save')}
      </button>

      {saved?.updatedBy && (
        <p className="text-xs text-gray-400">
          {saved.updatedBy}{saved.updatedAtUtc ? ` — ${new Date(saved.updatedAtUtc).toLocaleString()}` : ''}
        </p>
      )}
    </div>
  )
}