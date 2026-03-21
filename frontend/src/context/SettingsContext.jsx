import { createContext, useContext, useState, useEffect } from 'react'

const SettingsContext = createContext({ systemTimezone: 'UTC', setSystemTimezone: () => {} })

export function SettingsProvider({ children }) {
  const [systemTimezone, setSystemTimezone] = useState('UTC')

  useEffect(() => {
    fetch('/api/settings/system')
      .then(r => (r.ok ? r.json() : null))
      .then(data => { if (data?.timezone) setSystemTimezone(data.timezone) })
      .catch(() => {})
  }, [])

  return (
    <SettingsContext.Provider value={{ systemTimezone, setSystemTimezone }}>
      {children}
    </SettingsContext.Provider>
  )
}

export function useSettings() {
  return useContext(SettingsContext)
}
