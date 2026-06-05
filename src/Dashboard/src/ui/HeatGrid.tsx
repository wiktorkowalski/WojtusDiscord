import { C } from '../theme'

export interface HeatCell {
  /** Day of week, 0 = Sunday. */
  d: number
  /** Hour of day, 0..23. */
  h: number
  count: number
}

export interface HeatGridProps {
  data: HeatCell[]
  cell?: number
  gap?: number
  accent?: string
}

const DAYS = ['S', 'M', 'T', 'W', 'T', 'F', 'S']

export function HeatGrid({ data, cell = 13, gap = 3, accent = C.blurple }: HeatGridProps) {
  const max = Math.max(...data.map((d) => d.count), 1)
  const get = (d: number, h: number): number =>
    data.find((x) => x.d === d && x.h === h)?.count ?? 0

  return (
    <div style={{ display: 'inline-block' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap }}>
        {DAYS.map((dl, d) => (
          <div key={d} style={{ display: 'flex', gap, alignItems: 'center' }}>
            <div
              style={{
                width: 12,
                fontSize: 10,
                color: C.faint,
                fontFamily: 'JetBrains Mono, monospace',
                textAlign: 'right',
                marginRight: 2,
              }}
            >
              {dl}
            </div>
            {Array.from({ length: 24 }, (_, h) => {
              const v = get(d, h)
              const t = v / max
              return (
                <div
                  key={h}
                  title={`${v} msgs`}
                  style={{
                    width: cell,
                    height: cell,
                    borderRadius: 3,
                    background: t < 0.04 ? C.bg : accent,
                    opacity: t < 0.04 ? 1 : 0.18 + t * 0.82,
                  }}
                />
              )
            })}
          </div>
        ))}
      </div>
    </div>
  )
}
