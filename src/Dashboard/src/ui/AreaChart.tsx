import { useId } from 'react'
import { C } from '../theme'
import { smoothPath } from './format'
import type { Pt } from './format'

export interface AreaPoint {
  count: number
}

export interface AreaChartProps {
  data: AreaPoint[]
  w?: number
  h?: number
  color?: string
  pad?: number
}

export function AreaChart({ data, w = 760, h = 240, color = C.blurple, pad = 28 }: AreaChartProps) {
  const rawId = useId()
  const gid = 'ac' + rawId.replace(/:/g, '')

  const vals = data.map((d) => d.count)
  const max = Math.max(...vals, 1)
  const iw = w - pad * 2
  const ih = h - pad * 1.4
  const y = (v: number) => pad / 2 + ih - (v / max) * ih
  // A single data point has no span to interpolate across (i/(n-1) = 0/0 = NaN,
  // which propagates to a blank chart); render it as a flat full-width line.
  const pts: Pt[] =
    vals.length === 1
      ? [[pad, y(vals[0])], [pad + iw, y(vals[0])]]
      : vals.map((v, i) => [pad + (i / (vals.length - 1)) * iw, y(v)])
  const line = smoothPath(pts)
  const last = pts[pts.length - 1]
  const grid = [0, 0.25, 0.5, 0.75, 1]

  return (
    <svg
      viewBox={`0 0 ${w} ${h}`}
      width="100%"
      style={{ display: 'block', overflow: 'visible' }}
    >
      <defs>
        <linearGradient id={gid} x1={0} y1={0} x2={0} y2={1}>
          <stop offset="0%" stopColor={color} stopOpacity={0.38} />
          <stop offset="100%" stopColor={color} stopOpacity={0.02} />
        </linearGradient>
      </defs>
      {grid.map((g, i) => (
        <line
          key={i}
          x1={pad}
          x2={w - pad}
          y1={pad / 2 + g * ih}
          y2={pad / 2 + g * ih}
          stroke={C.border}
          strokeWidth={1}
          strokeDasharray={i === grid.length - 1 ? '0' : '3 4'}
          opacity={0.6}
        />
      ))}
      <path d={`${line} L ${pad + iw},${pad / 2 + ih} L ${pad},${pad / 2 + ih} Z`} fill={`url(#${gid})`} />
      <path
        d={line}
        fill="none"
        stroke={color}
        strokeWidth={2.4}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx={last[0]} cy={last[1]} r={4} fill={color} stroke={C.bg1} strokeWidth={2} />
    </svg>
  )
}
