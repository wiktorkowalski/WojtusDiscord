import { useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import {
  Avatar,
  AreaChart,
  BarRow,
  HeatGrid,
  Donut,
  Icon,
  fmt,
  compact,
  hhmm,
  useProfile,
} from '../ui'
import type { IconName } from '../ui/Icon'
import { statsApi } from '../api/statsApi'
import type { UserStat, VoiceStat, ActivityStat } from '../api/statsApi'

const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const mono = 'JetBrains Mono, monospace'
const cardStyle: CSSProperties = {
  borderRadius: 16,
  border: `1px solid ${C.border}`,
  background: `linear-gradient(180deg, rgba(255,255,255,.03), rgba(255,255,255,0) 120px), ${C.card}`,
  boxShadow: '0 1px 0 rgba(255,255,255,.05) inset, 0 14px 36px -22px rgba(0,0,0,.8)',
}

const TABS = ['Volume & trends', 'People', 'Places', 'Behavior'] as const
type Tab = (typeof TABS)[number]

const TYPE_COLORS = [C.faint, C.blurple, C.fuchsia, C.teal, C.amber, C.green, C.blue]
const DOW_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

// Running cumulative total of per-day counts (module scope so the accumulator isn't a
// render-captured mutable — keeps the render pure).
function runningTotal(rows: { count: number }[]): { count: number }[] {
  let total = 0
  return rows.map((p) => {
    total += p.count
    return { count: total }
  })
}

// ---------------------------------------------------------------- shared bits

function Panel({ title, sub, children, style }: { title: ReactNode; sub?: ReactNode; children: ReactNode; style?: CSSProperties }) {
  return (
    <div style={{ ...cardStyle, padding: 22, ...style }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 18, gap: 12 }}>
        <h3 style={{ ...disp, fontSize: 16, margin: 0, color: C.text }}>{title}</h3>
        {sub != null && <span style={{ fontSize: 12.5, color: C.muted, whiteSpace: 'nowrap' }}>{sub}</span>}
      </div>
      {children}
    </div>
  )
}

function PanelTitle({ icon, color, label }: { icon: IconName; color: string; label: string }) {
  return (
    <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <Icon name={icon} size={16} color={color} />
      {label}
    </span>
  )
}

function EmptyState({ icon, line }: { icon: IconName; line: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, color: C.faint, fontSize: 13, padding: '14px 2px' }}>
      <Icon name={icon} size={16} color={C.faint} />
      {line}
    </div>
  )
}

function ChartSkeleton({ h }: { h: number }) {
  return <div style={{ height: h, borderRadius: 12, background: C.bg2, opacity: 0.4 }} />
}

// vertical bar chart (hour of day / weekday)
interface VBar {
  label: string
  count: number
}
function VBars({ data, color, labelEvery = 6, h = 150 }: { data: VBar[]; color: string; labelEvery?: number; h?: number }) {
  const max = Math.max(...data.map((d) => d.count), 1)
  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', gap: 4, height: h }}>
      {data.map((d, i) => {
        const peak = d.count === max
        return (
          <div key={i} style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, justifyContent: 'flex-end', height: '100%' }}>
            <div
              title={`${d.label}: ${d.count}`}
              style={{
                width: '100%',
                maxWidth: 26,
                height: `${(d.count / max) * (h - 22)}px`,
                minHeight: 3,
                background: peak ? color : `${color}88`,
                borderRadius: 4,
                transition: 'height .5s cubic-bezier(.22,1,.36,1)',
              }}
            />
            {i % labelEvery === 0 && <span style={{ fontFamily: mono, fontSize: 9.5, color: C.faint }}>{d.label}</span>}
          </div>
        )
      })}
    </div>
  )
}

