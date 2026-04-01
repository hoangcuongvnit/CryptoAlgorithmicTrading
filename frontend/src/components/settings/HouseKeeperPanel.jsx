import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

function Toggle({ label, value, onChange }) {
  return (
    <label className="flex items-center gap-3 cursor-pointer select-none">
      <div
        onClick={() => onChange(!value)}
        className="relative w-10 h-5 rounded-full transition-colors"
        style={{ background: value ? '#2f6fed' : '#cbd5e1' }}
      >
        <div
          className="absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform"
          style={{ left: value ? '1.25rem' : '0.125rem' }}
        />
      </div>
      <span className="text-sm font-medium text-gray-700">{label}</span>
    </label>
  )
}

export function HouseKeeperPanel() {
  const { t } = useTranslation('settings')
  const [enabled, setEnabled] = useState(true)
  const [dryRun, setDryRun] = useState(true)
  const [schedule, setSchedule] = useState('03:15')
  const [retentionOrders, setRetentionOrders] = useState('365')
  const [retentionGaps, setRetentionGaps] = useState('60')
  const [retentionTicks, setRetentionTicks] = useState('12')
  const [batchSize, setBatchSize] = useState('5000')
  const [maxRunSeconds, setMaxRunSeconds] = useState('600')
  const [saved, setSaved] = useState(null)
  const [toast, setToast] = useState(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    fetch('/api/settings/housekeeper')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setSaved(data)
        setEnabled(data.enabled)
        setDryRun(data.dryRun)
        setSchedule(data.scheduleUtc)
        setRetentionOrders(String(data.retentionOrdersDays))
        setRetentionGaps(String(data.retentionGapsDays))
        setRetentionTicks(String(data.retentionTicksMonths))
        setBatchSize(String(data.batchSize))
        setMaxRunSeconds(String(data.maxRunSeconds))
      })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }

  async function handleSave() {
    const scheduleValid = /^\d{2}:\d{2}$/.test(schedule.trim())
    if (!scheduleValid) { showToast('error', t('housekeeper.errors.invalidSchedule')); return }

    setSaving(true)
    try {
      const res = await fetch('/api/settings/housekeeper', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          enabled,
          dryRun,
          scheduleUtc: schedule.trim(),
          retentionOrdersDays: parseInt(retentionOrders),
          retentionGapsDays: parseInt(retentionGaps),
          retentionTicksMonths: parseInt(retentionTicks),
          batchSize: parseInt(batchSize),
          maxRunSeconds: parseInt(maxRunSeconds),
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showToast('success', t('housekeeper.saveSuccess'))
        const updated = await fetch('/api/settings/housekeeper').then(r => r.json())
        setSaved(updated)
      } else {
        const text = await res.text()
        showToast('error', text || t('housekeeper.saveFailed'))
      }
    } catch {
      showToast('error', t('housekeeper.errors.networkError'))
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
        <h2 className="text-base font-semibold text-gray-800">{t('housekeeper.title')}</h2>
        <p className="text-xs text-gray-400 mt-0.5">{t('housekeeper.subtitle')}</p>
      </div>

      {/* Restart warning */}
      <div className="text-xs px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-amber-700">
        ⚠ {t('housekeeper.restartWarning')}
      </div>

      {/* Toggles */}
      <div className="space-y-3">
        <Toggle label={t('housekeeper.enabled')} value={enabled} onChange={setEnabled} />
        <Toggle label={t('housekeeper.dryRun')} value={dryRun} onChange={setDryRun} />
      </div>

      {/* Schedule */}
      <div>
        <label className="block text-sm font-semibold text-gray-700 mb-0.5">{t('housekeeper.scheduleUtc')}</label>
        <p className="text-xs text-gray-400 mb-1">{t('housekeeper.scheduleHint')}</p>
        <input
          type="text"
          value={schedule}
          onChange={e => setSchedule(e.target.value)}
          placeholder="03:15"
          className="w-40 px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
        />
      </div>

      {/* Retention + batch fields */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { label: t('housekeeper.retentionOrdersDays'), value: retentionOrders, set: setRetentionOrders, min: 1 },
          { label: t('housekeeper.retentionGapsDays'), value: retentionGaps, set: setRetentionGaps, min: 1 },
          { label: t('housekeeper.retentionTicksMonths'), value: retentionTicks, set: setRetentionTicks, min: 1 },
          { label: t('housekeeper.batchSize'), value: batchSize, set: setBatchSize, min: 100 },
          { label: t('housekeeper.maxRunSeconds'), value: maxRunSeconds, set: setMaxRunSeconds, min: 60 },
        ].map(({ label, value, set, min }) => (
          <div key={label}>
            <label className="block text-xs font-semibold text-gray-600 mb-1">{label}</label>
            <input
              type="number"
              value={value}
              onChange={e => set(e.target.value)}
              min={min}
              className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
        ))}
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
        {saving ? t('housekeeper.saving') : t('housekeeper.save')}
      </button>

      {saved?.updatedBy && (
        <p className="text-xs text-gray-400">
          {saved.updatedBy}{saved.updatedAtUtc ? ` — ${new Date(saved.updatedAtUtc).toLocaleString()}` : ''}
        </p>
      )}
    </div>
  )
}
