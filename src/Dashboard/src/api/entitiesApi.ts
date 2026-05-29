import { fetchApi, toQuery } from './client'
import type { PagedResult } from './types'

export interface UserList {
  id: string
  discordId: string
  username: string
  globalName: string | null
  isBot: boolean
  isSystem: boolean
  firstSeenUtc: string
  lastUpdatedUtc: string
}

export interface NameChange {
  usernameBefore: string | null
  usernameAfter: string | null
  globalNameBefore: string | null
  globalNameAfter: string | null
  changedAtUtc: string
}

export interface UserDetail extends UserList {
  discriminator: string | null
  membershipCount: number
  nameHistory: NameChange[]
}

export interface ChannelList {
  id: string
  discordId: string
  name: string
  type: string
  parentDiscordId: string | null
  isNsfw: boolean
  position: number
  isDeleted: boolean
}

export interface ChannelDetail extends ChannelList {
  topic: string | null
  messageCount: number
}

export interface MemberList {
  id: string
  userId: string
  userDiscordId: string
  username: string
  nickname: string | null
  joinedAtUtc: string | null
  isPending: boolean
  timeoutUntilUtc: string | null
}

export interface MessageList {
  id: string
  discordId: string
  content: string | null
  authorDiscordId: string
  authorName: string
  channelDiscordId: string
  channelName: string
  hasAttachments: boolean
  hasEmbeds: boolean
  isDeleted: boolean
  createdAtUtc: string
  editedAtUtc: string | null
}

export interface MessageEdit {
  contentBefore: string | null
  contentAfter: string | null
  editedAtUtc: string
  recordedAtUtc: string
}

export interface MessageDetail extends MessageList {
  attachmentsJson: string | null
  embedsJson: string | null
  deletedAtUtc: string | null
  editHistory: MessageEdit[]
}

interface Page {
  page?: number
  pageSize?: number
}

export const entitiesApi = {
  users: (q: Page & { search?: string }) =>
    fetchApi<PagedResult<UserList>>(`/entities/users${toQuery({ ...q })}`),
  user: (id: string) => fetchApi<UserDetail>(`/entities/users/${encodeURIComponent(id)}`),
  channels: (q: Page) => fetchApi<PagedResult<ChannelList>>(`/entities/channels${toQuery({ ...q })}`),
  channel: (id: string) => fetchApi<ChannelDetail>(`/entities/channels/${encodeURIComponent(id)}`),
  members: (q: Page) => fetchApi<PagedResult<MemberList>>(`/entities/members${toQuery({ ...q })}`),
  messages: (q: Page & { channelId?: string }) =>
    fetchApi<PagedResult<MessageList>>(`/entities/messages${toQuery({ ...q })}`),
  message: (id: string) => fetchApi<MessageDetail>(`/entities/messages/${encodeURIComponent(id)}`),
}
