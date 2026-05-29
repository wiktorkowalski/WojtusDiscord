// Small shared loading / error / empty states for query-backed views.

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <div className="p-8 text-discord-faint">{label}</div>
}

export function ErrorState({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : String(error)
  return (
    <div className="m-8 rounded-lg border border-discord-red/40 bg-discord-red/10 p-4 text-discord-red">
      Failed to load: {message}
    </div>
  )
}

export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string
  subtitle?: string
  actions?: React.ReactNode
}) {
  return (
    <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h2 className="text-2xl font-semibold text-white">{title}</h2>
        {subtitle && <p className="mt-1 text-sm text-discord-muted">{subtitle}</p>}
      </div>
      {actions}
    </div>
  )
}
