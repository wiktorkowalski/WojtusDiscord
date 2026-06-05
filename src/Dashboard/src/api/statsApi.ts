import { fetchApi } from './client'

export interface VolumeDaily { day: string; count: number }
export interface VolumeByType { eventType: string; count: number }
export interface VolumeHourly { hour: number; count: number }
export interface UserStat {
  username: string | null
  userDiscordId: string
  count: number
  avatarHash?: string | null
}
export interface VoiceStat {
  username: string | null
  userDiscordId: string
  minutes: number
  avatarHash?: string | null
}
export interface ChannelActivity {
  channelName: string
  channelDiscordId: string
  messageCount: number
  reactionCount: number
}
export interface EmojiStat {
  emoteName: string
  emoteDiscordId: string | null
  isCustom: boolean
  count: number
}
export interface ActivityStat { name: string; count: number }
export interface HeatmapCell { dayOfWeek: number; hour: number; count: number }

export interface WindowCounts { today: number; week: number; month: number; total: number }
export interface DailyPoint { day: string; count: number }
export interface Overview {
  totalMessages: number
  totalReactions: number
  totalEvents: number
  voiceMinutes: number
  totalUsers: number
  totalChannels: number
  messages: WindowCounts
  reactions: WindowCounts
  topChatter: UserStat | null
  topChannel: ChannelActivity | null
  messagesDaily: DailyPoint[]
  topEmojis: EmojiStat[]
}

export const statsApi = {
  overview: () => fetchApi<Overview>('/stats/overview'),
  volumeDaily: () => fetchApi<VolumeDaily[]>('/stats/volume/daily'),
  volumeByType: () => fetchApi<VolumeByType[]>('/stats/volume/by-type'),
  volumeHourly: () => fetchApi<VolumeHourly[]>('/stats/volume/hourly'),
  topMessages: () => fetchApi<UserStat[]>('/stats/people/top-messages'),
  topReactionsGiven: () => fetchApi<UserStat[]>('/stats/people/top-reactions-given'),
  topReactionsReceived: () => fetchApi<UserStat[]>('/stats/people/top-reactions-received'),
  voiceLeaderboard: () => fetchApi<VoiceStat[]>('/stats/people/voice-leaderboard'),
  channelActivity: () => fetchApi<ChannelActivity[]>('/stats/places/channel-activity'),
  topEmojis: () => fetchApi<EmojiStat[]>('/stats/behavior/top-emojis'),
  topActivities: () => fetchApi<ActivityStat[]>('/stats/behavior/top-activities'),
  heatmap: () => fetchApi<HeatmapCell[]>('/stats/behavior/heatmap'),
}
