import { useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import { Avatar, BarRow, Delta, EmojiTile, HeatGrid, Icon, cardStyle, fmt, hhmm, decimalHours, useProfile, WEEKDAYS } from '../ui'
import type { IconName } from '../ui'
import { communityApi } from '../api/communityApi'
import type { CommunityActivity, CommunityLeaderEntry, CommunityMetric, CommunityMetrics, CommunityRange } from '../api/communityApi'
import { statsApi } from '../api/statsApi'
import type { ChannelActivity, EmojiStat, HeatmapCell } from '../api/statsApi'

const mono = 'JetBrains Mono, monospace'
const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const labelStyle: CSSProperties = { fontSize: 11, fontWeight: 600, letterSpacing: '.12em', textTransform: 'uppercase', color: C.muted }
const noteStyle: CSSProperties = { margin: 0, padding: '8px 0', fontSize: 13, color: C.muted }
const cardPadding = 'clamp(16px, 4vw, 22px)'

// "All time" has no previous window and no per-day series, so the report offers week and month only.
type ReportRange = Exclude<CommunityRange, 'all'>

const RANGES: { key: ReportRange; button: string; heading: string; prev: string }[] = [
  { key: 'week', button: 'Week', heading: 'Last 7 days', prev: 'previous 7 days' },
  { key: 'month', button: 'Month', heading: 'Last 30 days', prev: 'previous 30 days' },
]

const count = (v: number): string => fmt(Math.round(v))

interface MetricSpec {
  key: keyof CommunityMetrics
  name: string
  icon: IconName
  color: string
  format: (v: number) => string
  /** False when the window value is not the sum of its days (distinct members). */
  additive: boolean
}

const METRICS: MetricSpec[] = [
  { key: 'messages', name: 'Messages', icon: 'message', color: C.blurple, format: count, additive: true },
  { key: 'memes', name: 'Media posts', icon: 'fire', color: C.fuchsia, format: count, additive: true },
  { key: 'reactionsReceived', name: 'Reactions', icon: 'reaction', color: C.amber, format: count, additive: true },
  { key: 'voiceMinutes', name: 'Time in VC', icon: 'voice', color: C.teal, format: decimalHours,additive: true },
  { key: 'onlineMinutes', name: 'Time online', icon: 'eye', color: C.green, format: decimalHours,additive: true },
  { key: 'activeMembers', name: 'Active members', icon: 'members', color: C.blue, format: count, additive: false },
]

// ---- Dates: the server buckets by Europe/Warsaw, so the day labels do too. ----

const DAY_MS = 86_400_000
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const guildDate = new Intl.DateTimeFormat('en-US', { timeZone: 'Europe/Warsaw', year: 'numeric', month: 'numeric', day: 'numeric' })

/** The `n` guild-local calendar days that end on the day of `at`, oldest first, as UTC-midnight dates. */
function windowDays(at: number, n: number): Date[] {
  const part = Object.fromEntries(guildDate.formatToParts(at).map((p) => [p.type, Number(p.value)]))
  const end = Date.UTC(part.year, part.month - 1, part.day)
  return Array.from({ length: n }, (_, i) => new Date(end - (n - 1 - i) * DAY_MS))
}

const dayMonth = (d: Date): string => `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`

function dateSpan(days: Date[]): string {
  if (days.length === 0) return '…'
  const last = days[days.length - 1]
  return `${dayMonth(days[0])} – ${dayMonth(last)} ${last.getUTCFullYear()}`
}

// ---- Shared card shell with the loading / error / empty states ----

type Load = 'loading' | 'error' | 'ready'

function Panel({
  title,
  titleSize = 17,
  icon,
  aside,
  load,
  empty,
  style,
  children,
}: {
  title: string
  titleSize?: number
  icon?: { name: IconName; color: string }
  aside?: string
  load: Load
  /** Shown instead of the children when the data loaded but holds nothing. */
  empty?: string | false
  style?: CSSProperties
  children: ReactNode
}) {
  return (
    <section className="animate-rise" style={{ ...cardStyle, padding: cardPadding, minWidth: 0, ...style }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'baseline', justifyContent: 'space-between', gap: 8, marginBottom: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
          {icon && <Icon name={icon.name} size={16} color={icon.color} />}
          <h2 style={{ ...disp, fontSize: titleSize, margin: 0, color: C.text }}>{title}</h2>
        </div>
        {aside && <span style={{ fontSize: 12.5, color: C.muted }}>{aside}</span>}
      </div>
      {load === 'loading' ? (
        <p style={noteStyle} aria-busy="true">
          Loading…
        </p>
      ) : load === 'error' ? (
        <p style={{ ...noteStyle, color: C.red }}>Could not load this data.</p>
      ) : empty ? (
        <p style={noteStyle}>{empty}</p>
      ) : (
        children
      )}
    </section>
  )
}

// ---- Header controls ----

function RangeToggle({ range, setRange }: { range: ReportRange; setRange: (r: ReportRange) => void }) {
  return (
    <div role="group" aria-label="Report window" style={{ display: 'inline-flex', gap: 2, background: C.bg, border: `1px solid ${C.border}`, borderRadius: 11, padding: 3 }}>
      {RANGES.map(({ key, button }) => {
        const on = range === key
        return (
          <button
            key={key}
            type="button"
            aria-pressed={on}
            onClick={() => setRange(key)}
            style={{
              minHeight: 44,
              padding: '0 20px',
              borderRadius: 8,
              border: 'none',
              cursor: 'pointer',
              fontSize: 14,
              fontWeight: 600,
              fontFamily: 'inherit',
              background: on ? C.blurple : 'transparent',
              color: on ? '#fff' : C.muted,
              transition: 'background .15s, color .15s',
            }}
          >
            {button}
          </button>
        )
      })}
    </div>
  )
}

// Waiting = images in the meme channels that no annotation run has taken yet.
// Zero stays quiet: only a backlog gets the accent colour.
function MemeIndexPill({ indexed, waiting }: { indexed: number; waiting: number }) {
  return (
    <div
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 8,
        padding: '8px 13px',
        background: C.bg,
        border: `1px solid ${C.border}`,
        borderRadius: 11,
        fontSize: 13,
        whiteSpace: 'nowrap',
      }}
    >
      <Icon name="fire" size={14} color={C.fuchsia} />
      <span style={{ color: C.muted }}>Meme index</span>
      <span style={{ fontFamily: mono, fontWeight: 600, color: C.text }}>{fmt(indexed)}</span>
      <span style={{ color: C.faint }}>·</span>
      <span style={{ fontFamily: mono, fontWeight: 600, color: waiting > 0 ? C.amber : C.faint }}>{fmt(waiting)} waiting</span>
    </div>
  )
}

