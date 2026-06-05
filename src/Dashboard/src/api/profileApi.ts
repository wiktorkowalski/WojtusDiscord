import { fetchApi } from './client'

export type PresenceStatus = 'online' | 'idle' | 'dnd' | 'offline'

export interface ProfileEmote {
  emoteName: string
  emoteDiscordId: string | null
  isCustom: boolean
}

export interface ProfileChannel {
  channelName: string
  channelDiscordId: string
}

export interface ProfileNameChange {
  usernameBefore: string | null
  usernameAfter: string | null
  globalNameBefore: string | null
  globalNameAfter: string | null
  changedAtUtc: string
}

export interface ProfileDailyPoint {
  day: string
  count: number
}

export interface Profile {
  userDiscordId: string
  username: string
  globalName: string | null
  avatarHash: string | null
  isBot: boolean
  status: PresenceStatus
  firstSeenUtc: string | null
  messageCount: number
  memeCount: number
  reactionsReceivedCount: number
  voiceMinutes: number
  onlineMinutes: number
  favoriteEmote: ProfileEmote | null
  busiestChannel: ProfileChannel | null
  messagesDaily14: ProfileDailyPoint[]
  nameHistory: ProfileNameChange[]
}

export const profileApi = {
  get: (discordId: string) =>
    fetchApi<Profile>(`/people/${encodeURIComponent(discordId)}/profile`),
}
