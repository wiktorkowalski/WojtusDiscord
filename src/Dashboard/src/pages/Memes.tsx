import { useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import { BarRow, Panel, cardStyle, fmt } from '../ui'
import type { Load } from '../ui'
import { memeStatsApi } from '../api/memeStatsApi'
import type {
  MemeDistribution,
  MemeDistributions,
  MemeIndex,
  MemeLoggedSearch,
  MemeSearchHit,
  MemeSearchOutcome,
  MemeSearchUsage,
  MemeStatusCounts,
  MemeWriter,
  MemeYearCount,
} from '../api/memeStatsApi'

const mono = 'JetBrains Mono, monospace'
const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const labelStyle: CSSProperties = { fontSize: 11, fontWeight: 600, letterSpacing: '.12em', textTransform: 'uppercase', color: C.muted }
const noteStyle: CSSProperties = { margin: 0, padding: '8px 0', fontSize: 13, color: C.muted }
const monoValue: CSSProperties = { fontFamily: mono, color: C.text }
const stripes = `repeating-linear-gradient(135deg, ${C.card} 0 10px, ${C.border} 10px 20px)`

const USAGE_DAYS = 30
const HIT_LIMIT = 6
// The server refuses a longer query (MemeStatsController.MaxQueryLength).
const MAX_QUERY_LENGTH = 200

// ---- Formatting: the server buckets by Europe/Warsaw, so the page shows guild-local dates too. ----

const TZ = 'Europe/Warsaw'
const dateFormat = new Intl.DateTimeFormat('en-GB', { timeZone: TZ, day: 'numeric', month: 'short', year: 'numeric' })
const timeFormat = new Intl.DateTimeFormat('en-GB', { timeZone: TZ, day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false })
const yearFormat = new Intl.DateTimeFormat('en-US', { timeZone: TZ, year: 'numeric' })

const guildDay = (iso: string): string => dateFormat.format(new Date(iso))
const guildTime = (iso: string): string => timeFormat.format(new Date(iso))

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB']

/** 2,072,301,568 -> "1.93 GB" (1 GB = 1024 MB). */
function fileSize(bytes: number): string {
  const unit = Math.min(BYTE_UNITS.length - 1, bytes > 0 ? Math.floor(Math.log(bytes) / Math.log(1024)) : 0)
  const value = bytes / 1024 ** unit
  return `${unit === 0 ? value : value.toFixed(value >= 100 ? 0 : value >= 10 ? 1 : 2)} ${BYTE_UNITS[unit]}`
}

const plural = (n: number, one: string, many: string): string => `${fmt(n)} ${n === 1 ? one : many}`

// ---- Labels for the codes the API returns ----

const NOT_CLASSIFIED = 'not classified (before schema v2)'

const IMAGE_KINDS: Record<string, string> = {
  template_meme: 'template meme',
  screenshot_post_or_chat: 'screenshot: post or chat',
  comic: 'comic',
  photo_with_caption: 'photo with caption',
  cutout_face_or_emote: 'cut-out face or emote',
  edited_photo: 'edited photo',
  video_frame: 'video frame',
  other: 'other',
}

const LANGUAGES: Record<string, string> = { pl: 'Polish', en: 'English', mixed: 'mixed', none: 'no text' }

const SEARCH_SOURCES: Record<string, string> = { slashCommand: '/meme', assistantTool: 'assistant', other: 'other' }

// A code the page does not know yet is shown as it is.
const imageKindLabel = (code: string | null): string => (code === null ? NOT_CLASSIFIED : (IMAGE_KINDS[code] ?? code))
const languageLabel = (code: string | null): string => (code === null ? NOT_CLASSIFIED : (LANGUAGES[code] ?? code))
const sourceLabel = (name: string | null): string => name ?? 'none visible'
const plainLabel = (name: string | null): string => name ?? 'none'

// ---- Thumbnail ----

// The thumbnail route answers 404 or 503 in normal use, so a failed image becomes a placeholder.
function Thumb({ src, alt, placeholder, style }: { src: string | null; alt: string; placeholder?: string; style: CSSProperties }) {
  const [failed, setFailed] = useState(false)
  if (!src || failed) {
    return (
      <div style={{ ...style, background: stripes, display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: mono, fontSize: 12, color: C.muted }}>
        {placeholder}
      </div>
    )
  }
  return <img src={src} alt={alt} loading="lazy" onError={() => setFailed(true)} style={{ ...style, objectFit: 'contain', background: C.bg }} />
}

// ---- Header ----

function AutomaticIndexingPill({ on }: { on: boolean }) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 8,
        padding: '9px 14px',
        borderRadius: 10,
        border: `1px solid ${C.borderSoft}`,
        background: C.bg2,
        fontSize: 13,
        fontWeight: 600,
        whiteSpace: 'nowrap',
      }}
    >
      {/* Filled dot = on, hollow ring = off; the text says it too. */}
      <span aria-hidden="true" style={{ width: 8, height: 8, borderRadius: '50%', border: `2px solid ${on ? C.green : C.muted}`, background: on ? C.green : 'transparent' }} />
      Automatic indexing: {on ? 'on' : 'off'}
    </span>
  )
}

