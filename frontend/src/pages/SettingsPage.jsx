import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../context/SettingsContext.jsx'

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

const STATUS_STYLES = {
  connected:    { bg: '#f0fdf4', border: '#86efac', text: '#15803d' },
  notConfigured:{ bg: '#f8fafc', border: '#cbd5e1', text: '#64748b' },
  invalid:      { bg: '#fef2f2', border: '#fca5a5', text: '#dc2626' },
  failed:       { bg: '#fff7ed', border: '#fdba74', text: '#c2410c' },
}

function TelegramStatusBadge({ status, t }) {
  const key = status === 'success' ? 'connected'
    : status === 'failed'  ? 'failed'
    : status === 'invalid' ? 'invalid'
    : 'notConfigured'
  const s = STATUS_STYLES[key]
  const label = t(`telegram.status.${key}`)
  return (
    <span className="text-xs font-semibold px-2 py-0.5 rounded-full"
      style={{ background: s.bg, border: `1px solid ${s.border}`, color: s.text }}>
      {label}
    </span>
  )
}

function TelegramPanel({ t }) {
  const [cfg, setCfg] = useState(null)          // loaded from GET
  const [botToken, setBotToken] = useState('')
  const [chatId, setChatId] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [showGuide, setShowGuide] = useState(false)
  const [actionToast, setActionToast] = useState(null)
  const [validating, setValidating] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [healthChecking, setHealthChecking] = useState(false)

  useEffect(() => {
    fetch('/api/settings/notifications/telegram')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setCfg(data)
        setEnabled(data.enabled)
        if (data.chatIdMasked) setChatId(data.chatIdMasked)
      })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setActionToast({ type, message })
    setTimeout(() => setActionToast(null), 4000)
  }

  const usingSaved = !!(cfg?.isConfigured && !botToken.trim())

  async function handleValidate() {
    if (!usingSaved) {
      if (!botToken.trim()) { showToast('error', t('telegram.errors.tokenRequired')); return }
      const rawChatId = parseInt(chatId.replace(/\*/g, ''), 10)
      if (!rawChatId) { showToast('error', t('telegram.errors.chatIdRequired')); return }
    }
    setValidating(true)
    try {
      let res
      if (usingSaved) {
        res = await fetch('/api/settings/notifications/telegram/validate-saved', { method: 'POST' })
      } else {
        const rawChatId = parseInt(chatId.replace(/\*/g, ''), 10)
        res = await fetch('/api/settings/notifications/telegram/validate', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ botToken: botToken.trim(), chatId: rawChatId }),
        })
      }
      let data
      try { data = await res.json() } catch { data = {} }
      if (data.valid) {
        showToast('success', `${t('telegram.validateSuccess')} (@${data.botUsername})`)
      } else {
        showToast('error', data.message || t('telegram.validateFailed'))
      }
    } catch {
      showToast('error', t('telegram.errors.networkError'))
    } finally {
      setValidating(false)
    }
  }

  async function handleSave() {
    const rawChatId = parseInt(chatId.replace(/\*/g, ''), 10) || null
    if (!cfg?.isConfigured && !botToken.trim()) {
      showToast('error', t('telegram.errors.tokenRequired')); return
    }
    if (!rawChatId) { showToast('error', t('telegram.errors.chatIdRequired')); return }

    setSaving(true)
    try {
      const res = await fetch('/api/settings/notifications/telegram', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          enabled,
          botToken: botToken.trim() || undefined,
          chatId: rawChatId,
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showToast('success', t('telegram.saveSuccess'))
        // Refresh displayed config
        const updated = await fetch('/api/settings/notifications/telegram').then(r => r.json())
        setCfg(updated)
        setBotToken('')
      } else {
        const text = await res.text()
        showToast('error', text || t('telegram.saveFailed'))
      }
    } catch {
      showToast('error', t('telegram.errors.networkError'))
    } finally {
      setSaving(false)
    }
  }

  async function handleTestMessage() {
    setTesting(true)
    try {
      const res = await fetch('/api/settings/notifications/telegram/test-message', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: t('telegram.testMessageText') }),
      })
      let data
      try { data = await res.json() } catch { data = {} }
      if (data.success) {
        showToast('success', t('telegram.testMessageSuccess'))
        const updated = await fetch('/api/settings/notifications/telegram').then(r => r.json())
        setCfg(updated)
      } else {
        showToast('error', data.error || data.message || t('telegram.testMessageFailed'))
      }
    } catch {
      showToast('error', t('telegram.errors.networkError'))
    } finally {
      setTesting(false)
    }
  }

  async function handleHealthCheck() {
    setHealthChecking(true)
    try {
      const res = await fetch('/api/settings/notifications/telegram/health-check', { method: 'POST' })
      let data
      try { data = await res.json() } catch { data = {} }
      if (data.success) {
        showToast('success', `${t('telegram.healthCheck.success')}${data.botUsername ? ` (@${data.botUsername})` : ''}`)
        const updated = await fetch('/api/settings/notifications/telegram').then(r => r.json())
        setCfg(updated)
      } else {
        showToast('error', data.message || t('telegram.healthCheck.failed'))
      }
    } catch {
      showToast('error', t('telegram.errors.networkError'))
    } finally {
      setHealthChecking(false)
    }
  }

  const statusKey = !cfg?.isConfigured ? null
    : cfg.lastTestStatus === 'success' ? 'success'
    : cfg.lastTestStatus === 'failed'  ? 'failed'
    : null

  return (
    <div
      className="rounded-xl p-6 space-y-5"
      style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
    >
      {/* Header row */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-gray-800">{t('telegram.title')}</h2>
          <p className="text-xs text-gray-400 mt-0.5">{t('telegram.subtitle')}</p>
        </div>
        {cfg && <TelegramStatusBadge status={statusKey} t={t} />}
      </div>

      {/* Metadata row */}
      {cfg?.isConfigured && (
        <div className="text-xs text-gray-500 space-y-0.5">
          {cfg.tokenMasked && <p>{t('telegram.tokenMasked')}: <span className="font-mono">{cfg.tokenMasked}</span></p>}
          {cfg.chatIdMasked && <p>{t('telegram.chatIdMasked')}: <span className="font-mono">{cfg.chatIdMasked}</span></p>}
          {cfg.updatedBy && <p>{t('telegram.updatedBy')}: {cfg.updatedBy}{cfg.updatedAtUtc ? ` — ${new Date(cfg.updatedAtUtc).toLocaleString()}` : ''}</p>}
          {cfg.lastTestAtUtc && <p>{t('telegram.lastTested')}: {new Date(cfg.lastTestAtUtc).toLocaleString()}</p>}
          {cfg.lastError && <p className="text-red-500">{t('telegram.lastError')}: {cfg.lastError}</p>}
        </div>
      )}

      {/* Enabled toggle */}
      <label className="flex items-center gap-3 cursor-pointer select-none">
        <div
          onClick={() => setEnabled(v => !v)}
          className="relative w-10 h-5 rounded-full transition-colors"
          style={{ background: enabled ? '#2f6fed' : '#cbd5e1' }}
        >
          <div
            className="absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform"
            style={{ left: enabled ? '1.25rem' : '0.125rem' }}
          />
        </div>
        <span className="text-sm font-medium text-gray-700">{t('telegram.enabledLabel')}</span>
      </label>

      {/* Bot Token */}
      <div>
        <label className="block text-sm font-semibold text-gray-700 mb-1">
          {t('telegram.botToken')}
          {cfg?.isConfigured && <span className="ml-2 text-xs font-normal text-gray-400">({t('telegram.leaveBlankToKeep')})</span>}
        </label>
        <input
          type="password"
          value={botToken}
          onChange={e => setBotToken(e.target.value)}
          placeholder={cfg?.isConfigured ? cfg.tokenMasked : t('telegram.botTokenPlaceholder')}
          className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
        />
      </div>

      {/* Chat ID */}
      <div>
        <label className="block text-sm font-semibold text-gray-700 mb-1">{t('telegram.chatId')}</label>
        <input
          type="text"
          value={chatId}
          onChange={e => setChatId(e.target.value)}
          placeholder={t('telegram.chatIdPlaceholder')}
          className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
        />
      </div>

      {/* Toast */}
      {actionToast && (
        <div className={`text-sm px-4 py-2 rounded-lg border ${
          actionToast.type === 'success'
            ? 'bg-green-50 text-green-700 border-green-200'
            : 'bg-red-50 text-red-700 border-red-200'
        }`}>
          {actionToast.message}
        </div>
      )}

      {/* Action buttons */}
      <div className="flex flex-wrap gap-2">
        <button
          onClick={handleValidate}
          disabled={validating}
          className="px-4 py-2 text-sm font-semibold rounded-lg border border-gray-200 text-gray-700 hover:bg-gray-50 disabled:opacity-40 transition-colors"
        >
          {validating ? t('telegram.validating') : t('telegram.testConnection')}
        </button>
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-4 py-2 text-sm font-semibold text-white rounded-lg disabled:opacity-40 transition-opacity"
          style={{ background: '#2f6fed' }}
        >
          {saving ? t('telegram.saving') : t('telegram.save')}
        </button>
        {cfg?.isConfigured && (
          <button
            onClick={handleTestMessage}
            disabled={testing || !cfg.enabled}
            className="px-4 py-2 text-sm font-semibold rounded-lg border border-gray-200 text-gray-700 hover:bg-gray-50 disabled:opacity-40 transition-colors"
          >
            {testing ? t('telegram.sending') : t('telegram.sendTestMessage')}
          </button>
        )}
        <button
          onClick={handleHealthCheck}
          disabled={healthChecking || !cfg?.isConfigured}
          className="px-4 py-2 text-sm font-semibold rounded-lg border border-gray-200 text-gray-700 hover:bg-gray-50 disabled:opacity-40 transition-colors"
          title={!cfg?.isConfigured ? t('telegram.healthCheck.notConfigured') : ''}
        >
          {healthChecking ? t('telegram.healthCheck.checking') : t('telegram.healthCheck.button')}
        </button>
      </div>
      {cfg?.isConfigured && (
        <p className="text-xs text-gray-400">
          {usingSaved ? t('telegram.credentialHint.saved') : t('telegram.credentialHint.input')}
        </p>
      )}

      {/* Collapsible setup guide */}
      <div>
        <button
          onClick={() => setShowGuide(v => !v)}
          className="text-xs text-blue-500 hover:underline flex items-center gap-1"
        >
          {showGuide ? '▾' : '▸'} {t('telegram.setupGuide.toggle')}
        </button>
        {showGuide && (
          <div className="mt-3 text-xs text-gray-500 space-y-2 pl-3 border-l-2 border-blue-100">
            <p className="font-semibold text-gray-700">{t('telegram.setupGuide.title')}</p>
            <p>1. {t('telegram.setupGuide.step1')}</p>
            <p>2. {t('telegram.setupGuide.step2')}</p>
            <p>3. {t('telegram.setupGuide.step3')}</p>
            <p>4. {t('telegram.setupGuide.step4')}</p>
            <p>5. {t('telegram.setupGuide.step5')}</p>
            <p className="text-orange-500 mt-2">⚠ {t('telegram.setupGuide.warning')}</p>
          </div>
        )}
      </div>
    </div>
  )
}

