import { C } from '../theme'
import { WEEKDAYS as DAYS } from './format'

export interface HeatCell {
  /** Day of week, 0 = Sunday. */
  d: number
  /** Hour of day, 0..23. */
  h: number
  count: number
}

export interface HeatGridProps {
  /** Sparse is fine: a missing cell counts as 0. */
  data: HeatCell[]
  /** Cell height in px; also the cell width unless `fluid`. */
  cell?: number
  gap?: number
  accent?: string
  /** First row: 0 = Sunday (default), 1 = Monday. */
  weekStart?: 0 | 1
  /** Row labels: one letter (default) or three ("Mon"). */
  dayLabels?: 'letter' | 'short'
  /** Stretch the 24 columns over the parent width instead of fixed-size cells. */
  fluid?: boolean
  /** Draw the 00 / 06 / 12 / 18 / 23 hour axis under the grid. */
  hourAxis?: boolean
}

const AXIS_HOURS = ['00', '06', '12', '18', '23']

export function HeatGrid({
  data,
  cell = 13,
  gap = 3,
  accent = C.blurple,
  weekStart = 0,
  dayLabels = 'letter',
  fluid = false,
  hourAxis = false,
}: HeatGridProps) {
  const counts = new Map(data.map((x) => [x.d * 24 + x.h, x.count]))
  const max = Math.max(...counts.values(), 1)
  const short = dayLabels === 'short'
  const labelWidth = short ? 30 : 12
  const columns = `repeat(24, ${fluid ? 'minmax(0, 1fr)' : `${cell}px`})`

  return (
    <div style={{ display: fluid ? 'block' : 'inline-block' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap }}>
        {DAYS.map((_, i) => {
          const d = (i + weekStart) % 7
          return (
            <div key={d} style={{ display: 'flex', gap: gap + 2, alignItems: 'center' }}>
              <div
                style={{
                  width: labelWidth,
                  flexShrink: 0,
                  fontSize: short ? 11.5 : 10,
                  color: short ? C.muted : C.faint,
                  fontFamily: short ? 'inherit' : 'JetBrains Mono, monospace',
                  textAlign: short ? 'left' : 'right',
                }}
              >
                {short ? DAYS[d] : DAYS[d][0]}
              </div>
              <div style={{ flex: fluid ? 1 : 'none', display: 'grid', gridTemplateColumns: columns, gap }}>
                {Array.from({ length: 24 }, (_, h) => {
                  const v = counts.get(d * 24 + h) ?? 0
                  return (
                    <div
                      key={h}
                      title={`${DAYS[d]} ${String(h).padStart(2, '0')}:00 — ${v} ${v === 1 ? 'message' : 'messages'}`}
                      style={{
                        height: cell,
                        borderRadius: 3,
                        background: v === 0 ? C.bg : accent,
                        opacity: v === 0 ? 1 : Math.max(0.22, 0.18 + (v / max) * 0.82),
                      }}
                    />
                  )
                })}
              </div>
            </div>
          )
        })}
        {hourAxis && (
          <div style={{ display: 'flex', gap: gap + 2, marginTop: 1 }}>
            <span style={{ width: labelWidth, flexShrink: 0 }} />
            <div
              style={{
                flex: fluid ? 1 : 'none',
                width: fluid ? undefined : 24 * cell + 23 * gap,
                display: 'flex',
                justifyContent: 'space-between',
                fontFamily: 'JetBrains Mono, monospace',
                fontSize: 11,
                color: C.muted,
              }}
            >
              {AXIS_HOURS.map((h) => (
                <span key={h}>{h}</span>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
