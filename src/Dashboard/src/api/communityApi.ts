import { fetchApi, toQuery } from './client'
import type { ChannelActivity, EmojiStat, HeatmapCell } from './statsApi'

export type CommunityRange = 'week' | 'month' | 'all'

export interface CommunityMetric {
  value: number
  prev: number | null
  spark: number[]
}

export interface CommunityLeaderEntry {
  username: string | null
  userDiscordId: string
  avatarHash: string | null
  value: number
}

export interface CommunityMetrics {
  messages: CommunityMetric
  memes: CommunityMetric
  reactionsReceived: CommunityMetric
  voiceMinutes: CommunityMetric
  onlineMinutes: CommunityMetric
  activeMembers: CommunityMetric
}

export interface CommunityLeaderboards {
  topChatters: CommunityLeaderEntry[]
  memeLords: CommunityLeaderEntry[]
  reactionsReceived: CommunityLeaderEntry[]
  voice: CommunityLeaderEntry[]
  reactionsGiven: CommunityLeaderEntry[]
}

export interface CommunityActivity {
  name: string
  minutes: number
  players: number
}

export interface Community {
  range: CommunityRange
  label: string
  prevLabel: string
  metrics: CommunityMetrics
  leaderboards: CommunityLeaderboards
  // topEmotes, channels and topActivities follow the range window.
  topEmotes: EmojiStat[]
  channels: ChannelActivity[]
  topActivities: CommunityActivity[]
  // The heatmap always covers the last heatmapDays guild-local days, whatever the range.
  heatmap: HeatmapCell[]
  heatmapDays: number
}

export const communityApi = {
  get: (range: CommunityRange) =>
    fetchApi<Community>(`/stats/community${toQuery({ range })}`),
}
