import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

// ── helpers ───────────────────────────────────────────────────────────────────

function Toast({ toast }) {
  if (!toast) return null
  const ok = toast.type === 'success'
  return (
    <div className={`text-sm px-3 py-2 rounded-lg border ${
      ok ? 'bg-green-50 text-green-700 border-green-200'
         : 'bg-red-50 text-red-700 border-red-200'
    }`}>
      {toast.message}
    </div>
  )
}

function useToast() {
  const [toast, setToast] = useState(null)
  function show(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }
  return [toast, show]
}

// ── per-environment credential section ───────────────────────────────────────

function EnvSection({ isTestnet, isConfigured, keyMasked, secretMasked,
                      apiKey, setApiKey, apiSecret, setApiSecret,
                      onTest, testing, activeEnv }) {
  const { t } = useTranslation('settings')
  const active = activeEnv === isTestnet

  const colors = isTestnet
    ? { border: active ? '#bfdbfe' : '#e2e8f0', bg: active ? '#eff6ff' : '#f8fafc',
        badge: { background: '#eff6ff', border: '1px solid #93c5fd', color: '#1d4ed8' },
        ring: 'focus:ring-blue-500' }
    : { border: active ? '#fdba74' : '#e2e8f0', bg: active ? '#fff7ed' : '#f8fafc',
        badge: { background: '#fff7ed', border: '1px solid #fdba74', color: '#c2410c' },
        ring: 'focus:ring-orange-400' }

  const title = isTestnet ? t('exchange.testnetSection') : t('exchange.liveSection')
  const badge = isTestnet ? t('exchange.testnetBadge') : t('exchange.mainnetBadge')
  const keyPh = isTestnet ? t('exchange.testnetApiKeyPlaceholder') : t('exchange.apiKeyPlaceholder')
  const secPh = isTestnet ? t('exchange.testnetApiSecretPlaceholder') : t('exchange.apiSecretPlaceholder')

  return (
    <div
      className="rounded-xl border p-4 space-y-3 transition-colors"
      style={{ borderColor: colors.border, background: colors.bg }}
    >
      {/* Section header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-gray-700">{title}</span>
          {active && (
            <span className="text-xs font-semibold px-1.5 py-0.5 rounded"
              style={{ background: '#dcfce7', color: '#15803d', border: '1px solid #86efac' }}>
              {t('exchange.activeLabel')}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs font-semibold px-2 py-0.5 rounded-full" style={colors.badge}>
            {badge}
          </span>
          <span
            className="text-xs font-semibold px-2 py-0.5 rounded-full"
            style={isConfigured
              ? { background: '#f0fdf4', border: '1px solid #86efac', color: '#15803d' }
              : { background: '#f1f5f9', border: '1px solid #cbd5e1', color: '#64748b' }}
          >
            {isConfigured ? t('exchange.status.configured') : t('exchange.status.notConfigured')}
          </span>
        </div>
      </div>

      {/* Saved credentials hint */}
      {isConfigured && (
        <div className="text-xs text-gray-400 font-mono space-y-0.5">
          {keyMasked && <div>Key: {keyMasked}</div>}
          {secretMasked && <div>Secret: {secretMasked}</div>}
        </div>
      )}

      {/* Input fields */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-semibold text-gray-600 mb-1">
            {t('exchange.apiKey')}
            {isConfigured && (
              <span className="ml-1 font-normal text-gray-400">({t('exchange.leaveBlankToKeep')})</span>
            )}
          </label>
          <input
            type="password"
            value={apiKey}
            onChange={e => setApiKey(e.target.value)}
            placeholder={isConfigured ? keyMasked : keyPh}
            className={`w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 ${colors.ring} font-mono`}
          />
        </div>
        <div>
          <label className="block text-xs font-semibold text-gray-600 mb-1">
            {t('exchange.apiSecret')}
            {isConfigured && (
              <span className="ml-1 font-normal text-gray-400">({t('exchange.leaveBlankToKeep')})</span>
            )}
          </label>
          <input
            type="password"
            value={apiSecret}
            onChange={e => setApiSecret(e.target.value)}
            placeholder={isConfigured ? secretMasked : secPh}
            className={`w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 ${colors.ring} font-mono`}
          />
        </div>
      </div>

      {/* Test Connection button — per section */}
      <div>
        <button
          onClick={onTest}
          disabled={testing || (!isConfigured && (!apiKey.trim() || !apiSecret.trim()))}
          className="px-4 py-1.5 text-sm font-semibold rounded-lg border border-gray-200 text-gray-700 hover:bg-white disabled:opacity-40 transition-colors"
        >
          {testing ? t('exchange.validating') : t('exchange.testConnection')}
        </button>
        {!isConfigured && (!apiKey.trim() || !apiSecret.trim()) && (
          <span className="ml-3 text-xs text-gray-400">{t('exchange.enterKeyToTest')}</span>
        )}
      </div>
    </div>
  )
}

// ── main panel ────────────────────────────────────────────────────────────────

export function ExchangePanel() {
  const { t } = useTranslation('settings')

  const [cfg, setCfg] = useState(null)

  // Active environment toggle (which env the system uses for real orders)
  const [useTestnet, setUseTestnet] = useState(true)
  const [prevTestnet, setPrevTestnet] = useState(true)

  // Live credentials form
  const [liveKey, setLiveKey] = useState('')
  const [liveSecret, setLiveSecret] = useState('')

  // Testnet credentials form
  const [testnetKey, setTestnetKey] = useState('')
  const [testnetSecret, setTestnetSecret] = useState('')

  const [liveToast, showLiveToast] = useToast()
  const [testnetToast, showTestnetToast] = useToast()
  const [saveToast, showSaveToast] = useToast()

  const [testingLive, setTestingLive] = useState(false)
  const [testingTestnet, setTestingTestnet] = useState(false)
  const [saving, setSaving] = useState(false)
  const [showGuide, setShowGuide] = useState(false)

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

  // ── test connection (per environment) ──────────────────────────────────────

  async function handleTest(isTestnet, key, secret, showToast, setTesting) {
    setTesting(true)
    try {
      let res
      // Use entered credentials if provided, otherwise test what's saved in DB
      if (key.trim() && secret.trim()) {
        res = await fetch('/api/settings/exchange/binance/validate', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ apiKey: key.trim(), apiSecret: secret.trim(), useTestnet: isTestnet }),
        })
      } else {
        res = await fetch('/api/settings/exchange/binance/validate-saved', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ useTestnet: isTestnet }),
        })
      }
      let data
      try { data = await res.json() } catch { data = {} }
      showToast(data.valid ? 'success' : 'error',
        data.valid ? t('exchange.validateSuccess') : (data.message || t('exchange.validateFailed')))
    } catch {
      showToast('error', t('exchange.errors.networkError'))
    } finally {
      setTesting(false)
    }
  }

  // ── save (both environments at once) ──────────────────────────────────────

  async function handleSave() {
    // Require keys for first-time setup of the active environment
    if (!cfg?.isConfigured && !useTestnet) {
      if (!liveKey.trim()) { showSaveToast('error', t('exchange.errors.keyRequired')); return }
      if (!liveSecret.trim()) { showSaveToast('error', t('exchange.errors.secretRequired')); return }
    }
    if (!cfg?.testnetIsConfigured && useTestnet) {
      if (!testnetKey.trim()) { showSaveToast('error', t('exchange.errors.keyRequired')); return }
      if (!testnetSecret.trim()) { showSaveToast('error', t('exchange.errors.secretRequired')); return }
    }
    setSaving(true)
    try {
      const res = await fetch('/api/settings/exchange/binance', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          apiKey: liveKey.trim() || undefined,
          apiSecret: liveSecret.trim() || undefined,
          testnetApiKey: testnetKey.trim() || undefined,
          testnetApiSecret: testnetSecret.trim() || undefined,
          useTestnet,
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showSaveToast('success', t('exchange.saveSuccess'))
        const updated = await fetch('/api/settings/exchange/binance').then(r => r.json())
        setCfg(updated)
        setPrevTestnet(updated.useTestnet)
        setLiveKey(''); setLiveSecret('')
        setTestnetKey(''); setTestnetSecret('')
      } else {
        showSaveToast('error', await res.text() || t('exchange.saveFailed'))
      }
    } catch {
      showSaveToast('error', t('exchange.errors.networkError'))
    } finally {
      setSaving(false)
    }
  }

  const switchingToMainnet = prevTestnet && !useTestnet

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
      </div>

      {/* Active environment toggle */}
      <div>
        <p className="text-xs font-semibold text-gray-500 mb-2 uppercase tracking-wide">
          {t('exchange.activeEnvironment')}
        </p>
        <div className="flex rounded-lg border border-gray-200 overflow-hidden w-fit text-sm font-semibold">
          <button
            onClick={() => setUseTestnet(false)}
            className="px-5 py-2 transition-colors"
            style={!useTestnet
              ? { background: '#fff7ed', color: '#c2410c', borderRight: '1px solid #fdba74' }
              : { background: '#f8fafc', color: '#64748b', borderRight: '1px solid #e2e8f0' }}
          >
            {t('exchange.mainnetBadge')}
          </button>
          <button
            onClick={() => setUseTestnet(true)}
            className="px-5 py-2 transition-colors"
            style={useTestnet
              ? { background: '#eff6ff', color: '#1d4ed8' }
              : { background: '#f8fafc', color: '#64748b' }}
          >
            {t('exchange.testnetBadge')}
          </button>
        </div>
        <p className="text-xs text-gray-400 mt-1.5">{t('exchange.activeEnvironmentHint')}</p>
      </div>

      {/* Mainnet → warning */}
      {switchingToMainnet && (
        <div className="text-sm px-4 py-2 rounded-lg border bg-orange-50 text-orange-700 border-orange-200">
          ⚠ {t('exchange.testnetWarning')}
        </div>
      )}

      {/* Live section */}
      <div className="space-y-1">
        <EnvSection
          isTestnet={false}
          isConfigured={cfg?.isConfigured}
          keyMasked={cfg?.apiKeyMasked}
          secretMasked={cfg?.apiSecretMasked}
          apiKey={liveKey} setApiKey={setLiveKey}
          apiSecret={liveSecret} setApiSecret={setLiveSecret}
          activeEnv={useTestnet === false}
          onTest={() => handleTest(false, liveKey, liveSecret, showLiveToast, setTestingLive)}
          testing={testingLive}
        />
        <Toast toast={liveToast} />
      </div>

      {/* Testnet section */}
      <div className="space-y-1">
        <EnvSection
          isTestnet={true}
          isConfigured={cfg?.testnetIsConfigured}
          keyMasked={cfg?.testnetApiKeyMasked}
          secretMasked={cfg?.testnetApiSecretMasked}
          apiKey={testnetKey} setApiKey={setTestnetKey}
          apiSecret={testnetSecret} setApiSecret={setTestnetSecret}
          activeEnv={useTestnet === true}
          onTest={() => handleTest(true, testnetKey, testnetSecret, showTestnetToast, setTestingTestnet)}
          testing={testingTestnet}
        />
        <Toast toast={testnetToast} />
      </div>

      {/* Save + save toast */}
      <div className="space-y-2 pt-1">
        <Toast toast={saveToast} />
        <div className="flex items-center gap-3">
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-2 text-sm font-semibold text-white rounded-lg disabled:opacity-40 transition-opacity"
            style={{ background: '#2f6fed' }}
          >
            {saving ? t('exchange.saving') : t('exchange.save')}
          </button>
          {cfg?.updatedBy && (
            <span className="text-xs text-gray-400">
              {cfg.updatedBy}{cfg.updatedAtUtc ? ` — ${new Date(cfg.updatedAtUtc).toLocaleString()}` : ''}
            </span>
          )}
        </div>
      </div>

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
              <a href="https://www.binance.com/en/my/settings/api-management"
                target="_blank" rel="noopener noreferrer"
                className="text-blue-500 hover:underline font-medium">
                → {t('exchange.setupGuide.linkMain')}
              </a>
              <a href="https://testnet.binance.vision/"
                target="_blank" rel="noopener noreferrer"
                className="text-blue-500 hover:underline font-medium">
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
