// Shared helpers for the humanized event feed (Timeline page + Overview live feed).
// The raw gateway payloads are DSharpPlus-serialised and inconsistent, so we humanize
// from the reliable top-level fields (eventType + resolved user/channel) and only
// opportunistically pull a reaction's emoji out of the payload.
import { useQuery } from '@tanstack/react-query'
import { entitiesApi } from '../api/entitiesApi'
import type { TimelineEvent } from '../api/timelineApi'
import { C } from '../theme'
import type { IconName } from './Icon'

export interface ResolvedUser {
  discordId: string
  name: string
  avatarHash: string | null
  isBot: boolean
}

export interface NameMaps {
  users: Map<string, ResolvedUser>
  channels: Map<string, string>
}

// Users (69) and channels (38) are small core entities — one page of each is plenty
// to resolve names/avatars for the feed. Cached and shared across the app.
export function useNameMaps() {
  return useQuery({
    queryKey: ['name-maps'],
    staleTime: 5 * 60_000,
    queryFn: async (): Promise<NameMaps> => {
      const [users, channels] = await Promise.all([
        entitiesApi.users({ pageSize: 200 }),
        entitiesApi.channels({ pageSize: 200 }),
      ])
      const userMap = new Map<string, ResolvedUser>()
      for (const u of users.items) {
        userMap.set(u.discordId, {
          discordId: u.discordId,
          name: u.globalName ?? u.username,
          avatarHash: u.avatarHash ?? null,
          isBot: u.isBot,
        })
      }
      const channelMap = new Map<string, string>()
      for (const c of channels.items) channelMap.set(c.discordId, c.name)
      return { users: userMap, channels: channelMap }
    },
  })
}

export type EventCategory =
  | 'message'
  | 'reaction'
  | 'voice'
  | 'typing'
  | 'presence'
  | 'member'
  | 'channel'
  | 'other'

export interface EventCategoryMeta {
  key: EventCategory
  label: string
  color: string
  icon: IconName
}

// Category catalogue (used by the Timeline filter chips and per-row styling).
export const EVENT_CATEGORIES: EventCategoryMeta[] = [
  { key: 'message', label: 'Messages', color: C.blurple, icon: 'message' },
  { key: 'reaction', label: 'Reactions', color: C.fuchsia, icon: 'reaction' },
  { key: 'voice', label: 'Voice', color: C.teal, icon: 'voice' },
  { key: 'member', label: 'Members', color: C.green, icon: 'members' },
  { key: 'channel', label: 'Channels', color: C.blue, icon: 'hash' },
  { key: 'typing', label: 'Typing', color: C.amber, icon: 'activity' },
  { key: 'presence', label: 'Presence', color: C.faint, icon: 'dot' },
]

const META_BY_KEY = new Map(EVENT_CATEGORIES.map((m) => [m.key, m]))

export function categoryOf(eventType: string): EventCategory {
  if (eventType.includes('Reaction')) return 'reaction'
  if (eventType.includes('Voice')) return 'voice'
  if (eventType.includes('Typing')) return 'typing'
  if (eventType.includes('Presence')) return 'presence'
  if (eventType.startsWith('Message')) return 'message'
  if (eventType.includes('Member') || eventType.includes('Ban')) return 'member'
  if (eventType.includes('Thread') || eventType.includes('Channel')) return 'channel'
  return 'other'
}

const OTHER_META: EventCategoryMeta = { key: 'other', label: 'Other', color: C.blue, icon: 'bolt' }

export function metaOf(category: EventCategory): EventCategoryMeta {
  return META_BY_KEY.get(category) ?? OTHER_META
}

// Comma-separated raw eventType list the API accepts for a chosen set of categories.
// The categories are matched by substring, so we send representative prefixes the
// backend filters with a contains/equals on event_type.
const CATEGORY_EVENT_TYPES: Record<EventCategory, string[]> = {
  message: ['MessageCreated', 'MessageUpdated', 'MessageDeleted', 'MessagePollVoted'],
  reaction: ['MessageReactionAdded', 'MessageReactionRemoved', 'MessageReactionsCleared'],
  voice: ['VoiceStateUpdated', 'VoiceServerUpdated'],
  typing: ['TypingStarted'],
  presence: ['PresenceUpdated'],
  member: ['GuildMemberAdded', 'GuildMemberUpdated', 'GuildMemberRemoved', 'GuildBanAdded', 'GuildBanRemoved'],
  channel: ['ChannelCreated', 'ChannelUpdated', 'ChannelDeleted', 'ThreadCreated', 'ThreadUpdated'],
  other: [],
}

export function eventTypesFor(categories: Set<EventCategory>): string | undefined {
  const types: string[] = []
  for (const cat of categories) types.push(...CATEGORY_EVENT_TYPES[cat])
  return types.length > 0 ? types.join(',') : undefined
}

const VERB: Record<string, string> = {
  MessageCreated: 'sent a message',
  MessageUpdated: 'edited a message',
  MessageDeleted: 'deleted a message',
  MessagePollVoted: 'voted in a poll',
  MessageReactionAdded: 'added a reaction',
  MessageReactionRemoved: 'removed a reaction',
  MessageReactionsCleared: 'cleared reactions',
  VoiceStateUpdated: 'updated voice state',
  TypingStarted: 'started typing',
  PresenceUpdated: 'updated presence',
  GuildMemberAdded: 'joined the server',
  GuildMemberUpdated: 'updated their profile',
  GuildMemberRemoved: 'left the server',
  ThreadCreated: 'created a thread',
  ChannelCreated: 'created a channel',
  ChannelUpdated: 'updated a channel',
  TypingStartedFallback: 'was active',
}

export interface HumanEvent {
  id: string
  rawType: string
  category: EventCategory
  meta: EventCategoryMeta
  user: ResolvedUser | null
  channelName: string | null
  verb: string
  emote: { name: string; id: string | null; custom: boolean } | null
  receivedAtUtc: string
  jsonSizeBytes: number
  serializationFailed: boolean
}

// Best-effort extraction of a reaction's emoji from the (untyped) payload.
function extractEmote(payload: unknown): HumanEvent['emote'] {
  if (typeof payload !== 'object' || payload === null) return null
  const emoji = (payload as { emoji?: unknown }).emoji
  if (typeof emoji !== 'object' || emoji === null) return null
  const e = emoji as { name?: unknown; id?: unknown }
  const name = typeof e.name === 'string' ? e.name : null
  if (name === null) return null
  const id = e.id === null || e.id === undefined ? null : String(e.id)
  return { name, id, custom: id !== null }
}

export function humanizeEvent(ev: TimelineEvent, maps: NameMaps | undefined): HumanEvent {
  const category = categoryOf(ev.eventType)
  const user = ev.userDiscordId ? (maps?.users.get(ev.userDiscordId) ?? null) : null
  const channelName = ev.channelDiscordId ? (maps?.channels.get(ev.channelDiscordId) ?? null) : null
  return {
    id: ev.id,
    rawType: ev.eventType,
    category,
    meta: metaOf(category),
    user,
    channelName,
    verb: VERB[ev.eventType] ?? humanizeFallback(ev.eventType),
    emote: category === 'reaction' ? extractEmote(ev.payload) : null,
    receivedAtUtc: ev.receivedAtUtc,
    jsonSizeBytes: ev.jsonSizeBytes,
    serializationFailed: ev.serializationFailed,
  }
}

// Turn an unmapped PascalCase event name into a readable phrase, e.g.
// "GuildAuditLogCreated" -> "guild audit log created".
function humanizeFallback(eventType: string): string {
  return eventType
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .toLowerCase()
}
