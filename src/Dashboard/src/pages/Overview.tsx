import { useState } from 'react'
import type { CSSProperties } from 'react'
import { useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import {
  Avatar,
  EmojiTile,
  AlbumArt,
  Sparkline,
  AreaChart,
  BarRow,
  HeatGrid,
  Delta,
  Icon,
  fmt,
  compact,
  hhmm,
  hours,
  useProfile,
} from '../ui'
import type { IconName } from '../ui/Icon'
import { communityApi } from '../api/communityApi'
import type { CommunityLeaderEntry, CommunityMetric, CommunityRange } from '../api/communityApi'
import { spotifyApi } from '../api/spotifyApi'
import { statsApi } from '../api/statsApi'
import { entitiesApi } from '../api/entitiesApi'
import { relativeTime, truncate } from '../utils/format'

const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const labelStyle: CSSProperties = { fontSize: 11, fontWeight: 600, letterSpacing: '.12em', textTransform: 'uppercase', color: C.faint }
const cardStyle: CSSProperties = {
  borderRadius: 16,
  border: `1px solid ${C.border}`,
  background: `linear-gradient(180deg, rgba(255,255,255,.03), rgba(255,255,255,0) 120px), ${C.card}`,
  boxShadow: '0 1px 0 rgba(255,255,255,.05) inset, 0 14px 36px -22px rgba(0,0,0,.8)',
}

const RANGES: [CommunityRange, string][] = [
  ['week', 'Week'],
  ['month', 'Month'],
  ['all', 'All time'],
]

function RangeToggle({ range, setRange }: { range: CommunityRange; setRange: (r: CommunityRange) => void }) {
  return (
    <div style={{ display: 'inline-flex', background: C.bg, border: `1px solid ${C.border}`, borderRadius: 11, padding: 3 }}>
      {RANGES.map(([k, lbl]) => {
        const on = range === k
        return (
          <button
            key={k}
            onClick={() => setRange(k)}
            style={{
              padding: '7px 16px',
              borderRadius: 8,
              border: 'none',
              cursor: 'pointer',
              fontSize: 13,
              fontWeight: 600,
              fontFamily: 'inherit',
              whiteSpace: 'nowrap',
              background: on ? C.blurple : 'transparent',
              color: on ? '#fff' : C.muted,
              transition: 'background .15s, color .15s',
            }}
          >
            {lbl}
          </button>
        )
      })}
    </div>
  )
}

interface TileSpec {
  icon: IconName
  name: string
  metric: CommunityMetric
  color: string
  format: (v: number) => string
}

function StatTile({ icon, name, metric, color, format, prevLabel }: TileSpec & { prevLabel: string }) {
  const [hover, setHover] = useState(false)
  const spark = metric.spark.map((c) => ({ count: c }))
  return (
    <div
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        ...cardStyle,
        padding: 18,
        transition: 'transform .2s, border-color .2s',
        transform: hover ? 'translateY(-2px)' : 'none',
        borderColor: hover ? C.borderSoft : C.border,
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <span style={{ ...labelStyle, whiteSpace: 'nowrap' }}>{name}</span>
        <div style={{ width: 30, height: 30, borderRadius: 9, background: `${color}1f`, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <Icon name={icon} size={16} color={color} />
        </div>
      </div>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', gap: 8 }}>
        <div style={{ ...disp, fontSize: 30, color: C.text, lineHeight: 1, whiteSpace: 'nowrap' }}>{format(metric.value)}</div>
        <Sparkline data={spark} w={78} h={30} color={color} />
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 12 }}>
        {metric.prev !== null ? (
          <Delta cur={metric.value} prev={metric.prev} />
        ) : (
          <span style={{ fontSize: 11.5, color: C.faint, fontFamily: 'JetBrains Mono, monospace' }}>—</span>
        )}
        <span style={{ fontSize: 12, color: C.muted }}>{prevLabel}</span>
      </div>
    </div>
  )
}

function Leaderboard({
  title,
  icon,
  color,
  rows,
  fmtVal,
}: {
  title: string
  icon: IconName
  color: string
  rows: CommunityLeaderEntry[]
  fmtVal: (v: number) => string
}) {
  const { openProfile } = useProfile()
  const max = Math.max(...rows.map((r) => r.value), 1)
  return (
    <div style={{ ...cardStyle, padding: 20 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 16 }}>
        <Icon name={icon} size={16} color={color} />
        <h3 style={{ ...disp, fontSize: 15, margin: 0, color: C.text }}>{title}</h3>
      </div>
      {rows.length === 0 && <div style={{ fontSize: 13, color: C.faint, padding: '8px 0' }}>No activity in this window.</div>}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
        {rows.map((r, i) => {
          const name = r.username ?? r.userDiscordId
          return (
            <button
              key={r.userDiscordId}
              onClick={() => openProfile(r.userDiscordId)}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 11,
                padding: '7px 8px',
                margin: '0 -8px',
                borderRadius: 10,
                background: 'transparent',
                border: 'none',
                cursor: 'pointer',
                fontFamily: 'inherit',
                textAlign: 'left',
                transition: 'background .15s',
              }}
              onMouseEnter={(e) => (e.currentTarget.style.background = C.bg2)}
              onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
            >
              <span style={{ width: 14, fontFamily: 'JetBrains Mono, monospace', fontSize: 12, color: i === 0 ? C.amber : C.faint, fontWeight: 700, flexShrink: 0 }}>
                {i + 1}
              </span>
              <Avatar discordId={r.userDiscordId} avatarHash={r.avatarHash} name={name} size={28} />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, marginBottom: 5 }}>
                  <span style={{ fontSize: 13, fontWeight: 600, color: C.text, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', flex: '1 1 auto', minWidth: 0 }}>
                    {name}
                  </span>
                  <span style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, color: C.muted, fontWeight: 600, flex: '0 0 auto' }}>{fmtVal(r.value)}</span>
                </div>
                <BarRow pct={r.value / max} color={color} h={5} />
              </div>
            </button>
          )
        })}
      </div>
    </div>
  )
}

