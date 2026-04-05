import { useEffect, useRef, useState, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'

/**
 * Connects to the LedgerHub SignalR endpoint.
 * Returns: { isConnected, lastEntry, lastBalance, lastEquity, onEntry, onBalance, onEquity }
 *
 * Callers can pass event-handler callbacks which are called whenever the server pushes:
 *   - ReceiveLedgerEntry  -> onEntry(entry)
 *   - ReceiveBalanceUpdate -> onBalance(balanceData)
 *   - ReceiveEquityUpdate  -> onEquity(equityData)
 *   - ReceiveSessionUpdate -> onSession(sessionData)
 */
export function useLedgerSignalR({ onEntry, onBalance, onEquity, onSession } = {}) {
  const connectionRef = useRef(null)
  const [isConnected, setIsConnected] = useState(false)
  const [lastEntry, setLastEntry]     = useState(null)
  const [lastBalance, setLastBalance] = useState(null)
  const [lastEquity, setLastEquity]   = useState(null)

  // Keep callbacks in refs so the effect doesn't re-run when they change
  const onEntryRef   = useRef(onEntry)
  const onBalanceRef = useRef(onBalance)
  const onEquityRef  = useRef(onEquity)
  const onSessionRef = useRef(onSession)
  useEffect(() => { onEntryRef.current   = onEntry   }, [onEntry])
  useEffect(() => { onBalanceRef.current = onBalance }, [onBalance])
  useEffect(() => { onEquityRef.current  = onEquity  }, [onEquity])
  useEffect(() => { onSessionRef.current = onSession }, [onSession])

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/ledger-hub')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connection.on('ReceiveLedgerEntry', (entry) => {
      setLastEntry(entry)
      onEntryRef.current?.(entry)
    })

    connection.on('ReceiveBalanceUpdate', (data) => {
      setLastBalance(data)
      onBalanceRef.current?.(data)
    })

    connection.on('ReceiveEquityUpdate', (data) => {
      setLastEquity(data)
      onEquityRef.current?.(data)
    })

    connection.on('ReceiveSessionUpdate', (data) => {
      onSessionRef.current?.(data)
    })

    connection.onreconnected(() => setIsConnected(true))
    connection.onreconnecting(() => setIsConnected(false))
    connection.onclose(() => setIsConnected(false))

    connection.start()
      .then(() => setIsConnected(true))
      .catch((err) => console.error('SignalR connection error:', err))

    connectionRef.current = connection

    return () => {
      connection.stop()
    }
  }, [])

  return { isConnected, lastEntry, lastBalance, lastEquity }
}
