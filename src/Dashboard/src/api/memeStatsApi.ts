import { fetchApi, toQuery } from './client'

// The "Meme index" page (#395). Snowflakes are strings, dates are ISO UTC.

export interface MemeChannel { channelDiscordId: string; name: string }
export interface MemeStatusCounts { pending: number; indexed: number; failed: number; skipped: number; total: number }
export interface MemeWriter {
  modelId: string
  promptVersion: string
  annotationCount: number
  /** Annotations / indexed memes * 100; 0 when nothing is indexed. */
  coveragePercent: number
  lastAnnotationAtUtc: string
}
export interface MemeNotIndexed { count: number; oldestPostedAtUtc: string | null }
export interface MemeYearCount { year: number; count: number }
/** `name` null is its own bucket: the memes with no value. */
export interface MemeBucket { name: string | null; count: number }
export interface MemeDistribution { buckets: MemeBucket[]; distinctCount: number; missingCount: number }
export interface MemeDistributions {
  imageKind: MemeDistribution
  language: MemeDistribution
  source: MemeDistribution
  templates: MemeDistribution
  franchises: MemeDistribution
  tags: MemeDistribution
}
export interface MemeIndex {
  automaticIndexing: boolean
  channels: MemeChannel[]
  status: MemeStatusCounts
  refusalCount: number
  totalFileSizeBytes: number
  searchableCount: number
  writers: MemeWriter[]
  lastAnnotationAtUtc: string | null
  notIndexed: MemeNotIndexed
  byYear: MemeYearCount[]
  distributions: MemeDistributions
}

export interface MemeSearchSourceCounts { slashCommand: number; assistantTool: number; pageButton: number; other: number }
/** The fields other than the id and the score are null when the meme has left the index. */
export interface MemeLoggedTopHit {
  attachmentDiscordId: string
  score: number
  fileName: string | null
  descriptionPl: string | null
  jumpUrl: string | null
  thumbnailUrl: string | null
}
export interface MemeLoggedSearch {
  searchedAtUtc: string
  query: string
  /** slashCommand, assistantTool or other. */
  source: string
  resultCount: number
  durationMs: number
  topHit: MemeLoggedTopHit | null
}
export interface MemeSearchUsage {
  days: number
  searchCount: number
  zeroResultCount: number
  /** 0..1; null when there is no search. */
  zeroResultRate: number | null
  durationP95Ms: number | null
  bySource: MemeSearchSourceCounts
  latest: MemeLoggedSearch[]
}

export interface MemeSearchHit {
  rank: number
  attachmentDiscordId: string
  messageDiscordId: string
  channelDiscordId: string
  fileName: string
  descriptionPl: string | null
  descriptionEn: string | null
  imageKind: string | null
  templates: string[]
  tags: string[]
  postedAtUtc: string
  /** tsRank + trigramWeight * trigramSimilarity. */
  score: number
  tsRank: number
  trigramSimilarity: number
  modelId: string
  promptVersion: string
  jumpUrl: string
  thumbnailUrl: string
}
export interface MemeSearchResult { query: string; total: number; trigramWeight: number; hits: MemeSearchHit[] }

export type MemeSearchOutcome =
  | { kind: 'ok'; result: MemeSearchResult }
  /** 429: the server runs its maximum number of searches. */
  | { kind: 'busy' }
  /** 400: the server refused the input and says why. */
  | { kind: 'rejected'; message: string }

export const memeStatsApi = {
  index: () => fetchApi<MemeIndex>('/stats/memes'),
  searchUsage: (days: number) => fetchApi<MemeSearchUsage>(`/stats/memes/search-usage${toQuery({ days })}`),
  // Own fetch: the page needs the status code, which fetchApi folds into the error text.
  search: async (q: string, limit: number): Promise<MemeSearchOutcome> => {
    const res = await fetch(`/api/stats/memes/search${toQuery({ q, limit })}`)
    if (res.status === 429) return { kind: 'busy' }
    if (res.status === 400) {
      const body = (await res.json().catch(() => null)) as { error?: string } | null
      return { kind: 'rejected', message: body?.error ?? 'The server refused this query.' }
    }
    if (!res.ok) throw new Error(`API ${res.status} ${res.statusText}`)
    return { kind: 'ok', result: (await res.json()) as MemeSearchResult }
  },
}