// horizontal ranked bars
interface HBar {
  label: string
  value: number
  display?: string
  icon?: ReactNode
}
function HBars({ rows, color }: { rows: HBar[]; color: string | ((i: number) => string) }) {
  const max = Math.max(...rows.map((r) => r.value), 1)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {rows.map((r, i) => (
        <div key={`${r.label}-${i}`} style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <span
            style={{
              fontSize: 13,
              color: C.muted,
              width: 150,
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              flexShrink: 0,
              display: 'flex',
              alignItems: 'center',
              gap: 7,
            }}
          >
            {r.icon}
            {r.label}
          </span>
          <div style={{ flex: 1 }}>
            <BarRow pct={r.value / max} color={typeof color === 'function' ? color(i) : color} h={9} />
          </div>
          <span style={{ fontFamily: mono, fontSize: 12.5, color: C.text, minWidth: 48, textAlign: 'right', flexShrink: 0, fontWeight: 600, whiteSpace: 'nowrap', paddingLeft: 6 }}>
            {r.display ?? compact(r.value)}
          </span>
        </div>
      ))}
    </div>
  )
}

interface PeopleRowEntry {
  username: string | null
  userDiscordId: string
  avatarHash?: string | null
}
function PeopleRow({ entry, value, max, color, display }: { entry: PeopleRowEntry; value: number; max: number; color: string; display: string }) {
  const { openProfile } = useProfile()
  const name = entry.username ?? entry.userDiscordId
  return (
    <button
      onClick={() => openProfile(entry.userDiscordId)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 13,
        padding: '9px 10px',
        margin: '0 -10px',
        borderRadius: 11,
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
      <Avatar discordId={entry.userDiscordId} avatarHash={entry.avatarHash} name={name} size={34} />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, marginBottom: 6 }}>
          <span style={{ fontSize: 14, fontWeight: 600, color: C.text, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', flex: '1 1 auto', minWidth: 0 }}>{name}</span>
          <span style={{ fontFamily: mono, fontSize: 12.5, color: C.muted, fontWeight: 600, flex: '0 0 auto', whiteSpace: 'nowrap' }}>{display}</span>
        </div>
        <BarRow pct={value / max} color={color} h={6} />
      </div>
    </button>
  )
}

function StatBadge({ value, label, color }: { value: string; label: string; color?: string }) {
  return (
    <div style={{ ...cardStyle, padding: '16px 18px' }}>
      <div style={{ ...disp, fontSize: 26, color: color ?? C.text }}>{value}</div>
      <div style={{ fontSize: 12.5, color: C.muted, marginTop: 4 }}>{label}</div>
    </div>
  )
}

// ---------------------------------------------------------------- Volume tab

function VolumeTab() {
  const overview = useQuery({ queryKey: ['stats', 'overview'], queryFn: statsApi.overview })
  const daily = useQuery({ queryKey: ['stats', 'volume-daily'], queryFn: statsApi.volumeDaily })
  const byType = useQuery({ queryKey: ['stats', 'volume-by-type'], queryFn: statsApi.volumeByType })
  const hourly = useQuery({ queryKey: ['stats', 'volume-hourly'], queryFn: statsApi.volumeHourly })

  const o = overview.data
  const dailyRows = daily.data ?? []
  const dailyChart = dailyRows.map((p) => ({ count: p.count }))
  const cumulativeChart = runningTotal(dailyRows)
  const typeRows: HBar[] = (byType.data ?? []).slice(0, 7).map((e) => ({ label: e.eventType, value: e.count }))
  const hourRows: VBar[] = (hourly.data ?? []).map((h) => ({ label: String(h.hour), count: h.count }))

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 16 }}>
        {o ? (
          <>
            <StatBadge value={compact(o.totalEvents)} label="events captured" color={C.blurple} />
            <StatBadge value={fmt(o.totalMessages)} label="messages" color={C.teal} />
            <StatBadge value={fmt(o.totalReactions)} label="reactions" color={C.fuchsia} />
            <StatBadge value={`${compact(Math.round(o.voiceMinutes / 60))}h`} label="voice time" color={C.amber} />
          </>
        ) : (
          Array.from({ length: 4 }, (_, i) => <div key={i} style={{ ...cardStyle, padding: '16px 18px', height: 70, opacity: 0.4 }} />)
        )}
      </div>

      <Panel title="Events per day" sub="last 30 days">
        {daily.isLoading ? (
          <ChartSkeleton h={230} />
        ) : dailyChart.length > 0 ? (
          <>
            <AreaChart data={dailyChart} w={1080} h={230} color={C.blurple} />
            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 6, fontFamily: mono, fontSize: 10.5, color: C.faint }}>
              <span>{dailyRows[0].day}</span>
              <span>{dailyRows[dailyRows.length - 1].day}</span>
            </div>
          </>
        ) : (
          <EmptyState icon="timeline" line="No daily volume yet." />
        )}
      </Panel>

      <Panel title="Cumulative growth" sub="total messages over the window">
        {daily.isLoading ? (
          <ChartSkeleton h={180} />
        ) : cumulativeChart.length > 0 ? (
          <>
            <AreaChart data={cumulativeChart} w={1080} h={180} color={C.green} />
            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 6, fontFamily: mono, fontSize: 10.5, color: C.faint }}>
              <span>{dailyRows[0].day}</span>
              <span>{fmt(cumulativeChart[cumulativeChart.length - 1].count)} total</span>
            </div>
          </>
        ) : (
          <EmptyState icon="timeline" line="No daily volume yet." />
        )}
      </Panel>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <Panel title="By event type">
          {byType.isLoading ? (
            <ChartSkeleton h={150} />
          ) : typeRows.length > 0 ? (
            <HBars rows={typeRows} color={(i) => TYPE_COLORS[i % TYPE_COLORS.length]} />
          ) : (
            <EmptyState icon="stats" line="No events recorded yet." />
          )}
        </Panel>
        <Panel title="By hour of day" sub="CET/CEST">
          {hourly.isLoading ? (
            <ChartSkeleton h={200} />
          ) : hourRows.length > 0 ? (
            <VBars data={hourRows} color={C.blurple} labelEvery={3} h={200} />
          ) : (
            <EmptyState icon="clock" line="No hourly activity yet." />
          )}
        </Panel>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------- People tab

