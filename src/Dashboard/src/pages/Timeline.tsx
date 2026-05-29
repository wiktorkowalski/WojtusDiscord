import { useMemo, useState } from 'react'
import { useTimeline } from '../hooks/useTimeline'
import type { TimelineFilters, TimelineEvent } from '../api/timelineApi'
import EventChip from '../components/EventChip'
import Modal from '../components/Modal'
import JsonViewer from '../components/JsonViewer'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'
import { formatTimestamp, relativeTime } from '../utils/format'

// Landing view: the unified, filterable activity feed across all event types.
export default function Timeline() {
  const [draft, setDraft] = useState<TimelineFilters>({})
  const [filters, setFilters] = useState<TimelineFilters>({})
  const [selected, setSelected] = useState<TimelineEvent | null>(null)

  const { data, isLoading, isError, error, fetchNextPage, hasNextPage, isFetchingNextPage, refetch } =
    useTimeline(filters)

  const events = useMemo(() => data?.pages.flatMap((p) => p.events) ?? [], [data])

  const setField = (key: keyof TimelineFilters, value: string) =>
    setDraft((d) => ({ ...d, [key]: value || undefined }))

  return (
    <div className="p-8">
      <PageHeader
        title="Timeline"
        subtitle="Every gateway event, newest first"
        actions={
          <button
            onClick={() => refetch()}
            className="rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-muted hover:text-white"
          >
            ⟳ refresh
          </button>
        }
      />

      <div className="mb-5 flex flex-wrap items-center gap-2">
        <input
          value={draft.eventType ?? ''}
          onChange={(e) => setField('eventType', e.target.value)}
          placeholder="event type(s), comma-separated"
          className="w-64 rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-text"
        />
        <input
          value={draft.userId ?? ''}
          onChange={(e) => setField('userId', e.target.value)}
          placeholder="user id"
          className="mono w-40 rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-text"
        />
        <input
          value={draft.channelId ?? ''}
          onChange={(e) => setField('channelId', e.target.value)}
          placeholder="channel id"
          className="mono w-40 rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-text"
        />
        <button
          onClick={() => setFilters(draft)}
          className="rounded bg-blurple px-3 py-1.5 text-sm text-white hover:bg-blurple-hover"
        >
          Apply
        </button>
        {Object.keys(filters).length > 0 && (
          <button
            onClick={() => {
              setDraft({})
              setFilters({})
            }}
            className="text-sm text-discord-muted hover:text-white"
          >
            clear
          </button>
        )}
      </div>

      {isLoading ? (
        <Loading />
      ) : isError ? (
        <ErrorState error={error} />
      ) : (
        <>
          <ul className="space-y-1">
            {events.map((ev) => (
              <li
                key={ev.id}
                onClick={() => setSelected(ev)}
                className="flex cursor-pointer items-center gap-3 rounded-lg border border-discord-border bg-discord-bg-card px-3 py-2 hover:border-blurple"
              >
                <span
                  className="w-28 flex-shrink-0 text-xs text-discord-faint"
                  title={formatTimestamp(ev.receivedAtUtc).title}
                >
                  {relativeTime(ev.receivedAtUtc)}
                </span>
                <EventChip eventType={ev.eventType} />
                {ev.serializationFailed && (
                  <span className="rounded bg-discord-red/20 px-1.5 py-0.5 text-xs text-discord-red">
                    serialization failed
                  </span>
                )}
                <span className="ml-auto flex items-center gap-3 text-xs text-discord-faint">
                  {ev.userDiscordId && <span className="mono">user {ev.userDiscordId}</span>}
                  {ev.channelDiscordId && <span className="mono">chan {ev.channelDiscordId}</span>}
                  <span>{ev.jsonSizeBytes} B</span>
                </span>
              </li>
            ))}
          </ul>

          {events.length === 0 && <p className="mt-8 text-center text-discord-faint">No events.</p>}

          {hasNextPage && (
            <div className="mt-5 flex justify-center">
              <button
                onClick={() => fetchNextPage()}
                disabled={isFetchingNextPage}
                className="rounded bg-discord-bg-dark px-4 py-2 text-sm text-discord-text hover:bg-discord-bg-card disabled:opacity-40"
              >
                {isFetchingNextPage ? 'loading…' : 'Load more'}
              </button>
            </div>
          )}
        </>
      )}

      {selected && (
        <Modal title={`${selected.eventType} · ${formatTimestamp(selected.receivedAtUtc).text}`} onClose={() => setSelected(null)}>
          <JsonViewer value={selected.payload} />
        </Modal>
      )}
    </div>
  )
}
