import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { rawEventsApi } from '../api/rawEventsApi'
import EventChip from '../components/EventChip'
import Pagination from '../components/Pagination'
import Modal from '../components/Modal'
import JsonViewer from '../components/JsonViewer'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'
import { formatTimestamp } from '../utils/format'

// Technical lens over raw_event_logs: filter by type / failed, inspect payloads.
export default function RawExplorer() {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [eventType, setEventType] = useState('')
  const [failedOnly, setFailedOnly] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const typesQuery = useQuery({ queryKey: ['raw-types'], queryFn: rawEventsApi.types })
  const listQuery = useQuery({
    queryKey: ['raw-events', page, pageSize, eventType, failedOnly],
    queryFn: () =>
      rawEventsApi.list({ page, pageSize, eventType: eventType || undefined, failedOnly }),
    placeholderData: keepPreviousData,
  })
  const detailQuery = useQuery({
    queryKey: ['raw-event', selectedId],
    queryFn: () => rawEventsApi.detail(selectedId!),
    enabled: selectedId !== null,
  })

  return (
    <div className="p-8">
      <PageHeader title="Raw events" subtitle="Unprocessed gateway payloads" />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <select
          value={eventType}
          onChange={(e) => {
            setEventType(e.target.value)
            setPage(1)
          }}
          className="rounded bg-discord-bg-dark px-2 py-1.5 text-sm text-discord-text"
        >
          <option value="">all event types</option>
          {typesQuery.data?.map((t) => (
            <option key={t.eventType} value={t.eventType}>
              {t.eventType} ({t.count})
            </option>
          ))}
        </select>
        <label className="flex items-center gap-2 text-sm text-discord-muted">
          <input
            type="checkbox"
            checked={failedOnly}
            onChange={(e) => {
              setFailedOnly(e.target.checked)
              setPage(1)
            }}
          />
          failed only
        </label>
        <button
          onClick={() => listQuery.refetch()}
          className="ml-auto rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-muted hover:text-white"
        >
          ⟳ refresh
        </button>
      </div>

      {listQuery.isLoading ? (
        <Loading />
      ) : listQuery.isError ? (
        <ErrorState error={listQuery.error} />
      ) : (
        <>
          <div className="overflow-x-auto rounded-lg border border-discord-border">
            <table className="w-full text-sm">
              <thead className="bg-discord-bg-alt text-discord-muted">
                <tr>
                  <th className="px-3 py-2 text-left font-semibold">Received</th>
                  <th className="px-3 py-2 text-left font-semibold">Type</th>
                  <th className="px-3 py-2 text-left font-semibold">User</th>
                  <th className="px-3 py-2 text-left font-semibold">Channel</th>
                  <th className="px-3 py-2 text-right font-semibold">Size</th>
                </tr>
              </thead>
              <tbody>
                {listQuery.data!.items.map((ev) => (
                  <tr
                    key={ev.id}
                    onClick={() => setSelectedId(ev.id)}
                    className="cursor-pointer border-b border-discord-border/40 hover:bg-discord-bg-card"
                  >
                    <td className="whitespace-nowrap px-3 py-1.5 text-discord-muted" title={formatTimestamp(ev.receivedAtUtc).title}>
                      {formatTimestamp(ev.receivedAtUtc).text}
                    </td>
                    <td className="px-3 py-1.5">
                      <EventChip eventType={ev.eventType} />
                      {ev.serializationFailed && (
                        <span className="ml-2 rounded bg-discord-red/20 px-1.5 py-0.5 text-xs text-discord-red">
                          failed
                        </span>
                      )}
                    </td>
                    <td className="mono px-3 py-1.5 text-discord-faint">{ev.userDiscordId ?? '—'}</td>
                    <td className="mono px-3 py-1.5 text-discord-faint">{ev.channelDiscordId ?? '—'}</td>
                    <td className="px-3 py-1.5 text-right tabular-nums text-discord-faint">{ev.jsonSizeBytes} B</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="mt-4">
            <Pagination
              page={page}
              pageSize={pageSize}
              total={listQuery.data!.totalCount}
              onPageChange={setPage}
              onPageSizeChange={(s) => {
                setPageSize(s)
                setPage(1)
              }}
            />
          </div>
        </>
      )}

      {selectedId && (
        <Modal title="Raw event payload" onClose={() => setSelectedId(null)}>
          {detailQuery.isLoading && <Loading />}
          {detailQuery.isError && <ErrorState error={detailQuery.error} />}
          {detailQuery.data && (
            <>
              <div className="mb-3 flex flex-wrap gap-x-6 gap-y-1 text-xs text-discord-muted">
                <span>
                  <EventChip eventType={detailQuery.data.eventType} />
                </span>
                <span title={formatTimestamp(detailQuery.data.receivedAtUtc).title}>
                  {formatTimestamp(detailQuery.data.receivedAtUtc).text}
                </span>
                <span className="mono">{detailQuery.data.jsonSizeBytes} B</span>
                {detailQuery.data.correlationId && (
                  <span className="mono">corr {detailQuery.data.correlationId}</span>
                )}
              </div>
              <JsonViewer value={detailQuery.data.payload} />
            </>
          )}
        </Modal>
      )}
    </div>
  )
}
