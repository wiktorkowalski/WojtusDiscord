import { colors } from '../theme'
import { formatNumber } from '../utils/format'

export interface LeaderboardRow {
  label: string
  sublabel?: string
  value: number
}

// Horizontal ranked bars — used for People/Places leaderboards without a chart lib.
export default function Leaderboard({
  rows,
  unit,
  color = colors.blurple,
}: {
  rows: LeaderboardRow[]
  unit?: string
  color?: string
}) {
  const max = Math.max(1, ...rows.map((r) => r.value))
  if (rows.length === 0) {
    return <p className="text-sm text-discord-faint">No data.</p>
  }

  return (
    <ol className="space-y-1.5">
      {rows.map((r, i) => (
        <li key={i} className="flex items-center gap-3">
          <span className="w-5 text-right text-xs text-discord-faint">{i + 1}</span>
          <div className="min-w-0 flex-1">
            <div className="mb-0.5 flex items-baseline justify-between gap-2">
              <span className="truncate text-sm text-discord-text">
                {r.label}
                {r.sublabel && <span className="mono ml-1 text-xs text-discord-faint">{r.sublabel}</span>}
              </span>
              <span className="tabular-nums text-sm text-discord-muted">
                {formatNumber(r.value)}
                {unit ? ` ${unit}` : ''}
              </span>
            </div>
            <div className="h-1.5 overflow-hidden rounded bg-discord-bg-dark">
              <div
                className="h-full rounded"
                style={{ width: `${(r.value / max) * 100}%`, backgroundColor: color }}
              />
            </div>
          </div>
        </li>
      ))}
    </ol>
  )
}
