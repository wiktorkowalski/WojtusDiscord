import { C } from '../theme'

export interface DonutProps {
  value: number
  max: number
  size?: number
  stroke?: number
  color?: string
  track?: string
}

export function Donut({
  value,
  max,
  size = 80,
  stroke = 9,
  color = C.blurple,
  track = C.bg,
}: DonutProps) {
  const r = (size - stroke) / 2
  const cx = size / 2
  const c = 2 * Math.PI * r
  const pct = Math.min(1, value / max)

  return (
    <svg width={size} height={size} style={{ transform: 'rotate(-90deg)' }}>
      <circle cx={cx} cy={cx} r={r} fill="none" stroke={track} strokeWidth={stroke} />
      <circle
        cx={cx}
        cy={cx}
        r={r}
        fill="none"
        stroke={color}
        strokeWidth={stroke}
        strokeLinecap="round"
        strokeDasharray={c}
        strokeDashoffset={c * (1 - pct)}
        style={{ transition: 'stroke-dashoffset .8s cubic-bezier(.22,1,.36,1)' }}
      />
    </svg>
  )
}
