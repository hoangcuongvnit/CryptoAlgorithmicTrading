const BASE = ''  // proxied by Vite to http://localhost:5097

async function json(res) {
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText)
    throw new Error(`${res.status}: ${text}`)
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
  resetSession(accountId, newInitialBalance, algorithmName) {
    return fetch(`${BASE}/api/ledger/sessions/reset`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ accountId, newInitialBalance, algorithmName }),
    }).then(json)
  },
}
