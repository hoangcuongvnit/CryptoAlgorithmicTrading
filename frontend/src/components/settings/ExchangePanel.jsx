import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

export function ExchangePanel() {
  const { t } = useTranslation('settings')
  const [cfg, setCfg] = useState(null)
  const [apiKey, setApiKey] = useState('')
  const [apiSecret, setApiSecret] = useState('')
  const [useTestnet, setUseTestnet] = useState(true)
  const [prevTestnet, setPrevTestnet] = useState(true)
  const [showGuide, setShowGuide] = useState(false)
  const [toast, setToast] = useState(null)
  const [validating, setValidating] = useState(false)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    fetch('/api/settings/exchange/binance')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setCfg(data)
        setUseTestnet(data.useTestnet)
        setPrevTestnet(data.useTestnet)
      })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }

  const usingSaved = !!(cfg?.isConfigured && !apiKey.trim())
  const switchingToMainnet = cfg?.useTestnet && !useTestnet

  async function handleValidate() {
    if (!cfg?.isConfigured && (!apiKey.trim() || !apiSecret.trim())) {
      showToast('error', t('exchange.errors.keyRequired')); return
    }
    setValidating(true)
    try {
      let res
      if (usingSaved) {
        res = await fetch('/api/settings/exchange/binance/validate-saved', { method: 'POST' })
      } else {
        res = await fetch('/api/settings/exchange/binance/validate', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ apiKey: apiKey.trim(), apiSecret: apiSecret.trim(), useTestnet }),
        })
      }
      let data
      try { data = await res.json() } catch { data = {} }
      if (data.valid) {
        showToast('success', t('exchange.validateSuccess'))
      } else {
        showToast('error', data.message || t('exchange.validateFailed'))
      }
    } catch {
      showToast('error', t('exchange.errors.networkError'))
    } finally {
      setValidating(false)
    }
  }

  async function handleSave() {
    if (!cfg?.isConfigured) {
      if (!apiKey.trim()) { showToast('error', t('exchange.errors.keyRequired')); return }
      if (!apiSecret.trim()) { showToast('error', t('exchange.errors.secretRequired')); return }
    }
    setSaving(true)
    try {
      const res = await fetch('/api/settings/exchange/binance', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          apiKey: apiKey.trim() || undefined,
          apiSecret: apiSecret.trim() || undefined,
          useTestnet,
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showToast('success', t('exchange.saveSuccess'))
        const updated = await fetch('/api/settings/exchange/binance').then(r => r.json())
        setCfg(updated)
        setPrevTestnet(updated.useTestnet)
        setApiKey('')
        setApiSecret('')
      } else {
        const text = await res.text()
        showToast('error', text || t('exchange.saveFailed'))
      }
    } catch {
      showToast('error', t('exchange.errors.networkError'))
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
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-gray-800">{t('exchange.title')}</h2>
          <p className="text-xs text-gray-400 mt-0.5">{t('exchange.subtitle')}</p>
        </div>
        <span
          className="text-xs font-semibold px-2 py-0.5 rounded-full"
          style={cfg?.isConfigured
            ? { background: '#f0fdf4', border: '1px solid #86efac', color: '#15803d' }
            : { background: '#f8fafc', border: '1px solid #cbd5e1', color: '#64748b' }}
        >
          {cfg?.isConfigured ? t('exchange.status.configured') : t('exchange.status.notConfigured')}
        </span>
      </div>

      {/* Saved credentials metadata */}
      {cfg?.isConfigured && (
        <div className="text-xs text-gray-500 space-y-0.5">
          {cfg.apiKeyMasked && <p>API Key: <span className="font-mono">{cfg.apiKeyMasked}</span></p>}
          {cfg.apiSecretMasked && <p>API Secret: <span className="font-mono">{cfg.apiSecretMasked}</span></p>}
          {cfg.updatedBy && <p>{cfg.updatedBy}{cfg.updatedAtUtc ? ` — ${new Date(cfg.updatedAtUtc).toLocaleString()}` : ''}</p>}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* API Key */}
        <div>
          <label className="block text-sm font-semibold text-gray-700 mb-1">
            {t('exchange.apiKey')}
            {cfg?.isConfigured && <span className="ml-2 text-xs font-normal text-gray-400">({t('exchange.leaveBlankToKeep')})</span>}
          </label>
          <input
            type="password"
            value={apiKey}
            onChange={e => setApiKey(e.target.value)}
            placeholder={cfg?.isConfigured ? cfg.apiKeyMasked : t('exchange.apiKeyPlaceholder')}
            className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
          />
        </div>

        {/* API Secret */}
        <div>
          <label className="block text-sm font-semibold text-gray-700 mb-1">
            {t('exchange.apiSecret')}
            {cfg?.isConfigured && <span className="ml-2 text-xs font-normal text-gray-400">({t('exchange.leaveBlankToKeep')})</span>}
          </label>
          <input
            type="password"
            value={apiSecret}
            onChange={e => setApiSecret(e.target.value)}
            placeholder={cfg?.isConfigured ? cfg.apiSecretMasked : t('exchange.apiSecretPlaceholder')}
            className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
          />
        </div>
      </div>

      {/* Testnet toggle */}
      <label className="flex items-center gap-3 cursor-pointer select-none">
        <div
          onClick={() => setUseTestnet(v => !v)}
          className="relative w-10 h-5 rounded-full transition-colors"
          style={{ background: useTestnet ? '#2f6fed' : '#f59e0b' }}
        >
          <div
            className="absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform"
            style={{ left: useTestnet ? '1.25rem' : '0.125rem' }}
          />
        </div>
        <span className="text-sm font-medium text-gray-700">{t('exchange.useTestnet')}</span>
        <span
          className="text-xs font-semibold px-2 py-0.5 rounded-full"
          style={useTestnet
            ? { background: '#eff6ff', border: '1px solid #93c5fd', color: '#1d4ed8' }
            : { background: '#fff7ed', border: '1px solid #fdba74', color: '#c2410c' }}
        >
          {useTestnet ? t('exchange.testnetBadge') : t('exchange.mainnetBadge')}
        </span>
      </label>

      {/* Testnet → mainnet warning */}
      {switchingToMainnet && (
        <div className="text-sm px-4 py-2 rounded-lg border bg-orange-50 text-orange-700 border-orange-200">
          ⚠ {t('exchange.testnetWarning')}
        </div>
      )}

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

      {/* Action buttons */}
      <div className="flex flex-wrap gap-2">
        <button
          onClick={handleValidate}
          disabled={validating}
          className="px-4 py-2 text-sm font-semibold rounded-lg border border-gray-200 text-gray-700 hover:bg-gray-50 disabled:opacity-40 transition-colors"
        >
          {validating ? t('exchange.validating') : t('exchange.testConnection')}
        </button>
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-4 py-2 text-sm font-semibold text-white rounded-lg disabled:opacity-40 transition-opacity"
          style={{ background: '#2f6fed' }}
        >
          {saving ? t('exchange.saving') : t('exchange.save')}
        </button>
      </div>

      {/* Restart warning */}
      <p className="text-xs text-gray-400">{t('exchange.restartWarning')}</p>

      {/* Setup guide */}
      <div>
        <button
          onClick={() => setShowGuide(v => !v)}
          className="text-xs text-blue-500 hover:underline flex items-center gap-1"
        >
          {showGuide ? '▾' : '▸'} {t('exchange.setupGuide.toggle')}
        </button>
        {showGuide && (
          <div className="mt-3 text-xs text-gray-500 space-y-2 pl-3 border-l-2 border-blue-100">
            <p className="font-semibold text-gray-700">{t('exchange.setupGuide.title')}</p>
            <p>1. {t('exchange.setupGuide.step1')}</p>
            <p>2. {t('exchange.setupGuide.step2')}</p>
            <p>3. {t('exchange.setupGuide.step3')}</p>
            <p>4. {t('exchange.setupGuide.step4')}</p>
            <p>5. {t('exchange.setupGuide.step5')}</p>
            <div className="flex flex-wrap gap-3 mt-2">
              <a
                href="https://www.binance.com/en/my/settings/api-management"
                target="_blank"
                rel="noopener noreferrer"
                className="text-blue-500 hover:underline font-medium"
              >
                → {t('exchange.setupGuide.linkMain')}
              </a>
              <a
                href="https://testnet.binance.vision/"
                target="_blank"
                rel="noopener noreferrer"
                className="text-blue-500 hover:underline font-medium"
              >
                → {t('exchange.setupGuide.linkTestnet')}
              </a>
            </div>
            <p className="text-orange-500 mt-2">⚠ {t('exchange.setupGuide.warning')}</p>
          </div>
        )}
      </div>
    </div>
  )
}
