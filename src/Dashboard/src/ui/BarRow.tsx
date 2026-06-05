import { C } from '../theme'

export interface BarRowProps {
  /** Fill fraction 0..1. */
  pct: number
  color?: string
  h?: number
  /** Draw the track background behind the fill. */
  track?: boolean
}

export function BarRow({ pct, color = C.blurple, h = 6, track = true }: BarRowProps) {
  return (
    <div
      style={{
        height: h,
        borderRadius: h,
        background: track ? C.bg : 'transparent',
        overflow: 'hidden',
        width: '100%',
      }}
    >
      <div
        style={{
          height: '100%',
          width: `${Math.max(2, pct * 100)}%`,
          borderRadius: h,
          background: color,
          transition: 'width .6s cubic-bezier(.22,1,.36,1)',
        }}
      />
    </div>
  )
}
