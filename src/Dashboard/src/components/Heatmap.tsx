import type { HeatmapCell } from '../api/statsApi'

const DOW = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

// 7 (day-of-week) x 24 (hour, guild-local) activity grid; intensity = count.
export default function Heatmap({ cells }: { cells: HeatmapCell[] }) {
  const grid = new Map<string, number>()
  let max = 0
  for (const c of cells) {
    grid.set(`${c.dayOfWeek}-${c.hour}`, c.count)
    if (c.count > max) max = c.count
  }

  return (
    <div className="overflow-x-auto">
      <table className="border-separate border-spacing-0.5 text-xs">
        <thead>
          <tr>
            <th />
            {Array.from({ length: 24 }, (_, h) => (
              <th key={h} className="px-0.5 font-normal text-discord-faint">
                {h % 6 === 0 ? h : ''}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {DOW.map((label, dow) => (
            <tr key={dow}>
              <td className="pr-1.5 text-right text-discord-faint">{label}</td>
              {Array.from({ length: 24 }, (_, hour) => {
                const count = grid.get(`${dow}-${hour}`) ?? 0
                const intensity = max > 0 ? count / max : 0
                return (
                  <td key={hour} className="p-0">
                    <div
                      title={`${label} ${hour}:00 — ${count}`}
                      className="h-5 w-5 rounded-sm"
                      style={{
                        backgroundColor: intensity === 0 ? '#1e1f22' : `rgba(88,101,242,${0.15 + intensity * 0.85})`,
                      }}
                    />
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
