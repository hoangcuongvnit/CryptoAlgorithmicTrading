/**
 * Format a UTC timestamp as a time string (HH:MM or HH:MM:SS) in the given IANA timezone.
 * Falls back to UTC on any error.
 *
 * @param {string|Date|null} utcTimestamp
 * @param {string} timezone  IANA timezone ID, e.g. "Asia/Ho_Chi_Minh"
 * @param {{ seconds?: boolean }} opts
 */
export function formatTime(utcTimestamp, timezone = 'UTC', opts = {}) {
  if (!utcTimestamp) return ''
  try {
    return new Date(utcTimestamp).toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      ...(opts.seconds ? { second: '2-digit' } : {}),
      hour12: false,
      timeZone: timezone || 'UTC',
    })
  } catch {
    return new Date(utcTimestamp).toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      timeZone: 'UTC',
    })
  }
}

/**
 * Format a UTC timestamp as a locale date+time string in the given IANA timezone.
 */
export function formatDateTime(utcTimestamp, timezone = 'UTC') {
  if (!utcTimestamp) return ''
  try {
    return new Date(utcTimestamp).toLocaleString('en-US', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
      timeZone: timezone || 'UTC',
    })
  } catch {
    return new Date(utcTimestamp).toLocaleString()
  }
}