function NowListening() {
  const { openProfile } = useProfile()
  const { data } = useQuery({ queryKey: ['spotify'], queryFn: spotifyApi.get })
  const listeners = data?.nowPlaying ?? []
  const lead = listeners[0]
  return (
    <div style={{ ...cardStyle, padding: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 13 }}>
        <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11, fontWeight: 700, letterSpacing: '.1em', textTransform: 'uppercase', color: C.green, whiteSpace: 'nowrap' }}>
          <Icon name="spotify" size={13} color={C.green} fill />
          Listening now
        </span>
        <span style={{ display: 'flex', alignItems: 'center' }}>
          {listeners.slice(0, 4).map((n, i) => (
            <button
              key={n.userDiscordId}
              onClick={() => openProfile(n.userDiscordId)}
              style={{ marginLeft: i ? -7 : 0, background: 'none', border: 'none', padding: 0, cursor: 'pointer' }}
            >
              <Avatar discordId={n.userDiscordId} avatarHash={n.avatarHash} name={n.username ?? n.userDiscordId} size={22} ring status={false} />
            </button>
          ))}
        </span>
      </div>
      {lead === undefined ? (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, color: C.faint, fontSize: 12.5, padding: '6px 2px' }}>
          <Icon name="music" size={16} color={C.faint} />
          Nobody&apos;s listening right now.
        </div>
      ) : (
        <div style={{ display: 'flex', gap: 11, alignItems: 'center' }}>
          <AlbumArt url={lead.albumArtUrl} size={42} radius={8} />
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontSize: 13.5, fontWeight: 700, color: C.text, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              {lead.track ?? 'On Spotify'}
            </div>
            <div style={{ fontSize: 12, color: C.muted, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              {[lead.artist, lead.username ?? undefined].filter(Boolean).join(' · ')}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function TopEmotes() {
  const { data } = useQuery({ queryKey: ['behavior', 'top-emojis'], queryFn: statsApi.topEmojis })
  const emojis = (data ?? []).slice(0, 7)
  return (
    <div style={{ ...cardStyle, padding: 20 }}>
      <h3 style={{ ...disp, fontSize: 15, margin: '0 0 14px' }}>Top emotes</h3>
      {emojis.length === 0 && <div style={{ fontSize: 13, color: C.faint }}>No reactions recorded yet.</div>}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 11 }}>
        {emojis.map((e) => (
          <div key={`${e.emoteName}-${e.emoteDiscordId ?? 'u'}`} style={{ display: 'flex', alignItems: 'center', gap: 11 }}>
            <EmojiTile name={e.emoteName} custom={e.isCustom} emoteId={e.emoteDiscordId} glyph={e.emoteName} size={26} />
            <span
              style={{
                fontSize: 12.5,
                color: C.muted,
                flex: 1,
                fontFamily: e.isCustom ? 'JetBrains Mono, monospace' : 'inherit',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
              }}
            >
              {e.isCustom ? `:${e.emoteName}:` : e.emoteName}
            </span>
            <span style={{ fontFamily: 'JetBrains Mono, monospace', fontSize: 12, color: C.text, fontWeight: 600 }}>{fmt(e.count)}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

function LiveFeed() {
  const { openProfile } = useProfile()
  const { data } = useQuery({ queryKey: ['overview', 'recent-messages'], queryFn: () => entitiesApi.messages({ pageSize: 11 }) })
  const messages = data?.items ?? []
  return (
    <div style={{ ...cardStyle, padding: 0, overflow: 'hidden' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '15px 18px', borderBottom: `1px solid ${C.border}` }}>
        <h3 style={{ ...disp, fontSize: 15, margin: 0 }}>Latest messages</h3>
        <span style={{ display: 'flex', alignItems: 'center', gap: 6, color: C.red, fontSize: 11, fontWeight: 700, fontFamily: 'JetBrains Mono, monospace' }}>
          <span style={{ width: 7, height: 7, borderRadius: 4, background: C.red, animation: 'dPulse 1.4s infinite' }} />
          LIVE
        </span>
      </div>
      <div style={{ padding: '6px 14px 12px' }}>
        {messages.length === 0 && <div style={{ fontSize: 13, color: C.faint, padding: '12px 4px' }}>No messages yet.</div>}
        {messages.map((m) => (
          <div key={m.id} style={{ display: 'flex', gap: 10, padding: '8px 4px' }}>
            <button onClick={() => openProfile(m.authorDiscordId)} style={{ background: 'none', border: 'none', padding: 0, cursor: 'pointer' }}>
              <Avatar discordId={m.authorDiscordId} avatarHash={m.authorAvatarHash} name={m.authorName} size={28} />
            </button>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <button
                  onClick={() => openProfile(m.authorDiscordId)}
                  style={{ background: 'none', border: 'none', padding: 0, cursor: 'pointer', fontSize: 13, fontWeight: 700, color: C.text, fontFamily: 'inherit' }}
                >
                  {m.authorName}
                </button>
                <span style={{ marginLeft: 'auto', fontFamily: 'JetBrains Mono, monospace', fontSize: 10.5, color: C.faint }}>{relativeTime(m.createdAtUtc)}</span>
              </div>
              <div style={{ fontSize: 12.5, color: C.muted, marginTop: 1 }}>
                in <b style={{ color: C.text }}>#{m.channelName}</b>
                <div style={{ color: m.isDeleted ? C.faint : C.text, marginTop: 2, textDecoration: m.isDeleted ? 'line-through' : 'none' }}>
                  {m.content ? (
                    truncate(m.content, 90)
                  ) : (
                    <span style={{ color: C.faint, fontStyle: 'italic' }}>{m.hasAttachments ? 'attachment' : m.hasEmbeds ? 'embed' : 'no text'}</span>
                  )}
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

export default function Overview() {
  const [range, setRange] = useState<CommunityRange>('week')
  const community = useQuery({ queryKey: ['community', range], queryFn: () => communityApi.get(range) })
  const overview = useQuery({ queryKey: ['overview'], queryFn: statsApi.overview })
  const heatmap = useQuery({ queryKey: ['behavior', 'heatmap'], queryFn: statsApi.heatmap })

  const d = community.data
  const messagesDaily = (overview.data?.messagesDaily ?? []).map((p) => ({ count: p.count }))
  const heatCells = (heatmap.data ?? []).map((c) => ({ d: c.dayOfWeek, h: c.hour, count: c.count }))

  const tiles: TileSpec[] = d
    ? [
        { icon: 'message', name: 'Messages', metric: d.metrics.messages, color: C.blurple, format: fmt },
        { icon: 'fire', name: 'Media posts', metric: d.metrics.memes, color: C.fuchsia, format: fmt },
        { icon: 'reaction', name: 'Reactions recv', metric: d.metrics.reactionsReceived, color: C.amber, format: fmt },
        { icon: 'voice', name: 'Time in VC', metric: d.metrics.voiceMinutes, color: C.teal, format: hours },
        { icon: 'eye', name: 'Time online', metric: d.metrics.onlineMinutes, color: C.green, format: hours },
        { icon: 'members', name: 'Active members', metric: d.metrics.activeMembers, color: C.blue, format: fmt },
      ]
    : []

  return (
    <div style={{ padding: '24px 32px 40px', maxWidth: 1280, margin: '0 auto' }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', marginBottom: 20, gap: 16, flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ ...disp, fontSize: 30, fontWeight: 800, margin: 0 }}>Community</h1>
          <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>
            How the server&apos;s doing · <span style={{ color: C.text }}>{d?.label ?? '…'}</span> · all times CET
          </p>
        </div>
        <RangeToggle range={range} setRange={setRange} />
      </div>

      {community.isError && <div style={{ ...cardStyle, padding: 20, color: C.red, marginBottom: 16 }}>Failed to load community stats.</div>}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 16, marginBottom: 16 }}>
        {d
          ? tiles.map((t) => <StatTile key={t.name} {...t} prevLabel={d.prevLabel} />)
          : Array.from({ length: 6 }, (_, i) => <div key={i} style={{ ...cardStyle, padding: 18, height: 118, opacity: 0.4 }} />)}
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 360px', gap: 22, alignItems: 'start' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16, minWidth: 0 }}>
          <div style={{ ...cardStyle, padding: 22 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 6, gap: 10 }}>
              <h3 style={{ ...disp, fontSize: 16, margin: 0 }}>Messages · last 30 days</h3>
              <span style={{ fontSize: 12.5, color: C.muted }}>per day</span>
            </div>
            {messagesDaily.length > 0 ? (
              <AreaChart data={messagesDaily} w={720} h={180} color={C.blurple} />
            ) : (
              <div style={{ height: 180, display: 'flex', alignItems: 'center', justifyContent: 'center', color: C.faint, fontSize: 13 }}>No data yet.</div>
            )}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
            <Leaderboard title="Top chatters" icon="crown" color={C.blurple} rows={d?.leaderboards.topChatters ?? []} fmtVal={fmt} />
            <Leaderboard title="Meme lords" icon="fire" color={C.fuchsia} rows={d?.leaderboards.memeLords ?? []} fmtVal={fmt} />
            <Leaderboard title="Most reactions received" icon="reaction" color={C.amber} rows={d?.leaderboards.reactionsReceived ?? []} fmtVal={fmt} />
            <Leaderboard title="Most time in VC" icon="voice" color={C.teal} rows={d?.leaderboards.voice ?? []} fmtVal={hhmm} />
          </div>

          <div style={{ ...cardStyle, padding: 20 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 16, gap: 12 }}>
              <h3 style={{ ...disp, fontSize: 16, margin: 0, whiteSpace: 'nowrap' }}>When is the server alive?</h3>
              <span style={{ fontSize: 12.5, color: C.muted, whiteSpace: 'nowrap' }}>messages by hour × weekday</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 28, flexWrap: 'wrap' }}>
              {heatCells.length > 0 ? (
                <HeatGrid data={heatCells} cell={17} gap={4} accent={C.blurple} />
              ) : (
                <div style={{ color: C.faint, fontSize: 13 }}>No data yet.</div>
              )}
              <div style={{ flex: '1 1 180px', minWidth: 170 }}>
                <span style={{ fontSize: 12, color: C.muted }}>Total events</span>
                <div style={{ ...disp, fontSize: 20, color: C.text, margin: '4px 0 14px', whiteSpace: 'nowrap' }}>
                  {overview.data ? compact(overview.data.totalEvents) : '—'} captured
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11.5, color: C.faint }}>
                  less
                  {[0.2, 0.45, 0.7, 0.95].map((o) => (
                    <span key={o} style={{ width: 12, height: 12, borderRadius: 3, background: C.blurple, opacity: o }} />
                  ))}
                  more
                </div>
              </div>
            </div>
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 16, minWidth: 0 }}>
          <NowListening />
          <TopEmotes />
          <LiveFeed />
        </div>
      </div>
    </div>
  )
}