// ---- Tiles ----

function Tile({ name, value, sub }: { name: string; value: string; sub: string }) {
  return (
    <div className="animate-rise" style={{ ...cardStyle, padding: 16, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 10 }}>
      <span style={labelStyle}>{name}</span>
      <span style={{ ...disp, fontSize: 30, lineHeight: 1, fontVariantNumeric: 'tabular-nums' }}>{value}</span>
      <span style={{ fontSize: 12, color: C.muted }}>{sub}</span>
    </div>
  )
}

function IndexTiles({ index }: { index: MemeIndex }) {
  const { status } = index
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(150px, 100%), 1fr))', gap: 12 }}>
      <Tile name="Indexed" value={fmt(status.indexed)} sub={`of ${plural(status.total, 'attachment', 'attachments')}`} />
      <Tile name="Pending" value={fmt(status.pending)} sub="waiting for a writer" />
      <Tile name="Failed" value={fmt(status.failed)} sub="after retries" />
      <Tile name="Refusals" value={fmt(index.refusalCount)} sub="model declined the image" />
      <Tile name="Writers" value={fmt(index.writers.length)} sub="model × prompt version" />
      <Tile name="Files" value={fileSize(index.totalFileSizeBytes)} sub="total attachment size" />
    </div>
  )
}

// ---- Pipeline + writers ----

const STATUSES: { key: keyof MemeStatusCounts; name: string; color: string }[] = [
  { key: 'indexed', name: 'Indexed', color: C.green },
  { key: 'pending', name: 'Pending', color: C.blue },
  { key: 'failed', name: 'Failed', color: C.red },
  { key: 'skipped', name: 'Skipped', color: C.faint },
]

const cellStyle: CSSProperties = { padding: '10px 14px 10px 0', borderTop: `1px solid ${C.border}`, textAlign: 'left', whiteSpace: 'nowrap' }
const headCellStyle: CSSProperties = { ...labelStyle, padding: '8px 14px 8px 0', textAlign: 'left', whiteSpace: 'nowrap' }