export function SettingsPage() {
  const { t } = useTranslation('settings')
  const { systemTimezone, setSystemTimezone } = useSettings()
  const [selected, setSelected] = useState(systemTimezone)
  const [search, setSearch] = useState('')
  const [saving, setSaving] = useState(false)
  const [toast, setToast] = useState(null) // { type: 'success'|'error', message }

  // Keep selected in sync if the context is loaded asynchronously
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
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
        <p className="text-sm text-gray-500 mt-1">{t('subtitle')}</p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        {/* ── Left column: Timezone ── */}
        <div className="space-y-4">
          <div
            className="rounded-xl p-6 space-y-5"
            style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
          >
            {/* Timezone selector */}
            <div>
              <label className="block text-sm font-semibold text-gray-700 mb-1">
                {t('timezone.label')}
              </label>
              <p className="text-xs text-gray-400 mb-3">{t('timezone.description')}</p>

              <input
                type="text"
                placeholder={t('timezone.search')}
                value={search}
                onChange={e => setSearch(e.target.value)}
                className="w-full mb-2 px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
              />

              <select
                size={8}
                value={selected}
                onChange={e => setSelected(e.target.value)}
                className="w-full px-2 py-1 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
              >
                {filtered.map(tz => (
                  <option key={tz} value={tz}>
                    {tz} ({tzOffset(tz)})
                  </option>
                ))}
              </select>
            </div>

            {/* Current → selected preview */}
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

            {/* Toast feedback */}
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

          {/* Info box */}
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

        {/* ── Right column: Telegram ── */}
        <TelegramPanel t={t} />
      </div>
    </div>
  )
}
