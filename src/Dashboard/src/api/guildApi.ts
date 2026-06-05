import { fetchApi } from './client'

export type PresenceStatus = 'online' | 'idle' | 'dnd' | 'offline'

export interface GuildOnlineUser {
  userDiscordId: string
  username: string | null
  avatarHash: string | null
  status: PresenceStatus
}

export interface Guild {
  discordId: string
  name: string
  iconHash: string | null
  memberCount: number
  channelCount: number
  userCount: number
  eventSpanStartUtc: string | null
  online: GuildOnlineUser[]
}

export const guildApi = {
  get: () => fetchApi<Guild>('/guild'),
}