function Pipeline({ status, writers }: { status: MemeStatusCounts; writers: MemeWriter[] }) {
  return (
    <>
      {/* The legend below carries every number, so the bar itself is decoration. An empty index leaves the bare track. */}
      <div aria-hidden="true" style={{ display: 'flex', gap: 2, height: 14, borderRadius: 7, overflow: 'hidden', background: C.bg1 }}>
        {STATUSES.filter((s) => status[s.key] > 0).map((s) => (
          <span key={s.key} title={`${s.name}: ${fmt(status[s.key])}`} style={{ flex: status[s.key], minWidth: 3, background: s.color }} />
        ))}
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px 22px', marginTop: 12, fontSize: 12.5, color: C.muted }}>
        {STATUSES.map((s) => (
          <span key={s.key} style={{ display: 'inline-flex', alignItems: 'center', gap: 7 }}>
            <span aria-hidden="true" style={{ width: 10, height: 10, borderRadius: 3, background: s.color }} />
            {s.name} <b style={monoValue}>{fmt(status[s.key])}</b>
          </span>
        ))}
      </div>

      <h3 style={{ ...labelStyle, margin: '24px 0 10px' }}>Writers</h3>
      {writers.length === 0 ? (
        <p style={noteStyle}>No writer has annotated a meme yet.</p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', minWidth: 420, borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr>
                {['Model', 'Prompt', 'Annotations', 'Coverage'].map((h) => (
                  <th key={h} scope="col" style={headCellStyle}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {writers.map((w) => (
                <tr key={`${w.modelId} ${w.promptVersion}`}>
                  <td style={{ ...cellStyle, fontFamily: mono }}>{w.modelId}</td>
                  <td style={{ ...cellStyle, fontFamily: mono }}>{w.promptVersion}</td>
                  <td style={{ ...cellStyle, fontFamily: mono }}>{fmt(w.annotationCount)}</td>
                  <td style={{ ...cellStyle, paddingRight: 0, width: '35%', minWidth: 130 }}>
                    <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <BarRow pct={Math.min(1, w.coveragePercent / 100)} color={C.fuchsia} />
                      <span style={{ fontFamily: mono, fontSize: 12, color: C.muted }}>{w.coveragePercent}%</span>
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

// ---- Not indexed yet ----

function NotIndexed({ index }: { index: MemeIndex }) {
  const { count, oldestPostedAtUtc } = index.notIndexed
  const auto = index.automaticIndexing
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <span style={{ ...disp, fontSize: 40, lineHeight: 1, fontVariantNumeric: 'tabular-nums' }}>{fmt(count)}</span>
      <p style={{ margin: 0, fontSize: 13.5, lineHeight: 1.5, color: C.muted }}>
        {count === 0 ? 'Nothing waits: every image in the meme channels has an index row. ' : 'Images posted in meme channels with no index row. '}
        {auto
          ? 'Automatic indexing is on, so the indexer takes every new meme without an import.'
          : 'Automatic indexing is off, so every new meme lands here until the next import.'}
      </p>
      <p style={{ margin: 0, fontSize: 12.5, color: C.muted }}>
        {auto ? 'Last annotation' : 'Last import'}: <span style={monoValue}>{index.lastAnnotationAtUtc ? guildDay(index.lastAnnotationAtUtc) : 'never'}</span>
        {oldestPostedAtUtc && (
          <>
            {' '}
            · oldest waiting: <span style={monoValue}>{guildDay(oldestPostedAtUtc)}</span>
          </>
        )}
      </p>
    </div>
  )
}

// ---- By year ----

const YEAR_BAR_MAX = 160

function YearChart({ years }: { years: MemeYearCount[] }) {
  const max = Math.max(...years.map((y) => y.count), 1)
  return (
    // A long history needs more room than a phone has: the chart scrolls inside its own box, not the page.
    <div style={{ overflowX: 'auto' }}>
      <div style={{ minWidth: years.length * 46 }}>
        <div role="list" style={{ height: 190, display: 'flex', alignItems: 'flex-end', gap: 10, borderBottom: `1px solid ${C.borderSoft}` }}>
          {years.map((y) => {
            const tip = `${y.year}: ${plural(y.count, 'meme', 'memes')}`
            return (
              <div
                key={y.year}
                role="listitem"
                aria-label={tip}
                title={tip}
                style={{ flex: 1, minWidth: 0, height: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', gap: 4 }}
              >
                <span style={{ fontFamily: mono, fontSize: 11.5, color: C.muted }}>{fmt(y.count)}</span>
                <span
                  style={{
                    width: '100%',
                    maxWidth: 72,
                    borderRadius: '4px 4px 0 0',
                    background: C.fuchsia,
                    height: y.count === 0 ? 0 : Math.max(2, Math.round((y.count / max) * YEAR_BAR_MAX)),
                  }}
                />
              </div>
            )
          })}
        </div>
        <div aria-hidden="true" style={{ display: 'flex', gap: 10, marginTop: 8 }}>
          {years.map((y) => (
            <span key={y.year} style={{ flex: 1, textAlign: 'center', fontFamily: mono, fontSize: 11.5, color: C.muted }}>
              {y.year}
            </span>
          ))}
        </div>
      </div>
    </div>
  )
}

// ---- Distributions ----

interface DistributionSpec {
  key: keyof MemeDistributions
  title: string
  color: string
  label: (name: string | null) => string
  aside: (d: MemeDistribution) => string
}

/** "8 kinds", plus the memes with no value when there are any. */
const closedSetAside = (lead: (d: MemeDistribution) => string, missing: string) => (d: MemeDistribution): string =>
  lead(d) + (d.missingCount > 0 ? ` · ${fmt(d.missingCount)} ${missing}` : '')

/** "top 8 of 412", plus the memes with no value when there are any. */
const topListAside = (d: MemeDistribution): string =>
  (d.distinctCount > d.buckets.length ? `top ${d.buckets.length} of ${fmt(d.distinctCount)}` : plural(d.distinctCount, 'value', 'values')) +
  (d.missingCount > 0 ? ` · ${fmt(d.missingCount)} with none` : '')

const DISTRIBUTIONS: DistributionSpec[] = [
  { key: 'imageKind', title: 'Image kind', color: C.fuchsia, label: imageKindLabel, aside: closedSetAside((d) => plural(d.distinctCount, 'kind', 'kinds'), 'not classified') },
  { key: 'source', title: 'Source platform', color: C.blurple, label: sourceLabel, aside: closedSetAside((d) => plural(d.distinctCount, 'platform', 'platforms'), 'with none visible') },
  { key: 'language', title: 'Language', color: C.teal, label: languageLabel, aside: closedSetAside(() => 'of the text in the image', 'not classified') },
  { key: 'templates', title: 'Top templates', color: C.fuchsia, label: plainLabel, aside: topListAside },
  { key: 'franchises', title: 'Top franchises', color: C.blurple, label: plainLabel, aside: topListAside },
  { key: 'tags', title: 'Top tags', color: C.teal, label: plainLabel, aside: topListAside },
]

function DistributionRows({ spec, distribution }: { spec: DistributionSpec; distribution: MemeDistribution }) {
  const max = Math.max(...distribution.buckets.map((b) => b.count), 1)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 9 }}>
      {distribution.buckets.map((b) => {
        const name = spec.label(b.name)
        return (
          <div key={b.name ?? '\u0000'} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span title={name} style={{ flex: '0 0 42%', minWidth: 0, fontSize: 12.5, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', color: b.name === null ? C.muted : C.text }}>
              {name}
            </span>
            <span style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
              {/* The bar tops out at 78% of the column so the number always fits next to it. */}
              <span
                aria-hidden="true"
                style={{ height: 8, borderRadius: '0 4px 4px 0', background: b.name === null ? C.faint : spec.color, width: `${Math.max(2, Math.round((b.count / max) * 78))}%` }}
              />
              <span style={{ fontFamily: mono, fontSize: 11.5, color: C.muted, whiteSpace: 'nowrap' }}>{fmt(b.count)}</span>
            </span>
          </div>
        )
      })}
    </div>
  )
}

// ---- Try a search ----

const chipStyle: CSSProperties = { padding: '3px 8px', borderRadius: 6, background: C.border, fontSize: 11.5, color: C.text }
const MAX_TAG_CHIPS = 6

function HitCard({ hit, trigramWeight }: { hit: MemeSearchHit; trigramWeight: number }) {
  const description = hit.descriptionPl ?? hit.descriptionEn
  const hiddenTags = hit.tags.length - MAX_TAG_CHIPS
  return (
    <article style={{ borderRadius: 12, border: `1px solid ${C.border}`, background: C.bg2, overflow: 'hidden', display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      <Thumb src={hit.thumbnailUrl} alt={description ?? hit.fileName} placeholder="image not available" style={{ width: '100%', height: 150 }} />
      <div style={{ padding: '12px 14px', display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', gap: '2px 10px', fontFamily: mono, fontSize: 12, color: C.muted }}>
          <span>#{hit.rank}</span>
          <span>
            score <b style={{ color: C.text }}>{hit.score.toFixed(3)}</b> = ts_rank {hit.tsRank.toFixed(3)} + {trigramWeight} × trigram {hit.trigramSimilarity.toFixed(3)}
          </span>
        </div>
        <p lang={hit.descriptionPl ? 'pl' : undefined} style={{ margin: 0, fontSize: 13, lineHeight: 1.45, overflowWrap: 'anywhere' }}>
          {description ?? hit.fileName}
        </p>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {/* Kind, templates, tags, in that order. The kind chip has an outline so it differs by more than position. */}
          <span title="Image kind" style={{ ...chipStyle, background: 'transparent', boxShadow: `inset 0 0 0 1px ${C.borderSoft}` }}>
            {imageKindLabel(hit.imageKind)}
          </span>
          {hit.templates.map((t) => (
            <span key={`template ${t}`} title="Template" style={chipStyle}>
              {t}
            </span>
          ))}
          {hit.tags.slice(0, MAX_TAG_CHIPS).map((t) => (
            <span key={`tag ${t}`} title="Tag" style={{ ...chipStyle, color: C.muted }}>
              {t}
            </span>
          ))}
          {hiddenTags > 0 && <span style={{ ...chipStyle, background: 'transparent', color: C.muted }}>+{hiddenTags} tags</span>}
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', gap: '2px 10px', fontSize: 12.5, color: C.muted }}>
          <a href={hit.jumpUrl} target="_blank" rel="noopener noreferrer" style={{ color: C.muted, textDecoration: 'underline' }}>
            Jump to message
          </a>
          <span>posted {guildDay(hit.postedAtUtc)}</span>
        </div>
      </div>
    </article>
  )
}

function SearchOutcome({ outcome }: { outcome: MemeSearchOutcome }) {
  if (outcome.kind === 'busy') return <p style={noteStyle}>The search is busy right now. Try again in a few seconds.</p>
  if (outcome.kind === 'rejected') return <p style={{ ...noteStyle, color: C.red }}>{outcome.message}</p>

  const { query, total, hits, trigramWeight } = outcome.result
  if (hits.length === 0) return <p style={noteStyle}>No meme matches “{query}”.</p>
  return (
    <>
      <p style={noteStyle}>
        {hits.length < total ? `The best ${hits.length} of ${plural(total, 'match', 'matches')}` : plural(total, 'match', 'matches')} for “{query}”.
      </p>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(280px, 100%), 1fr))', gap: 14 }}>
        {hits.map((h) => (
          <HitCard key={h.attachmentDiscordId} hit={h} trigramWeight={trigramWeight} />
        ))}
      </div>
    </>
  )
}

function SearchTester() {
  const [text, setText] = useState('')
  // A mutation, not a query: every run writes a search-log row, so it runs on submit only —
  // no refetch on focus or on the top bar's refresh, and no retry after a 429.
  const search = useMutation({ mutationFn: (q: string) => memeStatsApi.search(q, HIT_LIMIT) })
  const query = text.trim()
  const blocked = !query || search.isPending

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!blocked) search.mutate(query)
  }

  return (
    <Panel title="Try a search" load="ready">
      <form onSubmit={submit} style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'flex-end' }}>
        <div style={{ flex: '1 1 320px', minWidth: 0, display: 'flex', flexDirection: 'column', gap: 6 }}>
          <label htmlFor="meme-query" style={labelStyle}>
            Query
          </label>
          <input
            id="meme-query"
            type="search"
            value={text}
            onChange={(e) => setText(e.target.value)}
            maxLength={MAX_QUERY_LENGTH}
            placeholder="wojak za komputerem"
            autoComplete="off"
            style={{
              minHeight: 44,
              boxSizing: 'border-box',
              width: '100%',
              padding: '0 14px',
              borderRadius: 10,
              border: `1px solid ${C.borderSoft}`,
              background: C.bg,
              color: C.text,
              fontSize: 14,
              fontFamily: 'inherit',
            }}
          />
        </div>
        <button
          type="submit"
          disabled={blocked}
          style={{
            minHeight: 44,
            padding: '0 22px',
            borderRadius: 10,
            border: 'none',
            background: C.blurple,
            color: '#fff',
            fontSize: 14,
            fontWeight: 600,
            fontFamily: 'inherit',
            cursor: blocked ? 'default' : 'pointer',
            opacity: blocked ? 0.55 : 1,
          }}
        >
          Search
        </button>
      </form>
      {/* Remounts per run, so a thumbnail that failed once gets a new try. */}
      <div key={search.submittedAt} role="status" style={{ marginTop: 14 }}>
        {search.isPending ? (
          <p style={noteStyle}>Searching…</p>
        ) : search.isError ? (
          <p style={{ ...noteStyle, color: C.red }}>Could not run the search.</p>
        ) : search.data ? (
          <SearchOutcome outcome={search.data} />
        ) : (
          <p style={noteStyle}>No search run yet. Type a query and press Enter to see the ranked hits and how each one scored.</p>
        )}
      </div>
    </Panel>
  )
}

// ---- Search usage ----

function UsageTile({ name, value }: { name: string; value: string }) {
  return (
    <div style={{ padding: 14, borderRadius: 12, background: C.bg2, border: `1px solid ${C.border}`, display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
      <span style={labelStyle}>{name}</span>
      <span style={{ ...disp, fontSize: 24, lineHeight: 1, fontVariantNumeric: 'tabular-nums' }}>{value}</span>
    </div>
  )
}

const ellipsisCell: CSSProperties = { ...cellStyle, maxWidth: 240, overflow: 'hidden', textOverflow: 'ellipsis' }

function TopHitCell({ hit }: { hit: MemeLoggedSearch['topHit'] }) {
  if (!hit) return <span style={{ color: C.faint }}>–</span>
  // Every field but the id and the score is null once the meme has left the index.
  if (hit.fileName === null) return <span style={{ color: C.muted }}>no longer in the index</span>

  const text = hit.descriptionPl ?? hit.fileName
  return (
    <span style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
      <Thumb src={hit.thumbnailUrl} alt={hit.fileName} style={{ width: 32, height: 32, flex: 'none', borderRadius: 6 }} />
      <span title={text} style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {hit.jumpUrl ? (
          <a href={hit.jumpUrl} target="_blank" rel="noopener noreferrer" style={{ color: C.text, textDecoration: 'underline' }}>
            {text}
          </a>
        ) : (
          text
        )}
      </span>
    </span>
  )
}

function SearchUsage({ usage }: { usage: MemeSearchUsage }) {
  return (
    <>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(150px, 100%), 1fr))', gap: 12, marginBottom: 18 }}>
        <UsageTile name="Searches" value={fmt(usage.searchCount)} />
        <UsageTile name="Zero-result rate" value={usage.zeroResultRate === null ? '–' : `${Math.round(usage.zeroResultRate * 100)}%`} />
        <UsageTile name="Duration p95" value={usage.durationP95Ms === null ? '–' : `${fmt(Math.round(usage.durationP95Ms))} ms`} />
        <UsageTile name="Slash / assistant" value={`${fmt(usage.bySource.slashCommand)} / ${fmt(usage.bySource.assistantTool)}`} />
      </div>
      <div style={{ overflowX: 'auto', borderTop: `1px solid ${C.border}` }}>
        <table style={{ width: '100%', minWidth: 640, borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr>
              {['Time', 'Query', 'Source', 'Hits', 'ms', 'Top hit'].map((h) => (
                <th key={h} scope="col" style={{ ...headCellStyle, padding: '12px 14px 12px 0' }}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {usage.latest.map((s, i) => (
              <tr key={`${s.searchedAtUtc} ${i}`}>
                <td title={s.searchedAtUtc} style={{ ...cellStyle, fontFamily: mono, fontSize: 12, color: C.muted }}>
                  {guildTime(s.searchedAtUtc)}
                </td>
                <td title={s.query} style={ellipsisCell}>
                  {s.query}
                </td>
                <td style={cellStyle}>{SEARCH_SOURCES[s.source] ?? s.source}</td>
                <td style={{ ...cellStyle, fontFamily: mono }}>{fmt(s.resultCount)}</td>
                <td style={{ ...cellStyle, fontFamily: mono }}>{fmt(Math.round(s.durationMs))}</td>
                <td style={{ ...ellipsisCell, paddingRight: 0, maxWidth: 280 }}>
                  <TopHitCell hit={s.topHit} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {usage.latest.length === 0 && (
          <p style={{ margin: 0, padding: '28px 0 16px', borderTop: `1px solid ${C.border}`, textAlign: 'center', fontSize: 13.5, color: C.muted }}>
            No searches logged yet. Rows appear here after the first <span style={monoValue}>/meme</span> or assistant search.
          </p>
        )}
      </div>
    </>
  )
}

// ---- Page ----

/** Load state of a card that renders from one query; loaded data stays on screen through a failed refresh. */
const loadOf = (query: { data: unknown; isError: boolean }): Load => (query.data !== undefined ? 'ready' : query.isError ? 'error' : 'loading')

export default function Memes() {
  // fetchedAt gives "this year" without a clock read during render.
  const indexQuery = useQuery({ queryKey: ['memes', 'index'], queryFn: async () => ({ index: await memeStatsApi.index(), fetchedAt: Date.now() }) })
  const usageQuery = useQuery({ queryKey: ['memes', 'search-usage', USAGE_DAYS], queryFn: () => memeStatsApi.searchUsage(USAGE_DAYS) })

  const index = indexQuery.data?.index
  const load = loadOf(indexQuery)
  const byYear = index?.byYear ?? []
  const lastYear = byYear.at(-1)?.year
  const partialYear = indexQuery.data && lastYear === Number(yearFormat.format(indexQuery.data.fetchedAt)) ? lastYear : undefined
  const nothingIndexed = index?.searchableCount === 0 && 'No indexed memes yet.'

  return (
    <main style={{ maxWidth: 1280, margin: '0 auto', padding: '24px clamp(16px, 4vw, 32px) 48px', display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', justifyContent: 'space-between', gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <h1 style={{ ...disp, fontSize: 32, fontWeight: 800, margin: 0 }}>Meme index</h1>
          <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>
            What is indexed, who wrote it, what is in it, and how search uses it
            {index?.channels.map((c) => ` · #${c.name}`)}
          </p>
        </div>
        {index && <AutomaticIndexingPill on={index.automaticIndexing} />}
      </div>

      {/* A failed refresh keeps the last loaded numbers on screen, so say they are stale. */}
      {indexQuery.isError && index && (
        <p role="alert" style={{ ...noteStyle, color: C.red }}>
          Could not refresh the index stats. The numbers below are from the last successful load.
        </p>
      )}

      {index ? (
        <IndexTiles index={index} />
      ) : (
        <Panel title="Index totals" load={load}>
          {null}
        </Panel>
      )}

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 18 }}>
        <Panel title="Pipeline" load={load} style={{ flex: '2 1 480px' }}>
          {index && <Pipeline status={index.status} writers={index.writers} />}
        </Panel>
        <Panel title="Not indexed yet" load={load} style={{ flex: '1 1 300px' }}>
          {index && <NotIndexed index={index} />}
        </Panel>
      </div>

      <Panel title="Indexed memes by year posted" aside={partialYear ? `${partialYear} is a partial year` : undefined} load={load} empty={nothingIndexed}>
        <YearChart years={byYear} />
      </Panel>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(340px, 100%), 1fr))', gap: 18 }}>
        {DISTRIBUTIONS.map((spec) => {
          const distribution = index?.distributions[spec.key]
          return (
            <Panel
              key={spec.key}
              title={spec.title}
              titleSize={15}
              aside={distribution && distribution.buckets.length > 0 ? spec.aside(distribution) : undefined}
              load={load}
              empty={distribution?.buckets.length === 0 && (nothingIndexed || 'No indexed meme has a value here.')}
            >
              {distribution && <DistributionRows spec={spec} distribution={distribution} />}
            </Panel>
          )
        })}
      </div>

      <SearchTester />

      <Panel title="Search usage" aside={`last ${usageQuery.data?.days ?? USAGE_DAYS} days · from the search log`} load={loadOf(usageQuery)}>
        {usageQuery.data && <SearchUsage usage={usageQuery.data} />}
      </Panel>
    </main>
  )
}
