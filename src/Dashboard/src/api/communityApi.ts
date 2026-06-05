import { fetchApi, toQuery } from './client'

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
}

export interface Community {
  range: CommunityRange
  label: string
  prevLabel: string
  metrics: CommunityMetrics
  leaderboards: CommunityLeaderboards
}

export const communityApi = {
  get: (range: CommunityRange) =>
    fetchApi<Community>(`/stats/community${toQuery({ range })}`),
}
