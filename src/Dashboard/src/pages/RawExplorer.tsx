import { useState } from 'react'
import type { CSSProperties } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import { Icon, fmt, compact } from '../ui'
import { rawEventsApi } from '../api/rawEventsApi'
import type { RawEventSummary } from '../api/rawEventsApi'
import { relativeTime } from '../utils/format'

const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const mono = 'JetBrains Mono, monospace'
const cardStyle: CSSProperties = {
  borderRadius: 16,
  border: `1px solid ${C.border}`,
  background: C.card,
  boxShadow: '0 1px 0 rgba(255,255,255,.04) inset, 0 14px 36px -22px rgba(0,0,0,.8)',
}

const GRID = '120px 1fr 200px 200px 90px 40px'
const PAGE_SIZE = 25
const KEYFRAMES =
  '@keyframes dRawFade{from{opacity:0}to{opacity:1}}@keyframes dRawSlide{from{transform:translateX(100%)}to{transform:translateX(0)}}'

/** Event-type prefix -> palette colour for chips. */
function typeColor(t: string): string {
  if (t.startsWith('Message')) return C.blurple
  if (t.startsWith('Reaction')) return C.fuchsia
  if (t.startsWith('Voice')) return C.teal
  if (t.startsWith('Presence')) return C.faint
  if (t.startsWith('Typing')) return C.amber
  if (t.startsWith('Guild') || t.startsWith('Member')) return C.green
  return C.blue
}

function fmtBytes(b: number): string {
  return b < 1024 ? `${b} B` : `${(b / 1024).toFixed(1)} KB`
}

/** Recursive syntax-coloured JSON renderer. */
function JsonView({ data, depth = 0, k }: { data: unknown; depth?: number; k?: string }) {
  const pad: CSSProperties = { paddingLeft: depth ? 16 : 0 }
  const keyEl = k !== undefined ? <span style={{ color: C.blue }}>&quot;{k}&quot;</span> : null
  const colon = k !== undefined ? <span style={{ color: C.faint }}>: </span> : null

  if (data === null || data === undefined) {
    return (
      <div style={pad}>
        {keyEl}
        {colon}
        <span style={{ color: C.faint }}>null</span>
      </div>
    )
  }
  if (typeof data === 'string') {
    return (
      <div style={pad}>
        {keyEl}
        {colon}
        <span style={{ color: C.green }}>&quot;{data}&quot;</span>
      </div>
    )
  }
  if (typeof data === 'number') {
    return (
      <div style={pad}>
        {keyEl}
        {colon}
        <span style={{ color: C.amber }}>{data}</span>
      </div>
    )
  }
  if (typeof data === 'boolean') {
    return (
      <div style={pad}>
        {keyEl}
        {colon}
        <span style={{ color: C.teal }}>{String(data)}</span>
      </div>
    )
  }
  if (Array.isArray(data)) {
    if (data.length === 0) {
      return (
        <div style={pad}>
          {keyEl}
          {colon}
          <span style={{ color: C.faint }}>[]</span>
        </div>
      )
    }
    return (
      <div style={pad}>
        {keyEl}
        {colon}
        <span style={{ color: C.faint }}>[</span>
        {data.map((v, i) => (
          <JsonView key={i} data={v} depth={depth + 1} />
        ))}
        <div style={{ paddingLeft: 16 }}>
          <span style={{ color: C.faint }}>]</span>
        </div>
      </div>
    )
  }
  const entries = Object.entries(data as Record<string, unknown>)
  return (
    <div style={pad}>
      {keyEl}
      {colon}
      <span style={{ color: C.faint }}>{'{'}</span>
      {entries.map(([kk, v]) => (
        <JsonView key={kk} data={v} depth={depth + 1} k={kk} />
      ))}
      <div style={{ paddingLeft: depth ? 16 : 0 }}>
        <span style={{ color: C.faint }}>{'}'}</span>
      </div>
    </div>
  )
}

