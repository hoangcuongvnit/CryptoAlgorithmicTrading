import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import {
  useCloseAllStatus,
  useCloseAllHistory,
  useOpenPositions,
  apiCloseAllNow,
  apiScheduleCloseAll,
  apiCancelCloseAll,
  apiResumeTrading,
} from '../hooks/useDashboard.js'

// ── Helpers ───────────────────────────────────────────────────────────────

function newKey() {
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

function fmtLocal(isoOrDate) {
  if (!isoOrDate) return '—'
  return new Date(isoOrDate).toLocaleString(undefined, { hour12: false })
}

/** Convert datetime-local string to local Date */
function localInputToDate(value) {
  return value ? new Date(value) : null
}

/** Get min value for datetime-local (now + 60s) */
function minDateTimeLocal() {
  const d = new Date(Date.now() + 60_000)
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

// ── Status style map ─────────────────────────────────────────────────────

const STATUS_STYLE = {
  Idle:                { dot: '#9ca3af', bg: '#f9fafb',  border: '#e5e7eb', text: '#374151'  },
  Requested:           { dot: '#f59e0b', bg: '#fffbeb',  border: '#fcd34d', text: '#92400e'  },
  Scheduled:           { dot: '#3b82f6', bg: '#eff6ff',  border: '#93c5fd', text: '#1e40af'  },
  Executing:           { dot: '#f97316', bg: '#fff7ed',  border: '#fdba74', text: '#9a3412'  },
  Completed:           { dot: '#22c55e', bg: '#f0fdf4',  border: '#86efac', text: '#166534'  },
  CompletedWithErrors: { dot: '#ef4444', bg: '#fef2f2',  border: '#fca5a5', text: '#991b1b'  },
  Canceled:            { dot: '#9ca3af', bg: '#f9fafb',  border: '#e5e7eb', text: '#4b5563'  },
}

const ACTIVE = new Set(['Requested', 'Scheduled', 'Executing'])

// ── Countdown ─────────────────────────────────────────────────────────────

function Countdown({ targetUtc }) {
  const [label, setLabel] = useState('')

  useEffect(() => {
    const tick = () => {
      const diff = new Date(targetUtc) - Date.now()
      if (diff <= 0) { setLabel('now'); return }
      const h = Math.floor(diff / 3_600_000)
      const m = Math.floor((diff % 3_600_000) / 60_000)
      const s = Math.floor((diff % 60_000) / 1_000)
      setLabel(h > 0 ? `${h}h ${m}m ${s}s` : m > 0 ? `${m}m ${s}s` : `${s}s`)
    }
    tick()
    const id = setInterval(tick, 1_000)
    return () => clearInterval(id)
  }, [targetUtc])

  return <span className="font-mono font-bold text-blue-700">{label}</span>
}

// ── Confirmation dialog ───────────────────────────────────────────────────

function ConfirmDialog({ mode, scheduledDate, positionCount, onConfirm, onClose, isSubmitting, error, t }) {
  const [text, setText] = useState('')
  const isSchedule = mode === 'schedule'
  const confirmed = text.toUpperCase() === 'CLOSE ALL'

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        {/* Header */}
        <div className={`px-6 py-4 rounded-t-2xl border-b ${isSchedule ? 'bg-blue-50 border-blue-100' : 'bg-red-50 border-red-100'}`}>
          <div className="flex items-center gap-2">
            <span className="text-2xl">{isSchedule ? '📅' : '⚠️'}</span>
            <h3 className={`font-bold text-lg ${isSchedule ? 'text-blue-800' : 'text-red-800'}`}>
              {isSchedule ? t('dialog.scheduleTitle') : t('dialog.closeAllTitle')}
            </h3>
          </div>
        </div>

        {/* Body */}
        <div className="px-6 py-5 space-y-3">
          {/* Position count */}
          <div className={`flex items-center gap-2 px-3 py-2 rounded-lg text-sm font-medium ${
            positionCount > 0 ? 'bg-amber-50 text-amber-800' : 'bg-gray-50 text-gray-600'
          }`}>
            <span>{positionCount > 0 ? '📂' : '✅'}</span>
            <span>
              {positionCount > 0
                ? t('dialog.willClose_other', { count: positionCount })
                : t('dialog.noPositions')}
            </span>
          </div>

          {/* Schedule time */}
          {isSchedule && scheduledDate && (
            <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-blue-50 text-blue-800 text-sm font-medium">
              <span>🕐</span>
              <span>{t('dialog.scheduledForLine', { time: fmtLocal(scheduledDate) })}</span>
            </div>
          )}

          {/* Warnings */}
          <ul className="space-y-1.5">
            {[
              isSchedule ? 'warnExitOnlyScheduled' : 'warnExitOnly',
              'warnMarket',
            ].map(key => (
              <li key={key} className="flex items-start gap-2 text-sm text-gray-600">
                <span className="mt-0.5 text-amber-500 shrink-0">⚠</span>
                <span>{t(`dialog.${key}`)}</span>
              </li>
            ))}
          </ul>

          {/* Confirmation input */}
          <div className="pt-2">
            <label className="block text-sm font-semibold text-gray-700 mb-1">
              {t('dialog.typeToConfirm')}
            </label>
            <input
              autoFocus
              type="text"
              value={text}
              onChange={e => setText(e.target.value)}
              placeholder={t('dialog.placeholder')}
              className="w-full px-3 py-2 border-2 rounded-lg font-mono text-sm focus:outline-none uppercase tracking-widest"
              style={{ borderColor: confirmed ? '#22c55e' : '#d1d5db' }}
            />
          </div>

          {/* Error */}
          {error && (
            <div className="px-3 py-2 rounded-lg bg-red-50 border border-red-200 text-red-700 text-sm">
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-6 pb-5 flex gap-3 justify-end">
          <button
            onClick={onClose}
            disabled={isSubmitting}
            className="px-4 py-2 rounded-lg border border-gray-300 text-gray-700 text-sm font-medium hover:bg-gray-50 disabled:opacity-50 transition-colors"
          >
            {t('dialog.cancel')}
          </button>
          <button
            onClick={() => onConfirm(text)}
            disabled={!confirmed || isSubmitting}
            className={`px-5 py-2 rounded-lg text-white text-sm font-semibold transition-colors disabled:opacity-40 ${
              isSchedule
                ? 'bg-blue-600 hover:bg-blue-700'
                : 'bg-red-600 hover:bg-red-700'
            }`}
          >
            {isSubmitting ? '…' : isSchedule ? t('dialog.confirmSchedule') : t('dialog.confirmClose')}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Resume dialog ─────────────────────────────────────────────────────────

function ResumeDialog({ onConfirm, onClose, isSubmitting, error, t }) {
  const [text, setText] = useState('')
  const confirmed = text.toUpperCase() === 'RESUME TRADING'

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="px-6 py-4 rounded-t-2xl border-b bg-green-50 border-green-100">
          <div className="flex items-center gap-2">
            <span className="text-2xl">▶️</span>
            <h3 className="font-bold text-lg text-green-800">{t('resume.dialogTitle')}</h3>
          </div>
        </div>
        <div className="px-6 py-5 space-y-3">
          <p className="text-sm text-gray-600">{t('resume.dialogBody')}</p>
          <div className="pt-2">
            <label className="block text-sm font-semibold text-gray-700 mb-1">
              {t('resume.typeToConfirm')}
            </label>
            <input
              autoFocus
              type="text"
              value={text}
              onChange={e => setText(e.target.value)}
              placeholder={t('resume.placeholder')}
              className="w-full px-3 py-2 border-2 rounded-lg font-mono text-sm focus:outline-none uppercase tracking-widest"
              style={{ borderColor: confirmed ? '#22c55e' : '#d1d5db' }}
            />
          </div>
          {error && (
            <div className="px-3 py-2 rounded-lg bg-red-50 border border-red-200 text-red-700 text-sm">
              {error}
            </div>
          )}
        </div>
        <div className="px-6 pb-5 flex gap-3 justify-end">
          <button
            onClick={onClose}
            disabled={isSubmitting}
            className="px-4 py-2 rounded-lg border border-gray-300 text-gray-700 text-sm font-medium hover:bg-gray-50 disabled:opacity-50 transition-colors"
          >
            {t('dialog.cancel')}
          </button>
          <button
            onClick={() => onConfirm()}
            disabled={!confirmed || isSubmitting}
            className="px-5 py-2 rounded-lg bg-green-600 hover:bg-green-700 text-white text-sm font-semibold transition-colors disabled:opacity-40"
          >
            {isSubmitting ? '…' : t('resume.confirmBtn')}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Status badge ──────────────────────────────────────────────────────────

function StatusBadge({ status, t }) {
  const s = STATUS_STYLE[status] ?? STATUS_STYLE.Idle
  const labelKey = `status.${status.charAt(0).toLowerCase() + status.slice(1)}`
  return (
    <span
      className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-sm font-semibold border"
      style={{ background: s.bg, borderColor: s.border, color: s.text }}
    >
      <span
        className="w-2 h-2 rounded-full"
        style={{ background: s.dot,
          boxShadow: status === 'Executing' ? `0 0 6px ${s.dot}` : 'none',
          animation: status === 'Executing' ? 'pulse 1.5s infinite' : 'none',
        }}
      />
      {t(labelKey, { defaultValue: status })}
    </span>
  )
}

// ── History table ─────────────────────────────────────────────────────────

function HistoryTable({ t }) {
  const { data: history, loading, refresh } = useCloseAllHistory(15)
  const rows = Array.isArray(history) ? history : []

  return (
    <div className="bg-white rounded-2xl border border-gray-200 shadow-sm overflow-hidden">
      <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
        <h3 className="font-semibold text-gray-800">{t('history.title')}</h3>
        <button
          onClick={refresh}
          className="text-xs text-blue-600 hover:text-blue-800 font-medium px-2 py-1 rounded hover:bg-blue-50 transition-colors"
        >
          {loading ? '…' : t('history.refresh')}
        </button>
      </div>

      {rows.length === 0 ? (
        <p className="text-center text-gray-400 text-sm py-8">{t('history.noHistory')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b border-gray-100">
              <tr>
                {['type', 'status', 'by', 'requested', 'scheduled', 'closed', 'reason'].map(col => (
                  <th key={col} className="text-left px-4 py-2.5 text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    {t(`history.col.${col}`)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-50">
              {rows.map(row => {
                const s = STATUS_STYLE[row.status] ?? STATUS_STYLE.Idle
                return (
                  <tr key={row.operationId} className="hover:bg-gray-50">
                    <td className="px-4 py-2.5 text-gray-700 font-medium">
                      {t(`history.types.${row.operationType}`, { defaultValue: row.operationType })}
                    </td>
                    <td className="px-4 py-2.5">
                      <span
                        className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold border"
                        style={{ background: s.bg, borderColor: s.border, color: s.text }}
                      >
                        <span className="w-1.5 h-1.5 rounded-full" style={{ background: s.dot }} />
                        {row.status}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-gray-600">{row.requestedBy || '—'}</td>
                    <td className="px-4 py-2.5 text-gray-500 text-xs">{fmtLocal(row.requestedAtUtc)}</td>
                    <td className="px-4 py-2.5 text-gray-500 text-xs">{fmtLocal(row.scheduledForUtc)}</td>
                    <td className="px-4 py-2.5 text-gray-700 font-medium text-center">
                      {row.positionsClosedCount ?? 0}
                    </td>
                    <td className="px-4 py-2.5 text-gray-500 text-xs max-w-xs truncate">{row.reason || '—'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────

export function ShutdownControlPage() {
  const { t } = useTranslation('shutdown')
  const { data: status, loading: statusLoading, refresh: refreshStatus } = useCloseAllStatus()
  const { data: positions } = useOpenPositions()

  const [dialog, setDialog] = useState(null) // null | 'closeAll' | 'schedule'
  const [scheduledTime, setScheduledTime] = useState('') // datetime-local
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState(null)
  const [successMsg, setSuccessMsg] = useState(null)
  const [showResumeDialog, setShowResumeDialog] = useState(false)
  const [resumeError, setResumeError] = useState(null)
  const [isResuming, setIsResuming] = useState(false)

  const openPositions = Array.isArray(positions) ? positions.length : 0
  const opStatus = status?.status ?? 'Idle'
  const isActive = ACTIVE.has(opStatus)
  const style = STATUS_STYLE[opStatus] ?? STATUS_STYLE.Idle
  const tradingMode = status?.tradingMode ?? 'TradingEnabled'
  const resumeAllowed = status?.resumeAllowed ?? false
  const resumeBlockReasons = status?.resumeBlockReasons ?? []

  // Auto-refresh status when executing
  useEffect(() => {
    if (opStatus !== 'Executing') return
    const id = setInterval(refreshStatus, 3_000)
    return () => clearInterval(id)
  }, [opStatus, refreshStatus])

  const openDialog = useCallback((mode) => {
    setDialog(mode)
    setSubmitError(null)
  }, [])

  const closeDialog = useCallback(() => {
    setDialog(null)
    setSubmitError(null)
  }, [])

  const selectQuickOption = useCallback((minutes) => {
    const d = new Date(Date.now() + minutes * 60_000)
    const local = new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
    setScheduledTime(local)
  }, [])

  const handleConfirm = useCallback(async () => {
    setIsSubmitting(true)
    setSubmitError(null)
    try {
      const key = newKey()
      if (dialog === 'closeAll') {
        await apiCloseAllNow({ reason: 'manual', requestedBy: 'operator', idempotencyKey: key })
        setSuccessMsg(t('status.executing'))
      } else {
        const utc = new Date(scheduledTime).toISOString()
        await apiScheduleCloseAll({ executeAtUtc: utc, reason: 'scheduled_shutdown', requestedBy: 'operator', idempotencyKey: key })
        setSuccessMsg(t('actions.scheduleTitle'))
      }
      closeDialog()
      setTimeout(refreshStatus, 800)
    } catch (err) {
      setSubmitError(err.message ?? t('error.submitFailed'))
    } finally {
      setIsSubmitting(false)
    }
  }, [dialog, scheduledTime, closeDialog, refreshStatus, t])

  const handleCancelOperation = useCallback(async () => {
    if (!status?.operationId) return
    try {
      await apiCancelCloseAll(status.operationId)
      setTimeout(refreshStatus, 400)
    } catch (err) {
      setSuccessMsg(null)
    }
  }, [status, refreshStatus])

  const handleResume = useCallback(async () => {
    setIsResuming(true)
    setResumeError(null)
    try {
      await apiResumeTrading({ reason: 'operator_resume', requestedBy: 'operator' })
      setShowResumeDialog(false)
      setSuccessMsg(t('resume.success'))
      setTimeout(refreshStatus, 400)
    } catch (err) {
      setResumeError(err.message ?? t('error.submitFailed'))
    } finally {
      setIsResuming(false)
    }
  }, [refreshStatus, t])

  const scheduledDate = scheduledTime ? localInputToDate(scheduledTime) : null

  return (
    <div className="space-y-6">
      {/* Page header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">🛑 {t('title')}</h1>
          <p className="text-sm text-gray-500 mt-0.5">{t('subtitle')}</p>
        </div>
        {successMsg && (
          <div className="flex items-center gap-2 px-4 py-2 rounded-lg bg-green-50 border border-green-200 text-green-700 text-sm font-medium">
            <span>✓</span>
            <span>{successMsg}</span>
            <button onClick={() => setSuccessMsg(null)} className="ml-2 text-green-500 hover:text-green-700">✕</button>
          </div>
        )}
      </div>

      {/* Status card */}
      <div
        className="rounded-2xl border p-5 shadow-sm"
        style={{ background: style.bg, borderColor: style.border }}
      >
        <div className="flex flex-col sm:flex-row sm:items-start gap-4">
          {/* Left: status badge + details */}
          <div className="flex-1 space-y-3">
            <div className="flex items-center gap-3 flex-wrap">
              <span className="text-sm font-semibold text-gray-500">{t('status.title')}</span>
              <StatusBadge status={opStatus} t={t} />
              {status?.exitOnlyMode && (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-orange-100 border border-orange-200 text-orange-700 text-xs font-semibold">
                  🔒 {t('status.exitOnlyMode')}
                </span>
              )}
              {!status?.exitOnlyMode && tradingMode === 'TradingEnabled' && (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-green-100 border border-green-200 text-green-700 text-xs font-semibold">
                  ✅ {t('status.tradingEnabled')}
                </span>
              )}
              {status?.shutdownReady && (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-green-100 border border-green-200 text-green-700 text-xs font-semibold">
                  ✅ {t('status.shutdownReady')}
                </span>
              )}
            </div>

            {/* Stats row */}
            <div className="flex flex-wrap gap-4 text-sm">
              <div className="flex items-center gap-1.5">
                <span className="text-gray-400 text-xs">{t('status.openPositions')}</span>
                <span className={`font-bold ${openPositions > 0 ? 'text-amber-600' : 'text-green-600'}`}>
                  {statusLoading ? '…' : openPositions}
                </span>
              </div>
              {status?.positionsClosedCount > 0 && (
                <div className="flex items-center gap-1.5">
                  <span className="text-gray-400 text-xs">{t('status.positionsClosed')}</span>
                  <span className="font-bold text-gray-700">{status.positionsClosedCount}</span>
                </div>
              )}
              {status?.scheduledForUtc && (
                <div className="flex items-center gap-1.5">
                  <span className="text-gray-400 text-xs">{t('status.scheduledFor')}</span>
                  <span className="font-semibold" style={{ color: style.text }}>
                    {fmtLocal(status.scheduledForUtc)}
                  </span>
                  {opStatus === 'Scheduled' && (
                    <span className="text-blue-500 text-xs">
                      (<Countdown targetUtc={status.scheduledForUtc} />)
                    </span>
                  )}
                </div>
              )}
              {status?.startedAtUtc && (
                <div className="flex items-center gap-1.5">
                  <span className="text-gray-400 text-xs">{t('status.startedAt')}</span>
                  <span className="font-semibold text-gray-700">{fmtLocal(status.startedAtUtc)}</span>
                </div>
              )}
              {status?.completedAtUtc && (
                <div className="flex items-center gap-1.5">
                  <span className="text-gray-400 text-xs">{t('status.completedAt')}</span>
                  <span className="font-semibold text-gray-700">{fmtLocal(status.completedAtUtc)}</span>
                </div>
              )}
            </div>

            {status?.lastError && (
              <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-red-50 border border-red-200 text-red-700 text-sm">
                <span className="shrink-0">⚠</span>
                <span className="font-medium">{t('status.lastError')}: {status.lastError}</span>
              </div>
            )}
          </div>

          {/* Right: cancel button when scheduled */}
          {(opStatus === 'Scheduled' || opStatus === 'Requested') && (
            <button
              onClick={handleCancelOperation}
              className="shrink-0 px-4 py-2 rounded-lg border-2 border-gray-300 text-gray-600 text-sm font-semibold hover:border-red-300 hover:text-red-600 hover:bg-red-50 transition-colors"
            >
              {t('actions.cancelSchedule')}
            </button>
          )}
        </div>
      </div>

      {/* Completed banner */}
      {opStatus === 'Completed' && status?.shutdownReady && (
        <div className="rounded-2xl border border-green-200 bg-green-50 p-5 flex items-start gap-4 shadow-sm">
          <span className="text-3xl shrink-0">✅</span>
          <div>
            <p className="font-bold text-green-800 text-lg">{t('completedBanner.title')}</p>
            <p className="text-green-700 text-sm mt-0.5">
              {t('completedBanner.body', { count: status.positionsClosedCount ?? 0 })}
            </p>
          </div>
        </div>
      )}

      {/* Resume Trading — shown when in exit-only mode and resume is allowed */}
      {resumeAllowed && (
        <div className="rounded-2xl border border-green-200 bg-green-50 p-5 flex flex-col sm:flex-row sm:items-center gap-4 shadow-sm">
          <div className="flex-1">
            <p className="font-bold text-green-800">{t('resume.title')}</p>
            <p className="text-green-700 text-sm mt-0.5">{t('resume.desc')}</p>
          </div>
          <button
            onClick={() => { setShowResumeDialog(true); setResumeError(null) }}
            className="shrink-0 px-5 py-2.5 rounded-xl bg-green-600 hover:bg-green-700 text-white font-bold text-sm transition-colors shadow-sm"
          >
            ▶️ {t('resume.btn')}
          </button>
        </div>
      )}

      {/* Exit-only warning when resume is not yet allowed */}
      {status?.exitOnlyMode && !resumeAllowed && (
        <div className="rounded-2xl border border-orange-200 bg-orange-50 p-4 flex items-start gap-3">
          <span className="text-lg shrink-0">🔒</span>
          <div>
            <p className="font-semibold text-orange-800 text-sm">{t('resume.blockedTitle')}</p>
            {resumeBlockReasons.length > 0 && (
              <ul className="mt-1 space-y-0.5">
                {resumeBlockReasons.map((r, i) => (
                  <li key={i} className="text-xs text-orange-700">{r}</li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}

      {/* Actions — hidden while executing */}
      {opStatus !== 'Executing' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          {/* Close All Now */}
          <div className="bg-white rounded-2xl border border-gray-200 shadow-sm p-5 space-y-4">
            <div className="flex items-start gap-3">
              <span className="text-2xl shrink-0">⚡</span>
              <div>
                <h3 className="font-bold text-gray-900">{t('actions.closeAllNow')}</h3>
                <p className="text-sm text-gray-500 mt-0.5">{t('actions.closeAllDesc')}</p>
              </div>
            </div>

            {openPositions > 0 && (
              <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-100 text-amber-700 text-sm font-medium">
                <span>📂</span>
                <span>{openPositions} {t('status.openPositions').toLowerCase()}</span>
              </div>
            )}

            <button
              onClick={() => openDialog('closeAll')}
              disabled={isActive}
              className="w-full py-3 rounded-xl bg-red-600 hover:bg-red-700 text-white font-bold text-sm transition-colors disabled:opacity-40 disabled:cursor-not-allowed shadow-sm"
            >
              🛑 {t('actions.closeAllNow')}
            </button>

            {isActive && (
              <p className="text-xs text-center text-gray-400">{t('status.noActiveOp') + '…'}</p>
            )}
          </div>

          {/* Schedule Close All */}
          <div className="bg-white rounded-2xl border border-gray-200 shadow-sm p-5 space-y-4">
            <div className="flex items-start gap-3">
              <span className="text-2xl shrink-0">📅</span>
              <div>
                <h3 className="font-bold text-gray-900">{t('actions.scheduleTitle')}</h3>
                <p className="text-sm text-gray-500 mt-0.5">{t('actions.scheduleDesc')}</p>
              </div>
            </div>

            {/* Quick options */}
            <div>
              <p className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">
                {t('quick.label')}
              </p>
              <div className="flex flex-wrap gap-2">
                {[
                  { key: '5m',  minutes: 5   },
                  { key: '10m', minutes: 10  },
                  { key: '15m', minutes: 15  },
                  { key: '30m', minutes: 30  },
                  { key: '1h',  minutes: 60  },
                ].map(({ key, minutes }) => {
                  const btnTime = scheduledTime
                    ? new Date(scheduledTime).getTime()
                    : null
                  const optTime = Date.now() + minutes * 60_000
                  const isSelected = btnTime && Math.abs(btnTime - optTime) < 30_000
                  return (
                    <button
                      key={key}
                      onClick={() => selectQuickOption(minutes)}
                      disabled={isActive}
                      className={`px-3 py-1.5 rounded-lg text-sm font-semibold border-2 transition-colors disabled:opacity-40 ${
                        isSelected
                          ? 'border-blue-500 bg-blue-50 text-blue-700'
                          : 'border-gray-200 text-gray-600 hover:border-blue-300 hover:bg-blue-50'
                      }`}
                    >
                      {t(`quick.${key}`)}
                    </button>
                  )
                })}
              </div>
            </div>

            {/* Datetime picker */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wide mb-1.5">
                {t('picker.label')}
              </label>
              <input
                type="datetime-local"
                value={scheduledTime}
                min={minDateTimeLocal()}
                onChange={e => setScheduledTime(e.target.value)}
                disabled={isActive}
                className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg text-sm focus:outline-none focus:border-blue-400 disabled:opacity-40 disabled:bg-gray-50"
              />
            </div>

            {/* Preview */}
            {scheduledDate && (
              <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-blue-50 border border-blue-100 text-blue-700 text-sm">
                <span>🕐</span>
                <span>{t('picker.scheduledFor')}: <strong>{fmtLocal(scheduledDate)}</strong></span>
              </div>
            )}

            <button
              onClick={() => scheduledDate && openDialog('schedule')}
              disabled={!scheduledDate || isActive}
              className="w-full py-3 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-bold text-sm transition-colors disabled:opacity-40 disabled:cursor-not-allowed shadow-sm"
            >
              📅 {t('actions.confirmScheduleBtn')}
            </button>
          </div>
        </div>
      )}

      {/* Executing state */}
      {opStatus === 'Executing' && (
        <div className="bg-white rounded-2xl border border-orange-200 shadow-sm p-6 flex items-center gap-5">
          <div className="flex-shrink-0 w-12 h-12 rounded-full bg-orange-100 flex items-center justify-center animate-spin text-2xl">
            ⏳
          </div>
          <div>
            <p className="font-bold text-orange-800 text-lg">{t('status.executing')}</p>
            <p className="text-orange-600 text-sm">{t('status.exitOnlyMode')}</p>
          </div>
          <div className="ml-auto text-right">
            <p className="text-xs text-gray-400">{t('status.openPositions')}</p>
            <p className="text-2xl font-bold text-amber-600">{openPositions}</p>
          </div>
        </div>
      )}

      {/* History */}
      <HistoryTable t={t} />

      {/* Confirmation dialog */}
      {dialog && (
        <ConfirmDialog
          mode={dialog}
          scheduledDate={scheduledDate}
          positionCount={openPositions}
          onConfirm={handleConfirm}
          onClose={closeDialog}
          isSubmitting={isSubmitting}
          error={submitError}
          t={t}
        />
      )}

      {/* Resume dialog */}
      {showResumeDialog && (
        <ResumeDialog
          onConfirm={handleResume}
          onClose={() => setShowResumeDialog(false)}
          isSubmitting={isResuming}
          error={resumeError}
          t={t}
        />
      )}
    </div>
  )
}
