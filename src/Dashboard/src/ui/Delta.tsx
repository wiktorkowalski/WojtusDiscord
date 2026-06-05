import { C } from '../theme'

export interface DeltaProps {
  /** Current period value. */
  cur: number
  /** Previous period value — when null/undefined the pill renders nothing. */
  prev?: number | null
  small?: boolean
}

export function Delta({ cur, prev, small = false }: DeltaProps) {
  if (prev == null) return null

  // When the previous period had ~no data, a percentage is meaningless/explosive
  // (e.g. prev=1 -> +1192000%). Show "new" instead. Otherwise cap the magnitude so a
  // huge swing reads as ">999%" rather than a wall of digits.
  const isNew = prev <= 0 && cur > 0
  const diff = prev <= 0 ? (cur > 0 ? 100 : 0) : Math.round(((cur - prev) / prev) * 100)
  const up = diff >= 0
  const col = up ? C.green : C.red
  const text = isNew ? 'new' : Math.abs(diff) > 999 ? '>999%' : `${Math.abs(diff)}%`

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 3,
        color: col,
        fontSize: small ? 11 : 12.5,
        fontWeight: 600,
        fontFamily: 'JetBrains Mono, monospace',
        background: up ? 'rgba(59,165,93,.12)' : 'rgba(237,66,69,.12)',
        padding: '2px 6px',
        borderRadius: 6,
      }}
    >
      <svg
        width={9}
        height={9}
        viewBox="0 0 10 10"
        fill="none"
        stroke={col}
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
        style={{ transform: up ? 'none' : 'scaleY(-1)' }}
      >
        <path d="M5 8V2M2 5l3-3 3 3" />
      </svg>
      {text}
    </span>
  )
}