// ---- Metric tiles ----

const TILE_BAR_MAX = 40

function MetricTile({ spec, metric, days, on, onPick }: { spec: MetricSpec; metric: CommunityMetric; days: Date[]; on: boolean; onPick: () => void }) {
  const max = Math.max(...metric.spark, 1)
  return (
    <button
      type="button"
      aria-pressed={on}
      onClick={onPick}
      className="animate-rise"
      style={{
        ...cardStyle,
        // The selected tile gets a thicker edge, so colour is not the only cue.
        borderColor: on ? spec.color : C.border,
        boxShadow: on ? `0 0 0 1px ${spec.color}, ${cardStyle.boxShadow}` : cardStyle.boxShadow,
        padding: 16,
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
        textAlign: 'left',
        color: C.text,
        cursor: 'pointer',
        fontFamily: 'inherit',
      }}
    >
      <span style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <span style={labelStyle}>{spec.name}</span>
        <Icon name={spec.icon} size={16} color={spec.color} />
      </span>
      <span style={{ ...disp, fontSize: 30, lineHeight: 1, fontVariantNumeric: 'tabular-nums' }}>{spec.format(metric.value)}</span>
      <span style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6, fontSize: 12, color: C.muted }}>
        {/* No percentage against a missing or empty previous window. */}
        {metric.prev !== null && metric.prev > 0 && <Delta cur={metric.value} prev={metric.prev} small />}
        <span>{metric.prev !== null ? `was ${spec.format(metric.prev)}` : 'no previous window'}</span>
      </span>
      <span aria-hidden="true" style={{ display: 'flex', alignItems: 'flex-end', gap: 2, height: TILE_BAR_MAX }}>
        {metric.spark.map((v, i) => (
          <span
            key={i}
            title={`${dayMonth(days[i])}: ${spec.format(v)}`}
            style={{
              flex: 1,
              minWidth: 0,
              borderRadius: '3px 3px 0 0',
              background: spec.color,
              height: Math.max(2, Math.round((v / max) * TILE_BAR_MAX)),
            }}
          />
        ))}
      </span>
    </button>
  )
}