function PeopleBoard({
  title,
  icon,
  color,
  rows,
  isLoading,
  fmtVal,
  valueOf,
}: {
  title: string
  icon: IconName
  color: string
  rows: PeopleRowEntry[]
  isLoading: boolean
  fmtVal: (v: number) => string
  valueOf: (r: PeopleRowEntry) => number
}) {
  const max = Math.max(...rows.map(valueOf), 1)
  return (
    <Panel title={<PanelTitle icon={icon} color={color} label={title} />}>
      {isLoading ? (
        <div style={{ height: 180, borderRadius: 12, background: C.bg2, opacity: 0.4 }} />
      ) : rows.length === 0 ? (
        <EmptyState icon={icon} line="No data in this window." />
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {rows.map((r) => {
            const v = valueOf(r)
            return <PeopleRow key={r.userDiscordId} entry={r} value={v} max={max} color={color} display={fmtVal(v)} />
          })}
        </div>
      )}
    </Panel>
  )
}

function PeopleTab() {
  const topMessages = useQuery({ queryKey: ['stats', 'top-messages'], queryFn: statsApi.topMessages })
  const reactGiven = useQuery({ queryKey: ['stats', 'top-reactions-given'], queryFn: statsApi.topReactionsGiven })
  const reactRecv = useQuery({ queryKey: ['stats', 'top-reactions-received'], queryFn: statsApi.topReactionsReceived })
  const voice = useQuery({ queryKey: ['stats', 'voice-leaderboard'], queryFn: statsApi.voiceLeaderboard })

  const userValue = (r: PeopleRowEntry) => (r as UserStat).count
  const voiceValue = (r: PeopleRowEntry) => (r as VoiceStat).minutes

  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
      <PeopleBoard
        title="Top chatters"
        icon="crown"
        color={C.blurple}
        rows={topMessages.data ?? []}
        isLoading={topMessages.isLoading}
        fmtVal={(v) => `${fmt(v)} msgs`}
        valueOf={userValue}
      />
      <PeopleBoard
        title="Most reactions given"
        icon="fire"
        color={C.fuchsia}
        rows={reactGiven.data ?? []}
        isLoading={reactGiven.isLoading}
        fmtVal={(v) => `${fmt(v)} given`}
        valueOf={userValue}
      />
      <PeopleBoard
        title="Most reactions received"
        icon="reaction"
        color={C.amber}
        rows={reactRecv.data ?? []}
        isLoading={reactRecv.isLoading}
        fmtVal={(v) => fmt(v)}
        valueOf={userValue}
      />
      <PeopleBoard
        title="Most time in voice"
        icon="voice"
        color={C.teal}
        rows={voice.data ?? []}
        isLoading={voice.isLoading}
        fmtVal={(v) => hhmm(v)}
        valueOf={voiceValue}
      />
    </div>
  )
}

