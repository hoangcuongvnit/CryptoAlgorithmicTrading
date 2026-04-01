import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

function ConfirmDialog({ title, message, confirmLabel, cancelLabel, onConfirm, onCancel, dangerous }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div
        className="rounded-xl p-6 max-w-md w-full mx-4 space-y-4"
        style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 8px 32px rgba(15,23,42,0.15)' }}
      >
        <h3 className="text-base font-bold text-gray-900">{title}</h3>
        <p className="text-sm text-gray-600">{message}</p>
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

export function TradingModePanel() {
  const { t } = useTranslation('settings')
  const [cfg, setCfg] = useState(null)
  const [paperMode, setPaperMode] = useState(true)
  const [initialBalance, setInitialBalance] = useState('10000')
  const [pendingPaperMode, setPendingPaperMode] = useState(null) // null = no pending change
  const [toast, setToast] = useState(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    fetch('/api/settings/trading/mode')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        setCfg(data)
        setPaperMode(data.paperTradingMode)
        setInitialBalance(String(data.initialBalance))
      })
      .catch(() => {})
  }, [])

  function showToast(type, message) {
    setToast({ type, message })
    setTimeout(() => setToast(null), 4000)
  }

  function handleToggle() {
    const next = !paperMode
    setPendingPaperMode(next) // show confirmation dialog
  }

  function handleConfirm() {
    setPaperMode(pendingPaperMode)
    setPendingPaperMode(null)
  }

  function handleCancelConfirm() {
    setPendingPaperMode(null)
  }

  async function handleSave() {
    const balance = parseFloat(initialBalance)
    if (isNaN(balance) || balance <= 0) {
      showToast('error', t('tradingMode.errors.balanceRequired')); return
    }
    setSaving(true)
    try {
      const res = await fetch('/api/settings/trading/mode', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ paperTradingMode: paperMode, initialBalance: balance, updatedBy: 'admin' }),
      })
      if (res.ok) {
        showToast('success', t('tradingMode.saveSuccess'))
        const updated = await fetch('/api/settings/trading/mode').then(r => r.json())
        setCfg(updated)
      } else {
        const text = await res.text()
        showToast('error', text || t('tradingMode.saveFailed'))
      }
    } catch {
      showToast('error', t('tradingMode.errors.networkError'))
    } finally {
      setSaving(false)
    }
  }

  const switchingToLive = pendingPaperMode === false
  const switchingToPaper = pendingPaperMode === true

  return (
    <>
      {/* Confirmation dialog */}
      {pendingPaperMode !== null && (
        <ConfirmDialog
          title={switchingToLive ? t('tradingMode.confirmLive.title') : t('tradingMode.confirmPaper.title')}
          message={switchingToLive ? t('tradingMode.confirmLive.message') : t('tradingMode.confirmPaper.message')}
          confirmLabel={switchingToLive ? t('tradingMode.confirmLive.confirm') : t('tradingMode.confirmPaper.confirm')}
          cancelLabel={switchingToLive ? t('tradingMode.confirmLive.cancel') : t('tradingMode.confirmPaper.cancel')}
          dangerous={switchingToLive}
          onConfirm={handleConfirm}
          onCancel={handleCancelConfirm}
        />
      )}

      <div
        className="rounded-xl p-6 space-y-5"
        style={{ background: '#fff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }}
      >
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-base font-semibold text-gray-800">{t('tradingMode.title')}</h2>
            <p className="text-xs text-gray-400 mt-0.5">{t('tradingMode.subtitle')}</p>
          </div>
          {/* Current mode badge */}
          <span
            className="text-xs font-bold px-3 py-1 rounded-full"
            style={paperMode
              ? { background: '#eff6ff', border: '1px solid #93c5fd', color: '#1d4ed8' }
              : { background: '#fef2f2', border: '1px solid #fca5a5', color: '#dc2626' }}
          >
            {paperMode ? t('tradingMode.paper') : t('tradingMode.live')}
          </span>
        </div>

        {/* Mode description */}
        <div
          className="text-xs rounded-lg px-4 py-3"
          style={paperMode
            ? { background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e40af' }
            : { background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b' }}
        >
          {paperMode ? t('tradingMode.paperModeDescription') : t('tradingMode.liveModeDescription')}
        </div>

        {/* Toggle */}
        <label className="flex items-center gap-3 cursor-pointer select-none">
          <div
            onClick={handleToggle}
            className="relative w-10 h-5 rounded-full transition-colors"
            style={{ background: paperMode ? '#2f6fed' : '#dc2626' }}
          >
            <div
              className="absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform"
              style={{ left: paperMode ? '1.25rem' : '0.125rem' }}
            />
          </div>
          <span className="text-sm font-medium text-gray-700">{t('tradingMode.paperMode')}</span>
        </label>

        {/* Initial balance (only when paper mode) */}
        {paperMode && (
          <div>
            <label className="block text-sm font-semibold text-gray-700 mb-1">
              {t('tradingMode.initialBalance')}
            </label>
            <input
              type="number"
              value={initialBalance}
              onChange={e => setInitialBalance(e.target.value)}
              placeholder={t('tradingMode.initialBalancePlaceholder')}
              min="1"
              step="100"
              className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
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

        <button
          onClick={handleSave}
          disabled={saving}
          className="w-full py-2.5 text-sm font-semibold text-white rounded-lg transition-opacity disabled:opacity-40"
          style={{ background: '#2f6fed' }}
        >
          {saving ? t('tradingMode.saving') : t('tradingMode.save')}
        </button>

        {cfg?.updatedBy && (
          <p className="text-xs text-gray-400">
            {cfg.updatedBy}{cfg.updatedAtUtc ? ` — ${new Date(cfg.updatedAtUtc).toLocaleString()}` : ''}
          </p>
        )}
      </div>
    </>
  )
}