// ---- Big per-day chart ----

const CHART_BAR_MAX = 195

function DayChart({ spec, metric, days }: { spec: MetricSpec; metric: CommunityMetric; days: Date[] }) {
  const n = metric.spark.length
  const peak = Math.max(...metric.spark, 0)
  // Distinct members do not add up over days, so their window total gives no daily average.
  const avg = spec.additive && metric.prev !== null && metric.prev > 0 ? metric.prev / n : null
  const top = Math.max(peak, avg ?? 0, 1)
  const peakIndex = metric.spark.indexOf(peak)
  const week = n <= 7
  const gap = week ? 'clamp(6px, 1.5vw, 14px)' : 'clamp(1px, 0.4vw, 3px)'

  if (peak === 0) return <p style={noteStyle}>No activity in this window.</p>

  return (
    <>
      <div role="list" style={{ position: 'relative', height: 230, display: 'flex', alignItems: 'flex-end', gap, borderBottom: `1px solid ${C.borderSoft}` }}>
        {metric.spark.map((v, i) => {
          const tip = `${dayMonth(days[i])}: ${spec.format(v)}`
          return (
            <div
              key={i}
              role="listitem"
              aria-label={tip}
              title={tip}
              style={{ flex: 1, minWidth: 0, height: '100%', display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', alignItems: 'center', gap: 4 }}
            >
              {i === peakIndex && <span style={{ fontFamily: mono, fontSize: 11.5, fontWeight: 600, color: C.text, whiteSpace: 'nowrap' }}>{spec.format(v)}</span>}
              <span
                style={{
                  width: '100%',
                  maxWidth: 64,
                  borderRadius: '4px 4px 0 0',
                  background: spec.color,
                  height: Math.max(2, Math.round((v / top) * CHART_BAR_MAX)),
                }}
              />
            </div>
          )
        })}
        {avg !== null && (
          <div style={{ position: 'absolute', left: 0, right: 0, bottom: Math.round((avg / top) * CHART_BAR_MAX), borderTop: `1.5px dashed ${C.muted}`, pointerEvents: 'none' }}>
            <span style={{ position: 'absolute', right: 0, bottom: 4, padding: '1px 6px', borderRadius: 5, background: C.card, fontSize: 11.5, color: C.muted, fontFamily: mono, whiteSpace: 'nowrap' }}>
              prev. period avg {spec.format(avg)} / day
            </span>
          </div>
        )}
      </div>
      <div aria-hidden="true" style={{ display: 'flex', gap, marginTop: 8 }}>
        {metric.spark.map((_, i) => {
          const day = days[i]
          // Week: every bar. Month: every 5th day, counted back from today so the last bar always has a label.
          const text = week ? `${WEEKDAYS[day.getUTCDay()]} ${day.getUTCDate()}` : (n - 1 - i) % 5 === 0 ? dayMonth(day) : ''
          return (
            <span
              key={i}
              style={{
                flex: 1,
                minWidth: 0,
                display: 'flex',
                justifyContent: 'center',
                textAlign: 'center',
                fontSize: 11.5,
                color: C.muted,
                // Week labels may break into "Mon" / "29" on a phone; month labels overflow their thin bar instead.
                whiteSpace: week ? 'normal' : 'nowrap',
              }}
            >
              {text}
            </span>
          )
        })}
      </div>
      {!spec.additive && <p style={{ ...noteStyle, padding: '10px 0 0', fontSize: 12 }}>No previous-period line: a member counts once per window, so the window total has no daily average.</p>}
    </>
  )
}

// ---- Ranked rows: leaderboards and top emotes ----

interface BoardRow {
  key: string
  name: string
  value: number
  display: string
  /** Avatar or emote in front of the name. */
  lead?: ReactNode
  /** Present for a member row: a click opens the profile panel. */
  profileId?: string
}

function memberRows(entries: CommunityLeaderEntry[] | undefined, format: (v: number) => string): BoardRow[] {
  return (entries ?? []).map((e) => {
    const name = e.username ?? e.userDiscordId
    return {
      key: e.userDiscordId,
      name,
      value: e.value,
      display: format(e.value),
      lead: <Avatar discordId={e.userDiscordId} avatarHash={e.avatarHash} name={name} size={28} />,
      profileId: e.userDiscordId,
    }
  })
}

function emoteRows(emotes: EmojiStat[]): BoardRow[] {
  return emotes.map((e) => ({
    key: `${e.emoteName}-${e.emoteDiscordId ?? 'u'}`,
    name: e.isCustom ? `:${e.emoteName}:` : e.emoteName,
    value: e.count,
    display: fmt(e.count),
    lead: (
      <span style={{ width: 28, flex: 'none', display: 'flex', justifyContent: 'center' }}>
        <EmojiTile name={e.emoteName} custom={e.isCustom} emoteId={e.emoteDiscordId} glyph={e.emoteName} size={24} />
      </span>
    ),
  }))
}

function gameRows(games: CommunityActivity[]): BoardRow[] {
  return games.map((g) => ({
    key: g.name,
    name: g.name,
    value: g.minutes,
    display: `${decimalHours(g.minutes)} · ${fmt(g.players)} ${g.players === 1 ? 'player' : 'players'}`,
  }))
}

const boardRowStyle: CSSProperties = { display: 'flex', alignItems: 'center', gap: 10, padding: '6px 8px', margin: '0 -8px', borderRadius: 10 }

function BoardRowBody({ row, rank, max, color }: { row: BoardRow; rank?: number; max: number; color: string }) {
  return (
    <>
      {rank !== undefined && (
        <span style={{ width: 14, flex: 'none', fontFamily: mono, fontSize: 12, fontWeight: 700, color: rank === 1 ? C.amber : C.muted }}>{rank}</span>
      )}
      {row.lead}
      <span style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 5 }}>
        <span style={{ display: 'flex', justifyContent: 'space-between', gap: 10 }}>
          <span style={{ fontSize: 13, fontWeight: 600, color: C.text, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{row.name}</span>
          <span style={{ fontFamily: mono, fontSize: 12, fontWeight: 600, color: C.muted, whiteSpace: 'nowrap' }}>{row.display}</span>
        </span>
        <BarRow pct={row.value / max} color={color} h={5} />
      </span>
    </>
  )
}

function Leaderboard({ title, icon, color, rows, load }: { title: string; icon: IconName; color: string; rows: BoardRow[]; load: Load }) {
  const { openProfile } = useProfile()
  const max = Math.max(...rows.map((r) => r.value), 1)
  return (
    <Panel title={title} titleSize={15} icon={{ name: icon, color }} load={load} empty={rows.length === 0 && 'No activity in this window.'}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
        {rows.map((row, i) => {
          const body = <BoardRowBody row={row} rank={i + 1} max={max} color={color} />
          const profileId = row.profileId
          return profileId ? (
            <button
              key={row.key}
              type="button"
              onClick={() => openProfile(profileId)}
              title={`Open the profile of ${row.name}`}
              style={{ ...boardRowStyle, background: 'transparent', border: 'none', cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left', transition: 'background .15s' }}
              onMouseEnter={(e) => (e.currentTarget.style.background = C.bg2)}
              onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
            >
              {body}
            </button>
          ) : (
            <div key={row.key} style={boardRowStyle}>
              {body}
            </div>
          )
        })}
      </div>
    </Panel>
  )
}

function EmoteList({ emotes }: { emotes: EmojiStat[] }) {
  const max = Math.max(...emotes.map((e) => e.count), 1)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {emoteRows(emotes).map((row) => (
        <div key={row.key} style={boardRowStyle}>
          <BoardRowBody row={row} max={max} color={C.amber} />
        </div>
      ))}
    </div>
  )
}

// ---- Channels ----

function ChannelBar({ value, max, color }: { value: number; max: number; color: string }) {
  return (
    <span style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
      {/* The bar tops out at 70% of the column so the number always fits next to it. */}
      <span style={{ height: 8, borderRadius: '0 4px 4px 0', background: color, width: `${Math.round((value / max) * 70)}%` }} />
      <span style={{ fontFamily: mono, fontSize: 12, color: C.muted }}>{fmt(value)}</span>
    </span>
  )
}

function ChannelTable({ channels }: { channels: ChannelActivity[] }) {
  const maxMessages = Math.max(...channels.map((c) => c.messageCount), 1)
  const maxReactions = Math.max(...channels.map((c) => c.reactionCount), 1)
  const row: CSSProperties = { display: 'grid', gridTemplateColumns: 'minmax(0, 1.1fr) minmax(0, 1fr) minmax(0, 1fr)', gap: 16, alignItems: 'center' }
  return (
    <div role="table" aria-label="Messages and reactions per channel" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div role="row" style={row}>
        {['Channel', 'Messages', 'Reactions'].map((h) => (
          <span key={h} role="columnheader" style={labelStyle}>
            {h}
          </span>
        ))}
      </div>
      {channels.map((c) => (
        <div key={c.channelDiscordId} role="row" style={row}>
          <span role="cell" title={`#${c.channelName}`} style={{ fontSize: 13, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            #{c.channelName}
          </span>
          <span role="cell">
            <ChannelBar value={c.messageCount} max={maxMessages} color={C.blurple} />
          </span>
          <span role="cell">
            <ChannelBar value={c.reactionCount} max={maxReactions} color={C.amber} />
          </span>
        </div>
      ))}
    </div>
  )
}

// ---- Heatmap ----

function Heatmap({ cells }: { cells: HeatmapCell[] }) {
  return (
    <>
      {/* 24 columns need ~460px: on a phone the grid scrolls inside its own box, not the page. */}
      <div style={{ overflowX: 'auto' }}>
        <div style={{ minWidth: 460 }}>
          <HeatGrid data={cells.map((c) => ({ d: c.dayOfWeek, h: c.hour, count: c.count }))} cell={18} gap={3} accent={C.blurple} weekStart={1} dayLabels="short" fluid hourAxis />
        </div>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginTop: 12, fontSize: 11.5, color: C.muted }}>
        fewer messages
        {[0.22, 0.45, 0.7, 1].map((o) => (
          <span key={o} style={{ width: 12, height: 12, borderRadius: 3, background: C.blurple, opacity: o }} />
        ))}
        more
      </div>
    </>
  )
}

// ---- Page ----

export default function Overview() {
  const [range, setRange] = useState<ReportRange>('week')
  const [metricKey, setMetricKey] = useState<MetricSpec['key']>('messages')

  // One payload feeds every card. The previous window stays on screen while the next one loads.
  const community = useQuery({
    queryKey: ['community', range],
    queryFn: async () => ({ stats: await communityApi.get(range), fetchedAt: Date.now() }),
    placeholderData: keepPreviousData,
  })
  // Same key as the Statistics page, so the two pages share one cached response.
  const overview = useQuery({ queryKey: ['stats', 'overview'], queryFn: statsApi.overview })

  const s = community.data?.stats
  const load: Load = s ? 'ready' : community.isError ? 'error' : 'loading'
  // Every metric carries the same number of days, so one date list serves all the charts.
  const days = community.data && s ? windowDays(community.data.fetchedAt, s.metrics.messages.spark.length) : []
  const report = RANGES.find((r) => r.key === range) ?? RANGES[0]
  const selected = METRICS.find((m) => m.key === metricKey) ?? METRICS[0]
  const { topEmotes = [], channels = [], topActivities = [], heatmap = [], heatmapDays = 30 } = s ?? {}
  const boards = s?.leaderboards
  // Dims the cards that still show the other window. A wrapper carries it: the rise-in animation pins a card's own opacity.
  const switching: CSSProperties = { opacity: community.isPlaceholderData ? 0.55 : 1, transition: 'opacity .15s' }

  return (
    <main style={{ maxWidth: 1280, margin: '0 auto', padding: '24px clamp(16px, 4vw, 32px) 48px', display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', justifyContent: 'space-between', gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <h1 style={{ ...disp, fontSize: 32, fontWeight: 800, margin: 0 }}>{report.heading}</h1>
          <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>
            {community.isPlaceholderData ? '…' : dateSpan(days)} · compared with the {report.prev} · all times CET
          </p>
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 12 }}>
          {overview.data && <MemeIndexPill indexed={overview.data.memeIndexedCount} waiting={overview.data.memeWaitingCount} />}
          <RangeToggle range={range} setRange={setRange} />
        </div>
      </div>

      {/* A failed refresh keeps the last loaded numbers on screen, so say they are stale. */}
      {community.isError && s && (
        <p role="alert" style={{ ...noteStyle, color: C.red }}>
          Could not refresh the stats. The numbers below are from the last successful load.
        </p>
      )}

      <div aria-busy={community.isPlaceholderData} style={{ ...switching, display: 'flex', flexDirection: 'column', gap: 18 }}>
        {s ? (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(150px, 100%), 1fr))', gap: 12 }}>
            {METRICS.map((m) => (
              <MetricTile key={m.key} spec={m} metric={s.metrics[m.key]} days={days} on={m.key === selected.key} onPick={() => setMetricKey(m.key)} />
            ))}
          </div>
        ) : (
          <Panel title="Window totals" load={load}>
            {null}
          </Panel>
        )}

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 18 }}>
          <Panel title={`${selected.name} per day`} aside="pick a tile above to change the metric" load={load} style={{ flex: '3 1 560px' }}>
            {s && <DayChart spec={selected} metric={s.metrics[selected.key]} days={days} />}
          </Panel>
          <Panel title="Top emotes" load={load} empty={topEmotes.length === 0 && 'No reactions in this window.'} style={{ flex: '1 1 280px' }}>
            <EmoteList emotes={topEmotes} />
          </Panel>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(300px, 100%), 1fr))', gap: 18 }}>
          <Leaderboard title="Top chatters" icon="crown" color={C.blurple} rows={memberRows(boards?.topChatters, count)} load={load} />
          <Leaderboard title="Meme lords" icon="fire" color={C.fuchsia} rows={memberRows(boards?.memeLords, count)} load={load} />
          <Leaderboard title="Reactions received" icon="reaction" color={C.amber} rows={memberRows(boards?.reactionsReceived, count)} load={load} />
          <Leaderboard title="Reactions given" icon="reaction" color={C.amber} rows={memberRows(boards?.reactionsGiven, count)} load={load} />
          <Leaderboard title="Time in VC" icon="voice" color={C.teal} rows={memberRows(boards?.voice, hhmm)} load={load} />
          <Leaderboard title="Top games" icon="play" color={C.green} rows={gameRows(topActivities)} load={load} />
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(420px, 100%), 1fr))', gap: 18 }}>
        <div aria-busy={community.isPlaceholderData} style={{ ...switching, display: 'flex', minWidth: 0 }}>
          <Panel title="Channels" load={load} empty={channels.length === 0 && 'No channel activity in this window.'} style={{ flex: 1 }}>
            <ChannelTable channels={channels} />
          </Panel>
        </div>
        {/* The heatmap window is fixed, so it does not dim when the toggle changes. */}
        <Panel
          title="When is the server alive?"
          aside={`messages, hour × weekday, last ${heatmapDays} days`}
          load={load}
          empty={!heatmap.some((c) => c.count > 0) && `No messages in the last ${heatmapDays} days.`}
        >
          <Heatmap cells={heatmap} />
        </Panel>
      </div>
    </main>
  )
}