function TypeChip({ name, failed }: { name: string; failed: boolean }) {
  const col = typeColor(name)
  return (
    <>
      <span style={{ fontSize: 12, fontWeight: 600, color: col, background: `${col}1c`, padding: '2px 9px', borderRadius: 6, fontFamily: mono, whiteSpace: 'nowrap' }}>
        {name}
      </span>
      {failed && <span style={{ fontSize: 10, fontWeight: 700, color: C.red, fontFamily: mono }}>⚠ FAILED</span>}
    </>
  )
}

function PayloadDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['raw-event', id],
    queryFn: () => rawEventsApi.detail(id),
  })

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 60,
        background: 'rgba(10,11,13,.55)',
        backdropFilter: 'blur(4px)',
        display: 'flex',
        justifyContent: 'flex-end',
        animation: 'dRawFade .2s ease',
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: 560,
          maxWidth: '100%',
          height: '100%',
          background: C.bg1,
          borderLeft: `1px solid ${C.border}`,
          overflowY: 'auto',
          animation: 'dRawSlide .3s cubic-bezier(.22,1,.36,1)',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'flex-start',
            justifyContent: 'space-between',
            gap: 12,
            padding: '18px 22px',
            borderBottom: `1px solid ${C.border}`,
            position: 'sticky',
            top: 0,
            background: C.bg1,
            zIndex: 2,
          }}
        >
          <div style={{ minWidth: 0 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              {data ? (
                <TypeChip name={data.eventType} failed={data.serializationFailed} />
              ) : (
                <span style={{ fontSize: 12, color: C.faint, fontFamily: mono }}>Raw event</span>
              )}
            </div>
            {data && (
              <div style={{ fontFamily: mono, fontSize: 11.5, color: C.faint, marginTop: 8 }}>
                {relativeTime(data.receivedAtUtc)} · {fmtBytes(data.jsonSizeBytes)}
              </div>
            )}
            {data?.correlationId && (
              <div style={{ fontFamily: mono, fontSize: 11.5, color: C.faint, marginTop: 4, wordBreak: 'break-all' }}>
                corr {data.correlationId}
              </div>
            )}
          </div>
          <button
            onClick={onClose}
            aria-label="Close payload"
            style={{ width: 30, height: 30, borderRadius: 15, border: `1px solid ${C.border}`, background: C.card, color: C.muted, fontSize: 18, cursor: 'pointer', lineHeight: 1, flexShrink: 0 }}
          >
            ×
          </button>
        </div>
        <div style={{ padding: 22 }}>
          <div style={{ fontSize: 11, fontWeight: 600, letterSpacing: '.1em', textTransform: 'uppercase', color: C.faint, marginBottom: 12 }}>
            Raw payload
          </div>
          {isLoading && <div style={{ color: C.muted, fontSize: 13, padding: '8px 0' }}>Loading payload…</div>}
          {isError && (
            <div style={{ color: C.red, fontSize: 13, padding: '8px 0' }}>
              Failed to load payload{error instanceof Error ? `: ${error.message}` : ''}
            </div>
          )}
          {data && (
            <div style={{ background: '#0e0f12', border: `1px solid ${C.border}`, borderRadius: 12, padding: 16, fontFamily: mono, fontSize: 12, lineHeight: 1.7, overflowX: 'auto' }}>
              <JsonView data={data.payload} />
            </div>
          )}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 14, fontSize: 12, color: C.faint }}>
            <Icon name="shield" size={13} color={C.faint} />
            Stored verbatim as received from the Discord gateway.
          </div>
        </div>
      </div>
      <style>{KEYFRAMES}</style>
    </div>
  )
}

