import type { ReactNode } from 'react'

// Bordered panel with a title; wraps a chart or list.
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
    <div className="rounded-lg border border-discord-border bg-discord-bg-card p-4">
      <h3 className="text-sm font-semibold text-white">{title}</h3>
      {note && <p className="mb-2 mt-0.5 text-xs text-discord-faint">{note}</p>}
      <div className={note ? '' : 'mt-3'}>{children}</div>
    </div>
  )
}
