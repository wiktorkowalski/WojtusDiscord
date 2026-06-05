import { useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import { C } from '../theme'
import { Avatar, EmojiTile, Icon, fmt, agoStr, useProfile } from '../ui'
import {
  useNameMaps,
  humanizeEvent,
  EVENT_CATEGORIES,
  eventTypesFor,
} from '../ui/eventFeed'
import type { EventCategory, HumanEvent } from '../ui/eventFeed'
import { useTimeline } from '../hooks/useTimeline'
import type { TimelineEvent, TimelineFilters } from '../api/timelineApi'

const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const mono = 'JetBrains Mono, monospace'
const cardStyle: CSSProperties = {
  borderRadius: 16,
  border: `1px solid ${C.border}`,
  background: `linear-gradient(180deg, rgba(255,255,255,.03), rgba(255,255,255,0) 120px), ${C.card}`,
  boxShadow: '0 1px 0 rgba(255,255,255,.05) inset, 0 14px 36px -22px rgba(0,0,0,.8)',
}
const labelStyle: CSSProperties = { fontSize: 11, fontWeight: 600, letterSpacing: '.1em', textTransform: 'uppercase', color: C.faint }

// Presence is ~95% of all gateway traffic — noise. Default-select everything else.
const DEFAULT_CATS: Set<EventCategory> = new Set(
  EVENT_CATEGORIES.filter((c) => c.key !== 'presence').map((c) => c.key),
)

function sameSet(a: Set<EventCategory>, b: Set<EventCategory>): boolean {
  if (a.size !== b.size) return false
  for (const k of a) if (!b.has(k)) return false
  return true
}

// Fixed, ordered bucket list keyed off minutes-elapsed (newest first).
const BUCKETS: { key: string; label: string; max: number }[] = [
  { key: 'hour', label: 'Past hour', max: 60 },
  { key: 'today', label: 'Earlier today', max: 1440 },
  { key: 'yesterday', label: 'Yesterday', max: 2880 },
  { key: 'older', label: 'Older', max: Infinity },
]

function minutesAgo(iso: string): number {
  const t = Date.parse(iso)
  if (Number.isNaN(t)) return Infinity
  return Math.max(0, Math.floor((Date.now() - t) / 60_000))
}

function bucketIndex(min: number): number {
  for (let i = 0; i < BUCKETS.length; i++) if (min < BUCKETS[i].max) return i
  return BUCKETS.length - 1
}

function bytesLabel(bytes: number): string {
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${bytes} B`
}

interface Pair {
  ev: TimelineEvent
  h: HumanEvent
  min: number
}

function EventRow({ ev, h, min }: Pair) {
  const { openProfile } = useProfile()
  // HumanEvent drops the raw userDiscordId, so fall back to ev for unresolved users.
  const userId = h.user?.discordId ?? ev.userDiscordId ?? null
  const name = h.user?.name ?? ev.userDiscordId ?? 'Unknown'
  const dim = h.category === 'presence'
  return (
    <div
      style={{
        display: 'flex',
        gap: 13,
        padding: '13px 18px',
        borderBottom: `1px solid ${C.border}`,
        opacity: dim ? 0.5 : 1,
        transition: 'background .12s',
      }}
      onMouseEnter={(e) => (e.currentTarget.style.background = C.bg2)}
      onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
    >
      <div style={{ width: 52, flexShrink: 0, fontFamily: mono, fontSize: 11, color: C.faint, textAlign: 'right', paddingTop: 5 }}>
        {agoStr(min)} ago
      </div>
      <div style={{ width: 3, flexShrink: 0, borderRadius: 2, background: h.meta.color, opacity: 0.6 }} />
      {userId ? (
        <button onClick={() => openProfile(userId)} style={{ background: 'none', border: 'none', padding: 0, cursor: 'pointer', flexShrink: 0 }}>
          <Avatar discordId={h.user?.discordId} avatarHash={h.user?.avatarHash} name={name} size={34} isBot={h.user?.isBot} status={false} />
        </button>
      ) : (
        <Avatar name={name} size={34} status={false} />
      )}
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          {userId ? (
            <button
              onClick={() => openProfile(userId)}
              style={{ background: 'none', border: 'none', padding: 0, cursor: 'pointer', fontSize: 14, fontWeight: 700, color: C.text, fontFamily: 'inherit', maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {name}
            </button>
          ) : (
            <span style={{ fontSize: 14, fontWeight: 700, color: C.text }}>{name}</span>
          )}
          <span style={{ fontFamily: mono, fontSize: 10, color: h.meta.color, background: `${h.meta.color}1c`, padding: '2px 7px', borderRadius: 5, whiteSpace: 'nowrap' }}>
            {h.rawType}
          </span>
          {h.serializationFailed && (
            <span style={{ fontFamily: mono, fontSize: 10, color: C.red, background: `${C.red}1c`, padding: '2px 7px', borderRadius: 5 }}>FAILED</span>
          )}
        </div>
        <div style={{ marginTop: 2, fontSize: 13.5, color: C.muted, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
          <span>{h.verb}</span>
          {h.emote && <EmojiTile name={h.emote.name} custom={h.emote.custom} emoteId={h.emote.id} glyph={h.emote.name} size={18} />}
          {h.channelName && (
            <span>
              in <b style={{ color: C.text }}>#{h.channelName}</b>
            </span>
          )}
        </div>
      </div>
      <div style={{ fontFamily: mono, fontSize: 10.5, color: C.faint, flexShrink: 0, paddingTop: 5, whiteSpace: 'nowrap' }}>
        {bytesLabel(h.jsonSizeBytes)}
      </div>
    </div>
  )
}

export default function Timeline() {
  const [cats, setCats] = useState<Set<EventCategory>>(() => new Set(DEFAULT_CATS))
  const [userId, setUserId] = useState<string | undefined>(undefined)
  const [channelId, setChannelId] = useState<string | undefined>(undefined)

  const maps = useNameMaps()

  const filters: TimelineFilters = useMemo(
    () => ({ eventType: eventTypesFor(cats), userId, channelId }),
    [cats, userId, channelId],
  )
  const timeline = useTimeline(filters)
  const { data, isLoading, hasNextPage, isFetchingNextPage, fetchNextPage } = timeline

  // Empty category selection would send eventType=undefined => the API returns ALL
  // events (incl. presence) — the inverse of intent. Treat zero selection as "nothing".
  const noCats = cats.size === 0

  const pairs = useMemo<Pair[]>(() => {
    if (noCats) return []
    const events = data?.pages.flatMap((p) => p.events) ?? []
    return events.map((ev) => ({ ev, h: humanizeEvent(ev, maps.data), min: minutesAgo(ev.receivedAtUtc) }))
  }, [data, maps.data, noCats])

  // Group into the fixed ordered bucket list (stable across page appends).
  const groups = useMemo(() => {
    const byBucket: Pair[][] = BUCKETS.map(() => [])
    for (const p of pairs) byBucket[bucketIndex(p.min)].push(p)
    return BUCKETS.map((b, i) => ({ ...b, items: byBucket[i] })).filter((g) => g.items.length > 0)
  }, [pairs])

  const toggleCat = (k: EventCategory) =>
    setCats((prev) => {
      const n = new Set(prev)
      if (n.has(k)) n.delete(k)
      else n.add(k)
      return n
    })

  const humanUsers = useMemo(() => {
    const all = maps.data ? Array.from(maps.data.users.values()) : []
    return all.filter((u) => !u.isBot).slice(0, 8)
  }, [maps.data])

  const channelOptions = useMemo(() => {
    const all = maps.data ? Array.from(maps.data.channels.entries()) : []
    return all.map(([id, nm]) => ({ id, name: nm }))
  }, [maps.data])

  const filtersActive = !sameSet(cats, DEFAULT_CATS) || userId !== undefined || channelId !== undefined
  const clearFilters = () => {
    setCats(new Set(DEFAULT_CATS))
    setUserId(undefined)
    setChannelId(undefined)
  }

  return (
    <div style={{ padding: '24px 32px 40px', maxWidth: 1100, margin: '0 auto' }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', marginBottom: 6, gap: 16, flexWrap: 'wrap' }}>
        <div style={{ minWidth: 0 }}>
          <h1 style={{ ...disp, fontSize: 30, fontWeight: 800, margin: 0 }}>Timeline</h1>
          <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>The raw gateway-event lens · newest first · all times CET</p>
        </div>
        <span style={{ display: 'flex', alignItems: 'center', gap: 7, color: C.red, fontSize: 12, fontWeight: 700, fontFamily: mono }}>
          <span style={{ width: 8, height: 8, borderRadius: 4, background: C.red, animation: 'dPulse 1.4s infinite' }} />
          LIVE
        </span>
      </div>
      <p style={{ color: C.faint, fontSize: 12.5, margin: '4px 0 18px' }}>
        Message <i>previews</i> live on the Community feed — this is every gateway event, humanized.
      </p>

      {/* filter bar */}
      <div style={{ ...cardStyle, padding: 16, marginBottom: 16, display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <span style={{ ...labelStyle, marginRight: 4 }}>Type</span>
          {EVENT_CATEGORIES.map((c) => {
            const on = cats.has(c.key)
            return (
              <button
                key={c.key}
                onClick={() => toggleCat(c.key)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 7,
                  padding: '6px 12px',
                  borderRadius: 20,
                  cursor: 'pointer',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 600,
                  border: `1px solid ${on ? c.color : C.border}`,
                  background: on ? `${c.color}1c` : 'transparent',
                  color: on ? C.text : C.muted,
                  transition: 'all .15s',
                }}
              >
                <Icon name={c.icon} size={13} color={on ? c.color : C.faint} />
                {c.label}
              </button>
            )
          })}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <span style={{ ...labelStyle, marginRight: 4 }}>Person</span>
          <button
            onClick={() => setUserId(undefined)}
            style={{
              padding: '6px 12px',
              borderRadius: 20,
              cursor: 'pointer',
              fontFamily: 'inherit',
              fontSize: 13,
              fontWeight: 600,
              border: `1px solid ${userId === undefined ? C.blurple : C.border}`,
              background: userId === undefined ? `${C.blurple}1c` : 'transparent',
              color: userId === undefined ? C.text : C.muted,
            }}
          >
            Everyone
          </button>
          {humanUsers.map((u) => {
            const on = userId === u.discordId
            return (
              <button
                key={u.discordId}
                onClick={() => setUserId(on ? undefined : u.discordId)}
                title={u.name}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 7,
                  padding: '4px 12px 4px 4px',
                  borderRadius: 20,
                  cursor: 'pointer',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 600,
                  border: `1px solid ${on ? C.blurple : C.border}`,
                  background: on ? `${C.blurple}1c` : 'transparent',
                  color: on ? C.text : C.muted,
                  maxWidth: 180,
                }}
              >
                <Avatar discordId={u.discordId} avatarHash={u.avatarHash} name={u.name} size={22} status={false} />
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{u.name}</span>
              </button>
            )
          })}
          <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 8 }}>
            <Icon name="hash" size={14} color={C.faint} />
            <select
              value={channelId ?? 'all'}
              onChange={(e) => setChannelId(e.target.value === 'all' ? undefined : e.target.value)}
              style={{
                background: C.bg,
                border: `1px solid ${C.border}`,
                color: C.text,
                borderRadius: 9,
                padding: '7px 12px',
                fontFamily: 'inherit',
                fontSize: 13,
                cursor: 'pointer',
                maxWidth: 220,
              }}
            >
              <option value="all">All channels</option>
              {channelOptions.map((c) => (
                <option key={c.id} value={c.id}>
                  #{c.name}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* count + clear */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12, fontSize: 13, color: C.muted }}>
        <span style={{ fontFamily: mono, color: C.text, fontWeight: 600 }}>
          {fmt(pairs.length)}
          {hasNextPage ? '+' : ''}
        </span>{' '}
        events match
        {filtersActive && (
          <button
            onClick={clearFilters}
            style={{ marginLeft: 6, background: 'none', border: 'none', color: C.blurple, cursor: 'pointer', fontFamily: 'inherit', fontSize: 13, fontWeight: 600 }}
          >
            clear filters
          </button>
        )}
      </div>

      {/* feed */}
      <div style={{ ...cardStyle, padding: 0, overflow: 'hidden' }}>
        {isLoading ? (
          <div style={{ padding: '60px 20px', textAlign: 'center', color: C.faint, fontSize: 14 }}>Loading events…</div>
        ) : groups.length === 0 ? (
          <div style={{ padding: '56px 20px', textAlign: 'center', color: C.faint }}>
            <Icon name="timeline" size={26} color={C.faint} />
            <div style={{ marginTop: 10, fontSize: 14 }}>
              {noCats ? 'Pick at least one event type.' : 'No events match these filters.'}
            </div>
          </div>
        ) : (
          groups.map((g) => (
            <div key={g.key}>
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  padding: '8px 18px',
                  background: C.bg2,
                  borderBottom: `1px solid ${C.border}`,
                  ...labelStyle,
                }}
              >
                <span>{g.label}</span>
                <span style={{ fontFamily: mono, color: C.muted }}>{g.items.length}</span>
              </div>
              {g.items.map((p) => (
                <EventRow key={p.ev.id} ev={p.ev} h={p.h} min={p.min} />
              ))}
            </div>
          ))
        )}
      </div>

      {hasNextPage && !noCats && (
        <div style={{ display: 'flex', justifyContent: 'center', marginTop: 16 }}>
          <button
            onClick={() => fetchNextPage()}
            disabled={isFetchingNextPage}
            style={{
              padding: '9px 20px',
              borderRadius: 11,
              border: `1px solid ${C.border}`,
              background: C.bg2,
              color: C.text,
              cursor: isFetchingNextPage ? 'default' : 'pointer',
              fontFamily: 'inherit',
              fontSize: 13,
              fontWeight: 600,
              opacity: isFetchingNextPage ? 0.6 : 1,
              display: 'flex',
              alignItems: 'center',
              gap: 8,
            }}
          >
            <Icon name="chevdown" size={14} color={C.muted} />
            {isFetchingNextPage ? 'Loading…' : 'Load more'}
          </button>
        </div>
      )}
    </div>
  )
}
