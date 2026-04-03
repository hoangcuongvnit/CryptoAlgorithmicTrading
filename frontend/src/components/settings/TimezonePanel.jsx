import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../context/SettingsContext.jsx'

const TIMEZONES = [
  'UTC',
  'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles',
  'America/Anchorage', 'America/Honolulu', 'America/Toronto', 'America/Vancouver',
  'America/Sao_Paulo', 'America/Argentina/Buenos_Aires', 'America/Mexico_City',
  'America/Bogota', 'America/Lima',
  'Europe/London', 'Europe/Paris', 'Europe/Berlin', 'Europe/Madrid', 'Europe/Rome',
  'Europe/Amsterdam', 'Europe/Brussels', 'Europe/Vienna', 'Europe/Zurich',
  'Europe/Stockholm', 'Europe/Copenhagen', 'Europe/Oslo', 'Europe/Helsinki',
  'Europe/Warsaw', 'Europe/Prague', 'Europe/Budapest', 'Europe/Athens',
  'Europe/Istanbul', 'Europe/Moscow', 'Europe/Kiev',
  'Asia/Dubai', 'Asia/Riyadh', 'Asia/Kolkata', 'Asia/Colombo',
  'Asia/Dhaka', 'Asia/Yangon', 'Asia/Bangkok', 'Asia/Ho_Chi_Minh',
  'Asia/Jakarta', 'Asia/Singapore', 'Asia/Kuala_Lumpur', 'Asia/Manila',
  'Asia/Hong_Kong', 'Asia/Shanghai', 'Asia/Taipei', 'Asia/Seoul',
  'Asia/Tokyo', 'Asia/Vladivostok',
  'Australia/Perth', 'Australia/Darwin', 'Australia/Adelaide',
  'Australia/Brisbane', 'Australia/Sydney', 'Australia/Melbourne',
  'Pacific/Auckland', 'Pacific/Fiji', 'Pacific/Honolulu',
  'Africa/Cairo', 'Africa/Johannesburg', 'Africa/Lagos', 'Africa/Nairobi',
]

function tzOffset(tz) {
  try {
    const fmt = new Intl.DateTimeFormat('en', { timeZone: tz, timeZoneName: 'short' })
    const parts = fmt.formatToParts(new Date())
    return parts.find(p => p.type === 'timeZoneName')?.value ?? ''
  } catch {
    return ''
  }
}

export function TimezonePanel() {
  const { t } = useTranslation('settings')
  const { systemTimezone, setSystemTimezone } = useSettings()
  const [selected, setSelected] = useState(systemTimezone)
  const [search, setSearch] = useState('')
  const [saving, setSaving] = useState(false)
  const [toast, setToast] = useState(null)

  useEffect(() => { setSelected(systemTimezone) }, [systemTimezone])

  const filtered = TIMEZONES.filter(tz =>
    tz.toLowerCase().includes(search.toLowerCase())
  )

  async function handleSave() {
    setSaving(true)
    setToast(null)
    try {
      const res = await fetch('/api/settings/system/timezone', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ timezone: selected }),
      })
      if (res.ok) {
        setSystemTimezone(selected)
        setToast({ type: 'success', message: t('saveSuccess') })
      } else {
        const text = await res.text()
        setToast({ type: 'error', message: text || t('saveError') })
      }
    } catch {
      setToast({ type: 'error', message: t('saveError') })
    } finally {
      setSaving(false)
    }
  }

  const previewTime = selected
    ? new Date().toLocaleTimeString('en-US', {
        hour: '2-digit', minute: '2-digit', second: '2-digit',
        hour12: false, timeZone: selected,
      })
    : ''

  return (
    <div className="space-y-4">
      <div
        className="rounded-xl p-6 space-y-5"
        style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
      >
        <div>
          <label className="block text-sm font-semibold text-gray-700 mb-1">
            {t('timezone.label')}
          </label>
          <p className="text-xs text-gray-400 mb-3">{t('timezone.description')}</p>

          <div className="flex gap-2">
            <input
              type="text"
              placeholder={t('timezone.search')}
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="flex-1 px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <select
              value={selected}
              onChange={e => { setSelected(e.target.value); setSearch('') }}
              className="flex-1 px-2 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono bg-white"
            >
              {filtered.map(tz => (
                <option key={tz} value={tz}>
                  {tz} ({tzOffset(tz)})
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="text-sm space-y-1">
          <div className="flex items-center gap-2">
            <span className="text-gray-500 w-24 shrink-0">{t('timezone.active')}:</span>
            <span className="font-medium text-blue-600">{systemTimezone}</span>
          </div>
          {selected !== systemTimezone && (
            <div className="flex items-center gap-2">
              <span className="text-gray-500 w-24 shrink-0">{t('timezone.pending')}:</span>
              <span className="font-medium text-orange-500">{selected}</span>
            </div>
          )}
          {selected && (
            <div className="flex items-center gap-2">
              <span className="text-gray-500 w-24 shrink-0">{t('timezone.preview')}:</span>
              <span className="font-mono text-gray-700">{previewTime}</span>
            </div>
          )}
        </div>

        {toast && (
          <div
            className={`text-sm px-4 py-2 rounded-lg border ${
              toast.type === 'success'
                ? 'bg-green-50 text-green-700 border-green-200'
                : 'bg-red-50 text-red-700 border-red-200'
            }`}
          >
            {toast.message}
          </div>
        )}

        <button
          onClick={handleSave}
          disabled={saving || selected === systemTimezone}
          className="w-full py-2.5 text-sm font-semibold text-white rounded-lg transition-opacity disabled:opacity-40"
          style={{ background: '#2f6fed' }}
        >
          {saving ? t('saving') : t('save')}
        </button>
      </div>

      <div
        className="rounded-xl p-4 text-xs text-gray-500 space-y-1"
        style={{ background: '#f8fafc', border: '1px solid #e2e8f0' }}
      >
        <p className="font-semibold text-gray-700 mb-2">{t('note.title')}</p>
        <p>• {t('note.utcStorage')}</p>
        <p>• {t('note.displayOnly')}</p>
        <p>• {t('note.immediate')}</p>
      </div>
    </div>
  )
}
