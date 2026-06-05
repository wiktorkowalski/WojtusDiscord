import { fetchApi, toQuery } from './client'
import type { PagedResult } from './types'

export interface RawEventSummary {
  id: string
  eventType: string
  guildDiscordId: string
  channelDiscordId: string | null
  userDiscordId: string | null
  receivedAtUtc: string
  jsonSizeBytes: number
  serializationFailed: boolean
}

export interface RawEventDetail extends RawEventSummary {
  correlationId: string | null
  payload: unknown
}

export interface RawEventType {
  eventType: string
  count: number
}

export interface RawEventQuery {
  page?: number
  pageSize?: number
  eventType?: string
  failedOnly?: boolean
}

export const rawEventsApi = {
  types: () => fetchApi<RawEventType[]>('/raw-events/types'),
  list: (query: RawEventQuery) =>
    fetchApi<PagedResult<RawEventSummary>>(`/raw-events${toQuery({ ...query })}`),
  detail: (id: string) => fetchApi<RawEventDetail>(`/raw-events/${encodeURIComponent(id)}`),
}