// ---------------------------------------------------------------- Places tab

function PlacesTab() {
  const channels = useQuery({ queryKey: ['stats', 'channel-activity'], queryFn: statsApi.channelActivity })
  const rows = channels.data ?? []

  const byMessages: HBar[] = [...rows]
    .sort((a, b) => b.messageCount - a.messageCount)
    .map((c) => ({ label: c.channelName, value: c.messageCount, icon: <Icon name="hash" size={13} color={C.blurple} /> }))
  const byReactions: HBar[] = [...rows]
    .filter((c) => c.reactionCount > 0)
    .sort((a, b) => b.reactionCount - a.reactionCount)
    .map((c) => ({ label: c.channelName, value: c.reactionCount, icon: <Icon name="hash" size={13} color={C.fuchsia} /> }))

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <Panel title={<PanelTitle icon="hash" color={C.blurple} label="Channels by messages" />}>
        {channels.isLoading ? (
          <ChartSkeleton h={180} />
        ) : byMessages.length > 0 ? (
          <HBars rows={byMessages} color={C.blurple} />
        ) : (
          <EmptyState icon="hash" line="No channel activity yet." />
        )}
      </Panel>
      <Panel title={<PanelTitle icon="reaction" color={C.fuchsia} label="Channels by reactions" />}>
        {channels.isLoading ? (
          <ChartSkeleton h={150} />
        ) : byReactions.length > 0 ? (
          <HBars rows={byReactions} color={C.fuchsia} />
        ) : (
          <EmptyState icon="reaction" line="No reactions recorded in any channel yet." />
        )}
      </Panel>
    </div>
  )
}

// ---------------------------------------------------------------- Behavior tab

const NOISE_RE = /^Playing \d+\/\d+$/
function isNoise(name: string): boolean {
  return name === 'Custom Status' || NOISE_RE.test(name)
}

function TopActivities({ rows }: { rows: ActivityStat[] }) {
  if (rows.length === 0) return <EmptyState icon="activity" line="No presence activities recorded yet." />
  const max = Math.max(...rows.map((a) => a.count), 1)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 11 }}>
      {rows.map((a) => {
        const dirty = isNoise(a.name)
        return (
          <div key={a.name}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 5 }}>
              <span style={{ fontSize: 13, color: dirty ? C.faint : C.text, display: 'flex', alignItems: 'center', gap: 7, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', minWidth: 0 }}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{a.name}</span>
                {dirty && <span style={{ fontSize: 10, color: C.amber, background: 'rgba(250,166,26,.14)', padding: '1px 6px', borderRadius: 5, flexShrink: 0 }}>noise</span>}
              </span>
              <span style={{ fontFamily: mono, fontSize: 12, color: C.muted, flexShrink: 0 }}>{fmt(a.count)}</span>
            </div>
            <BarRow pct={a.count / max} color={dirty ? C.faint : C.green} h={5} />
          </div>
        )
      })}
    </div>
  )
}

