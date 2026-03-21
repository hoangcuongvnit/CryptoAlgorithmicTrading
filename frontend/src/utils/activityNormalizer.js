// Normalize API payloads into a unified ActivityEvent model
// ActivityEvent: { eventId, timestampUtc, service, category, action, symbol, severity, status, message, details }

function makeId(prefix, ts, extra) {
  return `${prefix}-${ts}-${extra}`
}

export function normalizeValidations(recentValidations = []) {
  return recentValidations.map((v, idx) => ({
    eventId: makeId('risk', v.timestampUtc, `${v.symbol}-${v.side}-${idx}`),
    timestampUtc: v.timestampUtc,
    service: 'RiskGuard',
    category: 'RISK_EVALUATION',
    action: v.approved ? 'OrderApproved' : 'OrderRejected',
    symbol: v.symbol,
    severity: v.approved ? 'INFO' : 'WARN',
    status: v.approved ? 'SUCCESS' : 'REJECTED',
    message: v.approved
      ? `${v.symbol} ${v.side} approved by risk engine`
      : `${v.symbol} ${v.side} rejected — ${v.rejectionReason ?? 'Unknown reason'}`,
    details: {
      side: v.side,
      approved: v.approved,
      rejectionReason: v.rejectionReason ?? null,
    },
  }))
}

const NOTIF_MAP = {
  order:         { service: 'Executor', category: 'ORDER',  action: 'OrderExecuted', severity: 'INFO',  status: 'SUCCESS'  },
  order_rejected:{ service: 'Executor', category: 'ORDER',  action: 'OrderFailed',   severity: 'WARN',  status: 'REJECTED' },
  system_event:  { service: 'System',   category: 'SYSTEM', action: 'SystemEvent',   severity: 'INFO',  status: 'SUCCESS'  },
  startup:       { service: 'System',   category: 'SYSTEM', action: 'ServiceStarted',severity: 'INFO',  status: 'SUCCESS'  },
}

export function normalizeNotifications(recent = []) {
  return recent.map((n, idx) => {
    const mapped = NOTIF_MAP[n.category] ?? {
      service: 'System', category: 'SYSTEM', action: n.category, severity: 'INFO', status: 'SUCCESS',
    }
    return {
      eventId: makeId('notif', n.timestampUtc, `${n.category}-${idx}`),
      timestampUtc: n.timestampUtc,
      service: mapped.service,
      category: mapped.category,
      action: mapped.action,
      symbol: null,
      severity: mapped.severity,
      status: mapped.status,
      message: n.summary,
      details: null,
    }
  })
}

export function mergeAndSort(validations = [], notifications = []) {
  const all = [...normalizeValidations(validations), ...normalizeNotifications(notifications)]
  const seen = new Set()
  const deduped = all.filter(e => {
    if (seen.has(e.eventId)) return false
    seen.add(e.eventId)
    return true
  })
  deduped.sort((a, b) => new Date(b.timestampUtc) - new Date(a.timestampUtc))
  return deduped
}
