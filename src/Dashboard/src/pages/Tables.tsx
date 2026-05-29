import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { tablesApi } from '../api/tablesApi'
import { formatNumber } from '../utils/format'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'

// Generic schema explorer entry point: every EF-mapped table, populated ones first.
export default function Tables() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['tables'],
    queryFn: tablesApi.list,
  })

  if (isLoading) return <Loading />
  if (isError) return <ErrorState error={error} />

  const tables = data ?? []
  const populated = tables.filter((t) => t.populated)
  const empty = tables.filter((t) => !t.populated)

  return (
    <div className="p-8">
      <PageHeader
        title="Tables"
        subtitle={`${tables.length} tables · ${populated.length} with data`}
      />

      <Section title="With data" tables={populated} />
      {empty.length > 0 && <Section title="Empty" tables={empty} muted />}
    </div>
  )
}

function Section({
  title,
  tables,
  muted,
}: {
  title: string
  tables: { name: string; displayName: string; entityName: string; rowCount: number }[]
  muted?: boolean
}) {
  if (tables.length === 0) return null
  return (
    <section className="mb-8">
      <h3 className="mb-3 text-sm font-semibold uppercase tracking-wide text-discord-faint">
        {title}
      </h3>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {tables.map((t) => (
          <Link
            key={t.name}
            to={`/tables/${encodeURIComponent(t.name)}`}
            className={`rounded-lg border border-discord-border bg-discord-bg-card p-3 transition-colors hover:border-blurple ${
              muted ? 'opacity-60 hover:opacity-100' : ''
            }`}
          >
            <div className="flex items-baseline justify-between gap-2">
              <span className="mono truncate text-sm text-discord-text">{t.name}</span>
              <span className="tabular-nums text-sm text-discord-muted">
                {formatNumber(t.rowCount)}
              </span>
            </div>
            <div className="mt-1 truncate text-xs text-discord-faint">{t.entityName}</div>
          </Link>
        ))}
      </div>
    </section>
  )
}
