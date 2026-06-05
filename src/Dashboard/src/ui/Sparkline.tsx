import { useId } from 'react'
import { C } from '../theme'
import { smoothPath } from './format'
import type { Pt } from './format'

/** Sparkline accepts raw numbers or objects carrying a `count`. */
export type SeriesPoint = number | { count: number }

export interface SparklineProps {
  data: SeriesPoint[]
  w?: number
  h?: number
  color?: string
  fill?: boolean
  strokeW?: number
}

export function Sparkline({
  data,
  w = 120,
  h = 34,
  color = C.blurple,
  fill = true,
  strokeW = 2,
}: SparklineProps) {
  const rawId = useId()
  const gid = 'sg' + rawId.replace(/:/g, '')

  const vals = data.map((d) => (typeof d === 'number' ? d : d.count))
  const max = Math.max(...vals, 1)
  const min = Math.min(...vals, 0)
  const pts: Pt[] = vals.map((v, i) => [
    (i / (vals.length - 1)) * w,
    h - 2 - ((v - min) / (max - min || 1)) * (h - 4),
  ])
  const line = smoothPath(pts)

  return (
    <svg width={w} height={h} style={{ display: 'block', overflow: 'visible' }}>
      {fill && (
        <defs>
          <linearGradient id={gid} x1={0} y1={0} x2={0} y2={1}>
            <stop offset="0%" stopColor={color} stopOpacity={0.32} />
            <stop offset="100%" stopColor={color} stopOpacity={0} />
          </linearGradient>
        </defs>
      )}
      {fill && <path d={`${line} L ${w},${h} L 0,${h} Z`} fill={`url(#${gid})`} stroke="none" />}
      <path
        d={line}
        fill="none"
        stroke={color}
        strokeWidth={strokeW}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}