function EventRow({ ev, onOpen }: { ev: RawEventSummary; onOpen: (id: string) => void }) {
  const base = ev.serializationFailed ? 'rgba(237,66,69,.05)' : 'transparent'
  return (
    <div
      onClick={() => onOpen(ev.id)}
      style={{
        display: 'grid',
        gridTemplateColumns: GRID,
        gap: 0,
        padding: '11px 18px',
        borderBottom: `1px solid ${C.border}`,
        cursor: 'pointer',
        alignItems: 'center',
        transition: 'background .12s',
        background: base,
      }}
      onMouseEnter={(e) => (e.currentTarget.style.background = C.bg2)}
      onMouseLeave={(e) => (e.currentTarget.style.background = base)}
    >
      <span style={{ fontFamily: mono, fontSize: 11.5, color: C.faint, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', paddingRight: 10 }}>
        {relativeTime(ev.receivedAtUtc)}
      </span>
      <span style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
        <TypeChip name={ev.eventType} failed={ev.serializationFailed} />
      </span>
      <span style={{ fontFamily: mono, fontSize: 11.5, color: ev.userDiscordId ? C.muted : C.faint, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: 12 }}>
        {ev.userDiscordId ?? '—'}
      </span>
      <span style={{ fontFamily: mono, fontSize: 11.5, color: ev.channelDiscordId ? C.muted : C.faint, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: 12 }}>
        {ev.channelDiscordId ?? '—'}
      </span>
      <span style={{ fontFamily: mono, fontSize: 11.5, color: C.text, textAlign: 'right' }}>{fmtBytes(ev.jsonSizeBytes)}</span>
      <span style={{ display: 'flex', justifyContent: 'flex-end', color: C.faint }}>
        <Icon name="chevron" size={15} />
      </span>
    </div>
  )
}

export default function RawExplorer() {
  const [page, setPage] = useState(1)
  const [eventType, setEventType] = useState<string>('')
  const [failedOnly, setFailedOnly] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const typesQuery = useQuery({ queryKey: ['raw-types'], queryFn: rawEventsApi.types })
  const listQuery = useQuery({
    queryKey: ['raw-events', page, eventType, failedOnly],
    queryFn: () => rawEventsApi.list({ page, pageSize: PAGE_SIZE, eventType: eventType || undefined, failedOnly }),
    placeholderData: keepPreviousData,
  })

  const types = typesQuery.data ?? []
  const topTypes = [...types].sort((a, b) => b.count - a.count).slice(0, 8)
  const totalEvents = types.reduce((s, t) => s + t.count, 0)

  const items = listQuery.data?.items ?? []
  const totalCount = listQuery.data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  function pickType(next: string) {
    setEventType((cur) => (cur === next ? '' : next))
    setPage(1)
  }
  function toggleFailed() {
    setFailedOnly((v) => !v)
    setPage(1)
  }

  return (
    <div style={{ padding: '24px 32px 40px', maxWidth: 1280, margin: '0 auto' }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', marginBottom: 18, gap: 16, flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ ...disp, fontSize: 30, fontWeight: 800, margin: 0 }}>Raw events</h1>
          <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>
            Every gateway event as stored JSON · debug lens · <span style={{ color: C.text }}>{fmt(totalEvents)}</span> total
          </p>
        </div>
        <button
          onClick={toggleFailed}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            padding: '9px 14px',
            borderRadius: 10,
            cursor: 'pointer',
            fontFamily: 'inherit',
            fontSize: 13,
            fontWeight: 600,
            border: `1px solid ${failedOnly ? C.red : C.border}`,
            background: failedOnly ? 'rgba(237,66,69,.14)' : C.card,
            color: failedOnly ? C.red : C.muted,
          }}
        >
          <span style={{ width: 8, height: 8, borderRadius: 4, background: failedOnly ? C.red : C.faint }} />
          failed only
        </button>
      </div>

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
        <button
          onClick={() => {
            setEventType('')
            setPage(1)
          }}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            padding: '7px 12px',
            borderRadius: 9,
            cursor: 'pointer',
            fontFamily: 'inherit',
            fontSize: 12.5,
            fontWeight: 600,
            border: `1px solid ${eventType === '' ? C.text : C.border}`,
            background: eventType === '' ? C.bg2 : 'transparent',
            color: eventType === '' ? C.text : C.muted,
          }}
        >
          All types <span style={{ fontFamily: mono, fontSize: 11, color: C.faint }}>{compact(totalEvents)}</span>
        </button>
        {topTypes.map((t) => {
          const on = eventType === t.eventType
          const col = typeColor(t.eventType)
          return (
            <button
              key={t.eventType}
              onClick={() => pickType(t.eventType)}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                padding: '7px 12px',
                borderRadius: 9,
                cursor: 'pointer',
                fontFamily: 'inherit',
                fontSize: 12.5,
                fontWeight: 600,
                border: `1px solid ${on ? col : C.border}`,
                background: on ? `${col}1c` : 'transparent',
                color: on ? C.text : C.muted,
              }}
            >
              <span style={{ width: 7, height: 7, borderRadius: 4, background: col }} />
              {t.eventType} <span style={{ fontFamily: mono, fontSize: 11, color: C.faint }}>{compact(t.count)}</span>
            </button>
          )
        })}
        {typesQuery.isLoading && <span style={{ fontSize: 12.5, color: C.faint, alignSelf: 'center' }}>Loading types…</span>}
      </div>

      <div style={{ ...cardStyle, padding: 0, overflow: 'hidden' }}>
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: GRID,
            gap: 0,
            padding: '12px 18px',
            borderBottom: `1px solid ${C.border}`,
            fontSize: 11,
            fontWeight: 600,
            letterSpacing: '.08em',
            textTransform: 'uppercase',
            color: C.faint,
          }}
        >
          <span>Received</span>
          <span>Event type</span>
          <span>User</span>
          <span>Channel</span>
          <span style={{ textAlign: 'right' }}>Size</span>
          <span />
        </div>

        {listQuery.isError ? (
          <div style={{ padding: '48px 20px', textAlign: 'center', color: C.red, fontSize: 14 }}>
            Failed to load raw events
            {listQuery.error instanceof Error ? `: ${listQuery.error.message}` : '.'}
          </div>
        ) : items.length === 0 ? (
          <div style={{ padding: '52px 20px', textAlign: 'center', color: C.faint }}>
            <Icon name="raw" size={26} color={C.faint} />
            <div style={{ fontSize: 14, marginTop: 10 }}>
              {listQuery.isLoading ? 'Loading events…' : 'No events match this filter.'}
            </div>
          </div>
        ) : (
          items.map((ev) => <EventRow key={ev.id} ev={ev} onOpen={setSelectedId} />)
        )}

        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '12px 18px', flexWrap: 'wrap' }}>
          <span style={{ fontSize: 12.5, color: C.muted }}>
            {totalCount > 0 ? (
              <>
                Showing <b style={{ color: C.text }}>{items.length}</b> of <b style={{ color: C.text }}>{fmt(totalCount)}</b> · click any row for the full payload
              </>
            ) : (
              'No events to show'
            )}
          </span>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              style={{
                padding: '6px 12px',
                borderRadius: 8,
                border: `1px solid ${C.border}`,
                background: C.card,
                color: page <= 1 ? C.faint : C.muted,
                fontSize: 12.5,
                fontWeight: 600,
                fontFamily: 'inherit',
                cursor: page <= 1 ? 'default' : 'pointer',
                opacity: page <= 1 ? 0.5 : 1,
              }}
            >
              Prev
            </button>
            <span style={{ fontFamily: mono, fontSize: 12, color: C.muted, whiteSpace: 'nowrap' }}>
              {page} / {totalPages}
            </span>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              style={{
                padding: '6px 12px',
                borderRadius: 8,
                border: `1px solid ${C.border}`,
                background: C.card,
                color: page >= totalPages ? C.faint : C.muted,
                fontSize: 12.5,
                fontWeight: 600,
                fontFamily: 'inherit',
                cursor: page >= totalPages ? 'default' : 'pointer',
                opacity: page >= totalPages ? 0.5 : 1,
              }}
            >
              Next
            </button>
          </div>
        </div>
      </div>

      {selectedId !== null && <PayloadDrawer id={selectedId} onClose={() => setSelectedId(null)} />}
    </div>
  )
}
