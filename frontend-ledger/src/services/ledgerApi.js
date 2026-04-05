const BASE = ''  // proxied by Vite to http://localhost:5097

export class ApiError extends Error {
  constructor(status, message, body = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

async function json(res) {
  if (!res.ok) {
    let body = null
    let text = ''
    try {
      text = await res.text()
      body = text ? JSON.parse(text) : null
    } catch {
      body = null
    }

    const message =
      (body && typeof body.message === 'string' && body.message) ||
      text ||
      res.statusText ||
      'Request failed'

    throw new ApiError(res.status, `${res.status}: ${message}`, body)
  }
  return res.json()
}

export const ledgerApi = {
  // Bootstrap / get-or-create account for environment
  bootstrap(environment = 'TESTNET', baseCurrency = 'USDT') {
    const params = new URLSearchParams({ environment, baseCurrency })
    return fetch(`${BASE}/api/ledger/accounts/bootstrap?${params}`, { method: 'POST' }).then(json)
  },

  // GET active session summary (balance, net PnL, ROE)
  getAccount(accountId) {
    return fetch(`${BASE}/api/ledger/account/${accountId}`).then(json)
  },

  // GET paginated ledger entries
  getEntries(sessionId, { fromDate, toDate, symbol, type, page = 1, pageSize = 50 } = {}) {
    const params = new URLSearchParams({ sessionId, page, pageSize })
    if (fromDate) params.append('fromDate', fromDate)
    if (toDate)   params.append('toDate',   toDate)
    if (symbol)   params.append('symbol',   symbol)
    if (type)     params.append('type',     type)
    return fetch(`${BASE}/api/ledger/entries?${params}`).then(json)
  },

  // GET sessions for account
  getSessions(accountId, status = 'ALL') {
    const params = new URLSearchParams({ status })
    return fetch(`${BASE}/api/ledger/sessions/${accountId}?${params}`).then(json)
  },

  // GET P&L breakdown by symbol
  getPnl(sessionId, symbol) {
    const params = new URLSearchParams({ sessionId })
    if (symbol) params.append('symbol', symbol)
    return fetch(`${BASE}/api/ledger/pnl?${params}`).then(json)
  },

  // POST reset session (archive old, create new)
  resetSession(accountId, newInitialBalance, algorithmName, options = {}) {
    return fetch(`${BASE}/api/ledger/sessions/reset`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        accountId,
        newInitialBalance,
        algorithmName,
        confirmCloseAll: options.confirmCloseAll ?? false,
        requestedBy: options.requestedBy,
      }),
    }).then(json)
  },
}
