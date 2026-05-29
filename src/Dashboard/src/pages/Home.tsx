import { useQuery } from '@tanstack/react-query'
import { fetchApi } from '../api/client'

interface Ping {
  message: string
}

// Tracer-bullet landing view: confirms the SPA is served by the .NET host and
// that the /api surface is reachable end-to-end. Replaced by the Timeline feed
// in slice S2.
export default function Home() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['ping'],
    queryFn: () => fetchApi<Ping>('/ping'),
  })

  return (
    <div className="p-8">
      <h2 className="text-2xl font-semibold text-white">Wojtuś Event Dashboard</h2>
      <p className="mt-2 text-discord-muted">
        Visualising everything ingested from the Discord guild.
      </p>

      <div className="mt-6 inline-flex items-center gap-3 rounded-lg border border-discord-border bg-discord-bg-card px-4 py-3">
        <span className="text-sm text-discord-muted">Backend status:</span>
        {isLoading && <span className="text-discord-faint">checking…</span>}
        {isError && (
          <span className="text-discord-red">
            unreachable — {(error as Error).message}
          </span>
        )}
        {data && (
          <span className="flex items-center gap-2 text-discord-green">
            <span className="inline-block h-2 w-2 rounded-full bg-discord-green" />
            connected ({data.message})
          </span>
        )}
      </div>
    </div>
  )
}
