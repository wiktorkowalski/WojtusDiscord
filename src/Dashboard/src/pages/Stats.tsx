import { useMemo, useState } from 'react'
import { useQuery, type UseQueryResult } from '@tanstack/react-query'
import {
  ResponsiveContainer,
  AreaChart,
  Area,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from 'recharts'
import { statsApi, type UserStat } from '../api/statsApi'
import ChartCard from '../components/ChartCard'
import Leaderboard, { type LeaderboardRow } from '../components/Leaderboard'
import Heatmap from '../components/Heatmap'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'
import { colors } from '../theme'

type Tab = 'volume' | 'people' | 'places' | 'behavior'
const TABS: { key: Tab; label: string }[] = [
  { key: 'volume', label: 'Volume & trends' },
  { key: 'people', label: 'People' },
  { key: 'places', label: 'Places' },
  { key: 'behavior', label: 'Behavior' },
]

const tooltipStyle = {
  contentStyle: { background: colors.bgDark, border: `1px solid ${colors.border}`, borderRadius: 8 },
  labelStyle: { color: colors.muted },
  itemStyle: { color: colors.text },
}

export default function Stats() {
  const [tab, setTab] = useState<Tab>('volume')
  return (
    <div className="p-8">
      <PageHeader title="Statistics" subtitle="All times in guild-local time (CET/CEST)" />
      <div className="mb-5 flex gap-1 border-b border-discord-border">
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium ${
              tab === t.key ? 'border-blurple text-white' : 'border-transparent text-discord-muted hover:text-white'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'volume' && <VolumeTab />}
      {tab === 'people' && <PeopleTab />}
      {tab === 'places' && <PlacesTab />}
      {tab === 'behavior' && <BehaviorTab />}
    </div>
  )
}

function userRows(q: UseQueryResult<UserStat[]>): LeaderboardRow[] {
  return (q.data ?? []).map((u) => ({
    label: u.username ?? 'unknown',
    sublabel: u.username ? undefined : u.userDiscordId,
    value: u.count,
  }))
}

function VolumeTab() {
  const daily = useQuery({ queryKey: ['v-daily'], queryFn: statsApi.volumeDaily })
  const byType = useQuery({ queryKey: ['v-type'], queryFn: statsApi.volumeByType })
  const hourly = useQuery({ queryKey: ['v-hourly'], queryFn: statsApi.volumeHourly })

  const cumulative = useMemo(() => {
    let sum = 0
    return (daily.data ?? []).map((d) => ({ day: d.day, count: d.count, cumulative: (sum += d.count) }))
  }, [daily.data])

  if (daily.isLoading) return <Loading />
  if (daily.isError) return <ErrorState error={daily.error} />

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <div className="lg:col-span-2">
        <ChartCard title="Events per day">
          <ResponsiveContainer width="100%" height={260}>
            <AreaChart data={cumulative}>
              <CartesianGrid stroke={colors.border} vertical={false} />
              <XAxis dataKey="day" tick={{ fill: colors.faint, fontSize: 11 }} minTickGap={40} />
              <YAxis tick={{ fill: colors.faint, fontSize: 11 }} />
              <Tooltip {...tooltipStyle} />
              <Area type="monotone" dataKey="count" stroke={colors.blurple} fill={colors.blurple} fillOpacity={0.25} />
            </AreaChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>
      <div className="lg:col-span-2">
        <ChartCard title="Cumulative growth">
          <ResponsiveContainer width="100%" height={220}>
            <AreaChart data={cumulative}>
              <CartesianGrid stroke={colors.border} vertical={false} />
              <XAxis dataKey="day" tick={{ fill: colors.faint, fontSize: 11 }} minTickGap={40} />
              <YAxis tick={{ fill: colors.faint, fontSize: 11 }} />
              <Tooltip {...tooltipStyle} />
              <Area type="monotone" dataKey="cumulative" stroke={colors.green} fill={colors.green} fillOpacity={0.2} />
            </AreaChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>
      <ChartCard title="By event type">
        <ResponsiveContainer width="100%" height={300}>
          <BarChart data={byType.data ?? []} layout="vertical" margin={{ left: 40 }}>
            <XAxis type="number" tick={{ fill: colors.faint, fontSize: 11 }} />
            <YAxis type="category" dataKey="eventType" width={140} tick={{ fill: colors.faint, fontSize: 11 }} />
            <Tooltip {...tooltipStyle} />
            <Bar dataKey="count" fill={colors.blurple} radius={[0, 4, 4, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </ChartCard>
      <ChartCard title="By hour of day">
        <ResponsiveContainer width="100%" height={300}>
          <BarChart data={hourly.data ?? []}>
            <CartesianGrid stroke={colors.border} vertical={false} />
            <XAxis dataKey="hour" tick={{ fill: colors.faint, fontSize: 11 }} />
            <YAxis tick={{ fill: colors.faint, fontSize: 11 }} />
            <Tooltip {...tooltipStyle} />
            <Bar dataKey="count" fill={colors.green} radius={[4, 4, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </ChartCard>
    </div>
  )
}

function PeopleTab() {
  const messages = useQuery({ queryKey: ['p-msg'], queryFn: statsApi.topMessages })
  const given = useQuery({ queryKey: ['p-rg'], queryFn: statsApi.topReactionsGiven })
  const received = useQuery({ queryKey: ['p-rr'], queryFn: statsApi.topReactionsReceived })
  const voice = useQuery({ queryKey: ['p-voice'], queryFn: statsApi.voiceLeaderboard })

  if (messages.isLoading) return <Loading />
  if (messages.isError) return <ErrorState error={messages.error} />

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <ChartCard title="Most messages">
        <Leaderboard rows={userRows(messages)} unit="msgs" />
      </ChartCard>
      <ChartCard title="Voice minutes" note="Open (unclosed) sessions are excluded.">
        <Leaderboard
          rows={(voice.data ?? []).map((v) => ({
            label: v.username ?? 'unknown',
            sublabel: v.username ? undefined : v.userDiscordId,
            value: v.minutes,
          }))}
          unit="min"
          color={colors.green}
        />
      </ChartCard>
      <ChartCard title="Most reactions given">
        <Leaderboard rows={userRows(given)} unit="reactions" color={colors.yellow} />
      </ChartCard>
      <ChartCard title="Most reactions received">
        <Leaderboard rows={userRows(received)} unit="reactions" color={colors.yellow} />
      </ChartCard>
    </div>
  )
}

function PlacesTab() {
  const channels = useQuery({ queryKey: ['places'], queryFn: statsApi.channelActivity })
  if (channels.isLoading) return <Loading />
  if (channels.isError) return <ErrorState error={channels.error} />

  const data = channels.data ?? []
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <ChartCard title="Messages per channel">
        <Leaderboard rows={data.map((c) => ({ label: `#${c.channelName}`, value: c.messageCount }))} unit="msgs" />
      </ChartCard>
      <ChartCard title="Reactions per channel">
        <Leaderboard
          rows={data.map((c) => ({ label: `#${c.channelName}`, value: c.reactionCount }))}
          unit="reactions"
          color={colors.yellow}
        />
      </ChartCard>
    </div>
  )
}

function BehaviorTab() {
  const emojis = useQuery({ queryKey: ['b-emoji'], queryFn: statsApi.topEmojis })
  const activities = useQuery({ queryKey: ['b-act'], queryFn: statsApi.topActivities })
  const heatmap = useQuery({ queryKey: ['b-heat'], queryFn: statsApi.heatmap })

  if (heatmap.isLoading) return <Loading />
  if (heatmap.isError) return <ErrorState error={heatmap.error} />

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <div className="lg:col-span-2">
        <ChartCard title="Activity heatmap" note="Messages by hour × day of week (guild-local time)">
          <Heatmap cells={heatmap.data ?? []} />
        </ChartCard>
      </div>
      <ChartCard title="Top emojis">
        <Leaderboard
          rows={(emojis.data ?? []).map((e) => ({
            label: e.isCustom ? `:${e.emoteName}:` : e.emoteName,
            value: e.count,
          }))}
          unit="uses"
          color={colors.yellow}
        />
      </ChartCard>
      <ChartCard
        title="Top activities"
        note="Includes presence artifacts (e.g. 'Custom Status', 'Playing N/10') — shown, not filtered."
      >
        <Leaderboard rows={(activities.data ?? []).map((a) => ({ label: a.name, value: a.count }))} unit="seen" />
      </ChartCard>
    </div>
  )
}
