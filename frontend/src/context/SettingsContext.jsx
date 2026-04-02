import { createContext, useContext, useState, useEffect, useCallback } from 'react'

const SettingsContext = createContext({
  systemTimezone: 'UTC',
  setSystemTimezone: () => {},
  tradingMode: null,       // 'paper' | 'testnet' | 'live' | null (loading)
  refreshTradingMode: () => {},
})

async function fetchTradingMode() {
  try {
    const [trading, exchange] = await Promise.all([
      fetch('/api/settings/trading/mode').then(r => r.ok ? r.json() : null),
      fetch('/api/settings/exchange/binance').then(r => r.ok ? r.json() : null),
    ])
    if (!trading) return null
    if (trading.paperTradingMode) return 'paper'
    if (exchange?.useTestnet) return 'testnet'
    return 'live'
  } catch {
    return null
  }
}

export function SettingsProvider({ children }) {
  const [systemTimezone, setSystemTimezone] = useState('UTC')
  const [tradingMode, setTradingMode] = useState(null)

  const refreshTradingMode = useCallback(async () => {
    const mode = await fetchTradingMode()
    if (mode) setTradingMode(mode)
  }, [])

  useEffect(() => {
    fetch('/api/settings/system')
      .then(r => (r.ok ? r.json() : null))
      .then(data => { if (data?.timezone) setSystemTimezone(data.timezone) })
      .catch(() => {})

    refreshTradingMode()
  }, [refreshTradingMode])

  return (
    <SettingsContext.Provider value={{ systemTimezone, setSystemTimezone, tradingMode, refreshTradingMode }}>
      {children}
    </SettingsContext.Provider>
  )
}

export function useSettings() {
  return useContext(SettingsContext)
}
