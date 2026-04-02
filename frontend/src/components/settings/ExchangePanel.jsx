import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../context/SettingsContext.jsx'

// ── small helpers ─────────────────────────────────────────────────────────────

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

// ── confirm dialog ────────────────────────────────────────────────────────────

function ConfirmDialog({ title, message, confirmLabel, cancelLabel, dangerous, onConfirm, onCancel }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div
        className="rounded-xl p-6 max-w-md w-full mx-4 space-y-4"
        style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 8px 32px rgba(15,23,42,0.18)' }}
      >
        <h3 className="text-base font-bold text-gray-900">{title}</h3>
        <p className="text-sm text-gray-600 leading-relaxed">{message}</p>
        <div className="flex gap-3 justify-end">
          <button
            onClick={onCancel}
            className="px-4 py-2 text-sm font-semibold rounded-lg border border-gray-200 text-gray-700 hover:bg-gray-50 transition-colors"
          >
            {cancelLabel}
          </button>
          <button
            onClick={onConfirm}
            className="px-4 py-2 text-sm font-semibold text-white rounded-lg transition-opacity"
            style={{ background: dangerous ? '#dc2626' : '#2f6fed' }}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── per-environment credential section ───────────────────────────────────────

function EnvSection({ isTestnet, isConfigured, keyMasked, secretMasked,
                      apiKey, setApiKey, apiSecret, setApiSecret,
                      onTest, testing, isActive }) {
  const { t } = useTranslation('settings')

  const colors = isTestnet
    ? {
        border: isActive ? '#bfdbfe' : '#e2e8f0',
        bg: isActive ? '#eff6ff' : '#f8fafc',
        badge: { background: '#eff6ff', border: '1px solid #93c5fd', color: '#1d4ed8' },
        ring: 'focus:ring-blue-500',
      }
    : {
        border: isActive ? '#fdba74' : '#e2e8f0',
        bg: isActive ? '#fff7ed' : '#f8fafc',
        badge: { background: '#fff7ed', border: '1px solid #fdba74', color: '#c2410c' },
        ring: 'focus:ring-orange-400',
      }

  const canTest = isConfigured || (apiKey.trim() && apiSecret.trim())

  return (
    <div
      className="rounded-xl border p-4 space-y-3 transition-all duration-200"
      style={{ borderColor: colors.border, background: colors.bg }}
    >
      {/* Header */}
      <div className="flex items-center justify-between flex-wrap gap-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-gray-700">
            {isTestnet ? t('exchange.testnetSection') : t('exchange.liveSection')}
          </span>
          {isActive && (
            <span className="text-xs font-bold px-1.5 py-0.5 rounded"
              style={{ background: '#dcfce7', color: '#15803d', border: '1px solid #86efac' }}>
              {t('exchange.activeLabel')}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs font-semibold px-2 py-0.5 rounded-full" style={colors.badge}>
            {isTestnet ? t('exchange.testnetBadge') : t('exchange.mainnetBadge')}
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
            placeholder={isConfigured ? keyMasked : (isTestnet ? t('exchange.testnetApiKeyPlaceholder') : t('exchange.apiKeyPlaceholder'))}
            className={`w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 ${colors.ring} font-mono bg-white`}
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
            placeholder={isConfigured ? secretMasked : (isTestnet ? t('exchange.testnetApiSecretPlaceholder') : t('exchange.apiSecretPlaceholder'))}
            className={`w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 ${colors.ring} font-mono bg-white`}
          />
        </div>
      </div>

      {/* Test Connection button — per section */}
      <div className="flex items-center gap-3">
        <button
          onClick={onTest}
          disabled={testing || !canTest}
          className="px-4 py-1.5 text-sm font-semibold rounded-lg border border-gray-300 bg-white text-gray-700 hover:bg-gray-50 disabled:opacity-40 transition-colors"
        >
          {testing ? t('exchange.validating') : t('exchange.testConnection')}
        </button>
        {!canTest && (
          <span className="text-xs text-gray-400">{t('exchange.enterKeyToTest')}</span>
        )}
      </div>
    </div>
  )
}

// ── main panel ────────────────────────────────────────────────────────────────

export function ExchangePanel() {
  const { t } = useTranslation('settings')
  const { refreshTradingMode } = useSettings()

  const [cfg, setCfg] = useState(null)

  // Current active environment in DB
  const [useTestnet, setUseTestnet] = useState(true)
  // Pending selection (before Save)
  const [pendingTestnet, setPendingTestnet] = useState(null)

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

  // selectedEnv = what the user has selected in the toggle (may differ from saved DB value)
  const [selectedEnv, setSelectedEnv] = useState(true) // true = testnet

  useEffect(() => {
    fetch('/api/settings/exchange/binance')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setCfg(data)
        setUseTestnet(data.useTestnet)
        setSelectedEnv(data.useTestnet)
      })
      .catch(() => {})
  }, [])

  // ── environment toggle with confirmation ──────────────────────────────────

  function handleEnvSelect(wantTestnet) {
    if (wantTestnet === selectedEnv) return
    // Ask confirm only when switching the active environment (savedTestnet differs from selection)
    if (wantTestnet !== useTestnet) {
      // Switching away from saved DB value → confirm on Save, not here
    }
    setSelectedEnv(wantTestnet)
  }

  // ── test connection (per environment) ────────────────────────────────────

  async function handleTest(isTestnet, key, secret, showToast, setTesting) {
    setTesting(true)
    try {
      let res
      if (key.trim() && secret.trim()) {
        res = await fetch('/api/settings/exchange/binance/validate', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ apiKey: key.trim(), apiSecret: secret.trim(), useTestnet: isTestnet }),
        })
      } else {
        // test saved credentials for this specific environment
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

  // ── save ──────────────────────────────────────────────────────────────────

  function handleSaveClick() {
    // If switching active environment, show confirmation first
    if (selectedEnv !== useTestnet) {
      setPendingTestnet(selectedEnv)
    } else {
      doSave(selectedEnv)
    }
  }

  function handleConfirmSwitch() {
    const env = pendingTestnet
    setPendingTestnet(null)
    doSave(env)
  }

  async function doSave(targetTestnet) {
    if (!cfg?.isConfigured && !targetTestnet) {
      if (!liveKey.trim()) { showSaveToast('error', t('exchange.errors.keyRequired')); return }
      if (!liveSecret.trim()) { showSaveToast('error', t('exchange.errors.secretRequired')); return }
    }
    if (!cfg?.testnetIsConfigured && targetTestnet) {
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
          useTestnet: targetTestnet,
          updatedBy: 'admin',
        }),
      })
      if (res.ok) {
        showSaveToast('success', t('exchange.saveSuccess'))
        const updated = await fetch('/api/settings/exchange/binance').then(r => r.json())
        setCfg(updated)
        setUseTestnet(updated.useTestnet)
        setSelectedEnv(updated.useTestnet)
        setLiveKey(''); setLiveSecret('')
        setTestnetKey(''); setTestnetSecret('')
        await refreshTradingMode()
      } else {
        showSaveToast('error', await res.text() || t('exchange.saveFailed'))
      }
    } catch {
      showSaveToast('error', t('exchange.errors.networkError'))
    } finally {
      setSaving(false)
    }
  }

  const switchingToMainnet = pendingTestnet === false
  const confirmKey = switchingToMainnet ? 'confirmSwitchToMainnet' : 'confirmSwitchToTestnet'

  const envChanged = selectedEnv !== useTestnet

  return (
    <>
      {/* Confirmation dialog */}
      {pendingTestnet !== null && (
        <ConfirmDialog
          title={t(`exchange.${confirmKey}.title`)}
          message={t(`exchange.${confirmKey}.message`)}
          confirmLabel={t(`exchange.${confirmKey}.confirm`)}
          cancelLabel={t(`exchange.${confirmKey}.cancel`)}
          dangerous={switchingToMainnet}
          onConfirm={handleConfirmSwitch}
          onCancel={() => setPendingTestnet(null)}
        />
      )}

      <div
        className="rounded-xl p-6 space-y-5"
        style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
      >
        {/* Header */}
        <div>
          <h2 className="text-base font-semibold text-gray-800">{t('exchange.title')}</h2>
          <p className="text-xs text-gray-400 mt-0.5">{t('exchange.subtitle')}</p>
        </div>

        {/* ── Active environment selector ── */}
        <div>
          <p className="text-xs font-semibold text-gray-500 mb-2 uppercase tracking-wide">
            {t('exchange.activeEnvironment')}
          </p>
          <div className="flex rounded-lg border border-gray-200 overflow-hidden w-fit text-sm font-semibold">
            <button
              onClick={() => handleEnvSelect(false)}
              className="px-5 py-2 transition-colors"
              style={selectedEnv === false
                ? { background: '#fff7ed', color: '#c2410c', borderRight: '1px solid #fdba74' }
                : { background: '#f8fafc', color: '#64748b', borderRight: '1px solid #e2e8f0' }}
            >
              {t('exchange.mainnetBadge')}
            </button>
            <button
              onClick={() => handleEnvSelect(true)}
              className="px-5 py-2 transition-colors"
              style={selectedEnv === true
                ? { background: '#eff6ff', color: '#1d4ed8' }
                : { background: '#f8fafc', color: '#64748b' }}
            >
              {t('exchange.testnetBadge')}
            </button>
          </div>

          {/* Hint: show when pending switch differs from saved */}
          {envChanged && (
            <p className="text-xs mt-1.5 font-semibold"
              style={{ color: selectedEnv ? '#1d4ed8' : '#c2410c' }}>
              ⚠ {selectedEnv
                ? t('exchange.pendingSwitchTestnet')
                : t('exchange.pendingSwitchMainnet')}
            </p>
          )}
          {!envChanged && (
            <p className="text-xs text-gray-400 mt-1.5">{t('exchange.activeEnvironmentHint')}</p>
          )}
        </div>

        {/* ── Live credentials section ── */}
        <div className="space-y-1">
          <EnvSection
            isTestnet={false}
            isConfigured={cfg?.isConfigured}
            keyMasked={cfg?.apiKeyMasked}
            secretMasked={cfg?.apiSecretMasked}
            apiKey={liveKey} setApiKey={setLiveKey}
            apiSecret={liveSecret} setApiSecret={setLiveSecret}
            isActive={selectedEnv === false}
            onTest={() => handleTest(false, liveKey, liveSecret, showLiveToast, setTestingLive)}
            testing={testingLive}
          />
          <Toast toast={liveToast} />
        </div>

        {/* ── Testnet credentials section ── */}
        <div className="space-y-1">
          <EnvSection
            isTestnet={true}
            isConfigured={cfg?.testnetIsConfigured}
            keyMasked={cfg?.testnetApiKeyMasked}
            secretMasked={cfg?.testnetApiSecretMasked}
            apiKey={testnetKey} setApiKey={setTestnetKey}
            apiSecret={testnetSecret} setApiSecret={setTestnetSecret}
            isActive={selectedEnv === true}
            onTest={() => handleTest(true, testnetKey, testnetSecret, showTestnetToast, setTestingTestnet)}
            testing={testingTestnet}
          />
          <Toast toast={testnetToast} />
        </div>

        {/* ── Save area ── */}
        <div className="space-y-2 pt-1 border-t border-gray-100">
          <Toast toast={saveToast} />
          <div className="flex items-center gap-3 pt-2">
            <button
              onClick={handleSaveClick}
              disabled={saving}
              className="px-5 py-2 text-sm font-semibold text-white rounded-lg disabled:opacity-40 transition-opacity"
              style={{ background: envChanged && !saving ? (selectedEnv ? '#1d4ed8' : '#dc2626') : '#2f6fed' }}
            >
              {saving
                ? t('exchange.saving')
                : envChanged
                  ? t('exchange.saveAndSwitch')
                  : t('exchange.save')}
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
    </>
  )
}
