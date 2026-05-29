import { fetchApi, toQuery } from './client'

export interface TimelineEvent {
  id: string
  eventType: string
  guildDiscordId: string
  channelDiscordId: string | null
  userDiscordId: string | null
  receivedAtUtc: string
  jsonSizeBytes: number
  serializationFailed: boolean
  payload: unknown
}

export interface TimelinePage {
  events: TimelineEvent[]
  nextCursor: string | null
  hasMore: boolean
}

export interface TimelineFilters {
  eventType?: string
  userId?: string
  channelId?: string
  after?: string
  before?: string
}

export const timelineApi = {
  get: (filters: TimelineFilters, cursor?: string, pageSize = 50) =>
    fetchApi<TimelinePage>(`/timeline${toQuery({ ...filters, cursor, pageSize })}`),
}
