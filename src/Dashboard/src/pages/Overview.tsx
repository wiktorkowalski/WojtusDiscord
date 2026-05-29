import { useQuery } from '@tanstack/react-query'
import { ResponsiveContainer, AreaChart, Area, XAxis, YAxis, Tooltip, CartesianGrid } from 'recharts'
import { statsApi, type WindowCounts } from '../api/statsApi'
import StatCard from '../components/StatCard'
import ChartCard from '../components/ChartCard'
import Heatmap from '../components/Heatmap'
import UserChip from '../components/UserChip'
import ChannelChip from '../components/ChannelChip'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'
import { IconMessage, IconSmile, IconMic, IconUsers, IconCrown, IconHash } from '../components/icons'
import { colors } from '../theme'
import { formatNumber } from '../utils/format'

export default function Overview() {
  const overview = useQuery({ queryKey: ['overview'], queryFn: statsApi.overview })
  const heatmap = useQuery({ queryKey: ['b-heat'], queryFn: statsApi.heatmap })

  if (overview.isLoading) return <div className="p-8"><Loading /></div>
  if (overview.isError) return <ErrorState error={overview.error} />
  const o = overview.data!

  const spark = o.messagesDaily.map((d) => d.count)

  return (
    <div className="p-8">
      <PageHeader
        title="Overview"
        subtitle={`${formatNumber(o.totalUsers)} members · ${formatNumber(o.totalChannels)} channels · ${formatNumber(o.totalEvents)} events captured`}
        actions={
          <button
            onClick={() => overview.refetch()}
            className="rounded-lg border border-discord-border bg-discord-bg-card px-3 py-1.5 text-sm text-discord-muted transition-colors hover:text-white"
          >
            ⟳ refresh
          </button>
        }
      />

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Messages"
          value={o.totalMessages}
          Icon={IconMessage}
          accent={colors.blurple}
          spark={spark}
          footer={<Windows w={o.messages} />}
          delay={0}
        />
        <StatCard
          label="Reactions"
          value={o.totalReactions}
          Icon={IconSmile}
          accent={colors.yellow}
          footer={<Windows w={o.reactions} />}
          delay={60}
        />
        <StatCard
          label="Voice minutes"
          value={o.voiceMinutes}
          Icon={IconMic}
          accent={colors.green}
          footer={<span>across all tracked sessions</span>}
          delay={120}
        />
        <StatCard
          label="Members"
          value={o.totalUsers}
          Icon={IconUsers}
          accent={colors.accent}
          footer={<span>{formatNumber(o.totalChannels)} channels tracked</span>}
          delay={180}
        />
      </div>

      <div className="mt-4 card animate-rise p-5" style={{ animationDelay: '220ms' }}>
        <h3 className="text-sm font-semibold text-white">Messages · last 30 days</h3>
        <div className="mt-4">
          <ResponsiveContainer width="100%" height={240}>
            <AreaChart data={o.messagesDaily} margin={{ left: -10, right: 8, top: 4 }}>
              <defs>
                <linearGradient id="msgArea" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={colors.blurple} stopOpacity={0.4} />
                  <stop offset="100%" stopColor={colors.blurple} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid stroke={colors.border} vertical={false} />
              <XAxis dataKey="day" tick={{ fill: colors.faint, fontSize: 11 }} minTickGap={40} tickLine={false} axisLine={false} />
              <YAxis tick={{ fill: colors.faint, fontSize: 11 }} tickLine={false} axisLine={false} width={42} />
              <Tooltip
                contentStyle={{ background: colors.bgDark, border: `1px solid ${colors.border}`, borderRadius: 10 }}
                labelStyle={{ color: colors.muted }}
                itemStyle={{ color: colors.text }}
              />
              <Area type="monotone" dataKey="count" stroke={colors.blurple} strokeWidth={2} fill="url(#msgArea)" />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
        <ChartCard title="Busiest channel">
          {o.topChannel ? (
            <div className="space-y-3">
              <ChannelChip name={o.topChannel.channelName} />
              <div className="flex gap-6">
                <Figure label="messages" value={o.topChannel.messageCount} />
                <Figure label="reactions" value={o.topChannel.reactionCount} />
              </div>
            </div>
          ) : (
            <Empty />
          )}
        </ChartCard>

        <ChartCard title="Top chatter">
          {o.topChatter ? (
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <IconCrown className="text-discord-yellow" />
                <UserChip name={o.topChatter.username ?? 'unknown'} />
              </div>
              <Figure label="messages" value={o.topChatter.count} />
            </div>
          ) : (
            <Empty />
          )}
        </ChartCard>

        <ChartCard title="Top emojis">
          {o.topEmojis.length > 0 ? (
            <ul className="space-y-2">
              {o.topEmojis.map((e) => (
                <li key={`${e.emoteName}-${e.emoteDiscordId ?? 'u'}`} className="flex items-center justify-between text-sm">
                  <span className="flex items-center gap-2 text-discord-text">
                    <IconHash className="text-discord-faint text-xs" />
                    {e.isCustom ? `:${e.emoteName}:` : e.emoteName}
                  </span>
                  <span className="tabular-nums text-discord-muted">{formatNumber(e.count)}</span>
                </li>
              ))}
            </ul>
          ) : (
            <Empty />
          )}
        </ChartCard>
      </div>

      <div className="mt-4 card animate-rise p-5" style={{ animationDelay: '260ms' }}>
        <h3 className="text-sm font-semibold text-white">Activity heatmap</h3>
        <p className="mb-3 mt-0.5 text-xs text-discord-faint">Messages by hour × day of week (guild-local time)</p>
        {heatmap.isLoading ? <Loading /> : <Heatmap cells={heatmap.data ?? []} />}
      </div>
    </div>
  )
}

function Windows({ w }: { w: WindowCounts }) {
  return (
    <span className="flex gap-3">
      <Pill k="today" v={w.today} />
      <Pill k="7d" v={w.week} />
      <Pill k="30d" v={w.month} />
    </span>
  )
}

function Pill({ k, v }: { k: string; v: number }) {
  return (
    <span>
      <span className="text-discord-faint">{k} </span>
      <span className="font-semibold text-discord-text">{formatNumber(v)}</span>
    </span>
  )
}

function Figure({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <div className="stat-figure text-2xl font-bold text-white">{formatNumber(value)}</div>
      <div className="text-xs text-discord-faint">{label}</div>
    </div>
  )
}

function Empty() {
  return <p className="text-sm text-discord-faint">No data yet.</p>
}
