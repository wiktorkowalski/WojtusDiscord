import type { ReactNode } from 'react'

// Titled surface that wraps a chart or list.
export default function ChartCard({
  title,
  note,
  children,
}: {
  title: string
  note?: string
  children: ReactNode
}) {
  return (
    <div className="card p-5">
      <h3 className="text-sm font-semibold tracking-wide text-white">{title}</h3>
      {note && <p className="mb-3 mt-0.5 text-xs text-discord-faint">{note}</p>}
      <div className={note ? '' : 'mt-4'}>{children}</div>
    </div>
  )
}
