// Display helpers. Timestamps render in the viewer's local zone (the guild runs
// ~CET, matching the user) with the exact ISO value available on hover.

export function formatTimestamp(value: unknown): { text: string; title: string } {
  if (value === null || value === undefined || value === '') {
    return { text: '—', title: '' }
  }
  const d = new Date(String(value))
  if (Number.isNaN(d.getTime())) {
    return { text: String(value), title: String(value) }
  }
  return { text: d.toLocaleString(), title: d.toISOString() }
}

export function relativeTime(value: unknown): string {
  if (value === null || value === undefined) return ''
  const d = new Date(String(value))
  if (Number.isNaN(d.getTime())) return String(value)
  const diffMs = d.getTime() - Date.now()
  const abs = Math.abs(diffMs)
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 31_536_000_000],
    ['month', 2_592_000_000],
    ['day', 86_400_000],
    ['hour', 3_600_000],
    ['minute', 60_000],
    ['second', 1000],
  ]
  for (const [unit, ms] of units) {
    if (abs >= ms || unit === 'second') {
      return rtf.format(Math.round(diffMs / ms), unit)
    }
  }
  return ''
}

export function truncate(text: string, max = 80): string {
  return text.length > max ? `${text.slice(0, max)}…` : text
}