function BehaviorTab() {
  const overview = useQuery({ queryKey: ['stats', 'overview'], queryFn: statsApi.overview })
  const heat = useQuery({ queryKey: ['stats', 'heatmap'], queryFn: statsApi.heatmap })
  const activities = useQuery({ queryKey: ['stats', 'top-activities'], queryFn: statsApi.topActivities })

  const cells = heat.data ?? []
  const heatCells = cells.map((c) => ({ d: c.dayOfWeek, h: c.hour, count: c.count }))
  const byDow: VBar[] = DOW_LABELS.map((label, d) => ({
    label,
    count: cells.filter((x) => x.dayOfWeek === d).reduce((s, x) => s + x.count, 0),
  }))
  const busiest = byDow.reduce((best, d) => (d.count > best.count ? d : best), byDow[0])

  const o = overview.data
  const rate = o && o.totalMessages > 0 ? Math.round((o.totalReactions / o.totalMessages) * 100) / 100 : 0
  const donutValue = Math.min(rate, 1) * 100

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <Panel title="Activity heatmap" sub="messages by hour × weekday · CET/CEST">
        {heat.isLoading ? (
          <ChartSkeleton h={170} />
        ) : heatCells.length > 0 ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 32, flexWrap: 'wrap' }}>
            <HeatGrid data={heatCells} cell={22} gap={5} accent={C.blurple} />
            <div style={{ flex: '1 1 180px', minWidth: 170 }}>
              <span style={{ fontSize: 12.5, color: C.muted }}>Busiest day</span>
              <div style={{ ...disp, fontSize: 22, color: C.text, margin: '4px 0 16px', whiteSpace: 'nowrap' }}>{busiest.label}</div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11.5, color: C.faint }}>
                less
                {[0.2, 0.45, 0.7, 0.95].map((opacity) => (
                  <span key={opacity} style={{ width: 13, height: 13, borderRadius: 3, background: C.blurple, opacity }} />
                ))}
                more
              </div>
            </div>
          </div>
        ) : (
          <EmptyState icon="timeline" line="No activity recorded yet." />
        )}
      </Panel>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <Panel title="Messages by weekday">
          {heat.isLoading ? (
            <ChartSkeleton h={180} />
          ) : heatCells.length > 0 ? (
            <VBars data={byDow} color={C.teal} labelEvery={1} h={180} />
          ) : (
            <EmptyState icon="message" line="No messages recorded yet." />
          )}
        </Panel>
        <Panel title="Top activities" sub="presence — includes noise">
          {activities.isLoading ? <ChartSkeleton h={180} /> : <TopActivities rows={activities.data ?? []} />}
        </Panel>
      </div>

      <Panel title="Reaction rate" sub="reactions per message">
        {overview.isLoading ? (
          <ChartSkeleton h={110} />
        ) : o ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 28, flexWrap: 'wrap' }}>
            <div style={{ position: 'relative', width: 110, height: 110, flexShrink: 0 }}>
              <Donut value={donutValue} max={100} size={110} stroke={12} color={C.fuchsia} />
              <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
                <span style={{ ...disp, fontSize: 24, color: C.text }}>{rate.toFixed(2)}</span>
                <span style={{ fontSize: 10.5, color: C.faint }}>per msg</span>
              </div>
            </div>
            <div style={{ fontSize: 14, color: C.muted, lineHeight: 1.6, maxWidth: 560 }}>
              <b style={{ color: C.text }}>{fmt(o.totalReactions)}</b> reactions across <b style={{ color: C.text }}>{fmt(o.totalMessages)}</b> messages — an average of{' '}
              <b style={{ color: C.fuchsia }}>{rate.toFixed(2)}</b> reactions per message.
            </div>
          </div>
        ) : (
          <EmptyState icon="reaction" line="No reactions recorded yet." />
        )}
      </Panel>
    </div>
  )
}

// ---------------------------------------------------------------- page

export default function Stats() {
  const [tab, setTab] = useState<Tab>('Volume & trends')

  return (
    <div style={{ padding: '24px 32px 40px', maxWidth: 1280, margin: '0 auto' }}>
      <div style={{ marginBottom: 18 }}>
        <h1 style={{ ...disp, fontSize: 30, fontWeight: 800, margin: 0 }}>Statistics</h1>
        <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>All times in guild-local time (CET/CEST)</p>
      </div>

      <div style={{ display: 'flex', gap: 4, borderBottom: `1px solid ${C.border}`, marginBottom: 22, flexWrap: 'wrap' }}>
        {TABS.map((t) => {
          const on = tab === t
          return (
            <button
              key={t}
              onClick={() => setTab(t)}
              style={{
                padding: '11px 16px',
                border: 'none',
                background: 'none',
                cursor: 'pointer',
                fontFamily: 'inherit',
                fontSize: 14,
                fontWeight: 600,
                color: on ? C.text : C.muted,
                borderBottom: `2px solid ${on ? C.blurple : 'transparent'}`,
                marginBottom: -1,
                transition: 'color .15s',
              }}
            >
              {t}
            </button>
          )
        })}
      </div>

      {tab === 'Volume & trends' && <VolumeTab />}
      {tab === 'People' && <PeopleTab />}
      {tab === 'Places' && <PlacesTab />}
      {tab === 'Behavior' && <BehaviorTab />}
    </div>
  )
}
