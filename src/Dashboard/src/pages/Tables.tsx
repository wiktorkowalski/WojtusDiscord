import { useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import { Icon, compact, fmt } from '../ui'
import { relativeTime } from '../utils/format'
import { tablesApi } from '../api/tablesApi'
import type { ColumnKind, ColumnMetadata, TableInfo } from '../api/types'

const disp: CSSProperties = { fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700 }
const mono = 'JetBrains Mono, monospace'
const cardStyle: CSSProperties = {
  borderRadius: 16,
  border: `1px solid ${C.border}`,
  background: C.card,
  boxShadow: '0 1px 0 rgba(255,255,255,.04) inset, 0 14px 36px -22px rgba(0,0,0,.8)',
}

const PAGE_SIZE = 25

// ─── grouping ─────────────────────────────────────────────────────────────
type GroupName = 'Core' | 'Events/System' | 'System' | 'Other'
const GROUP_ORDER: GroupName[] = ['Core', 'Events/System', 'System', 'Other']
const CORE = new Set(['users', 'members', 'channels', 'roles', 'guilds', 'emotes', 'stickers', 'messages'])

function groupOf(name: string): GroupName {
  if (name.endsWith('_events') || name.endsWith('_logs') || name.endsWith('_heartbeats') || name.endsWith('_intervals')) return 'Events/System'
  if (CORE.has(name)) return 'Core'
  if (name.startsWith('bot_') || name.startsWith('backfill') || name.includes('failed')) return 'System'
  return 'Other'
}

// ─── kind colours + badge ───────────────────────────────────────────────────
const KIND_COLOR: Record<ColumnKind, string> = {
  uuid: C.faint,
  snowflake: C.blurple,
  string: C.text,
  enum: C.amber,
  bool: C.green,
  timestamp: C.teal,
  json: C.fuchsia,
  int: C.muted,
  long: C.muted,
  number: C.muted,
  other: C.faint,
}

function KindBadge({ kind }: { kind: ColumnKind }) {
  const col = KIND_COLOR[kind] ?? C.faint
  return (
    <span style={{ fontSize: 10, fontWeight: 600, color: col, background: `${col}1c`, padding: '1px 7px', borderRadius: 5, fontFamily: mono }}>
      {kind}
    </span>
  )
}

// ─── typed cell ─────────────────────────────────────────────────────────────
function Cell({ col, value }: { col: ColumnMetadata; value: unknown }) {
  if (value === null || value === undefined) {
    return <span style={{ color: C.faint, fontStyle: 'italic', fontSize: 12.5 }}>null</span>
  }
  switch (col.kind) {
    case 'snowflake':
    case 'uuid':
      return <span style={{ fontFamily: mono, fontSize: 12, color: C.muted }}>{String(value)}</span>
    case 'timestamp':
      return (
        <span style={{ color: C.muted, fontSize: 12.5 }} title={String(value)}>
          {relativeTime(value)}
        </span>
      )
    case 'enum': {
      const label = col.enumValues?.find((e) => e.value === value)?.name ?? String(value)
      return (
        <span style={{ fontSize: 11, fontWeight: 600, color: C.amber, background: 'rgba(250,166,26,.14)', padding: '2px 8px', borderRadius: 6, fontFamily: mono }}>
          {label}
        </span>
      )
    }
    case 'bool':
      return <span style={{ color: value ? C.green : C.faint, fontFamily: mono, fontSize: 12.5 }}>{value ? 'true' : 'false'}</span>
    case 'json':
      return (
        <span
          style={{ fontFamily: mono, fontSize: 11.5, color: C.fuchsia, background: 'rgba(235,69,158,.12)', padding: '2px 8px', borderRadius: 6 }}
          title={typeof value === 'string' ? value : JSON.stringify(value)}
        >
          {'{…}'}
        </span>
      )
    default:
      return <span style={{ fontSize: 13, color: C.text }}>{String(value)}</span>
  }
}

// ─── empty state ────────────────────────────────────────────────────────────
function EmptyState({ name }: { name: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 14, padding: '90px 24px', textAlign: 'center' }}>
      <div style={{ width: 60, height: 60, borderRadius: 16, background: C.bg2, border: `1px solid ${C.border}`, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <Icon name="tables" size={26} color={C.faint} />
      </div>
      <div>
        <div style={{ ...disp, fontSize: 18, color: C.text }}>No rows yet</div>
        <p style={{ fontSize: 13.5, color: C.muted, margin: '6px 0 0', maxWidth: 380 }}>
          <span style={{ fontFamily: mono, color: C.text }}>{name}</span> is tracked but hasn&apos;t recorded anything yet.
        </p>
      </div>
    </div>
  )
}

// ─── panel-level empty (no table selected / nothing populated) ───────────────
function NoSelection() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 12, padding: '90px 24px', textAlign: 'center' }}>
      <Icon name="tables" size={26} color={C.faint} />
      <div style={{ fontSize: 13.5, color: C.muted }}>Select a table to inspect its rows.</div>
    </div>
  )
}

type FilterKey = 'all' | 'populated' | 'empty'
const FILTERS: [FilterKey, string][] = [
  ['all', 'All'],
  ['populated', 'Populated'],
  ['empty', 'Empty'],
]

// ─── right-hand detail panel ────────────────────────────────────────────────
function DetailPanel({ table }: { table: TableInfo }) {
  // DetailPanel is keyed on table.name by the parent, so it remounts (and page
  // resets to 1) whenever the selected table changes.
  const [page, setPage] = useState(1)

  const columns = useQuery({
    queryKey: ['tables', 'columns', table.name],
    queryFn: () => tablesApi.columns(table.name),
  })
  const rows = useQuery({
    queryKey: ['tables', 'rows', table.name, page],
    queryFn: () => tablesApi.rows(table.name, { page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })

  const cols = columns.data ?? []
  const items = rows.data?.items ?? []
  const total = rows.data?.totalCount ?? table.rowCount
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  // The table LIST reports APPROXIMATE row counts (pg_stat n_live_tup), which read 0
  // for a freshly backfilled table until ANALYZE runs — so we always fetch and only
  // treat a table as empty once the rows query has actually returned zero, never on
  // the estimate (which previously made backfilled tables unreachable).
  const loadedEmpty = rows.isSuccess && total === 0
  const showEmpty = rows.isSuccess ? total === 0 : !table.populated

  return (
    <div style={{ ...cardStyle, padding: 0, overflow: 'hidden' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 18px', borderBottom: `1px solid ${C.border}`, gap: 12 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <h3 style={{ ...disp, fontSize: 17, margin: 0, color: C.text }}>{table.displayName}</h3>
            <span style={{ fontFamily: mono, fontSize: 11.5, color: C.faint }}>{table.name}</span>
          </div>
          <div style={{ fontSize: 12, color: C.muted, marginTop: 3 }}>
            entity <span style={{ fontFamily: mono, color: C.text }}>{table.entityName}</span> · {fmt(table.rowCount)} rows
          </div>
        </div>
        {showEmpty ? (
          <span style={{ fontSize: 11.5, fontWeight: 600, color: C.faint, background: C.bg2, padding: '4px 10px', borderRadius: 7, flexShrink: 0 }}>empty</span>
        ) : (
          <span style={{ fontSize: 11.5, fontWeight: 600, color: C.green, background: 'rgba(59,165,93,.14)', padding: '4px 10px', borderRadius: 7, flexShrink: 0 }}>populated</span>
        )}
      </div>

      {loadedEmpty && <EmptyState name={table.name} />}

      {(columns.isError || rows.isError) && (
        <div style={{ padding: '40px 18px', color: C.red, fontSize: 13.5 }}>Failed to load table data.</div>
      )}

      {!loadedEmpty && !columns.isError && !rows.isError && (
        <>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: cols.length > 4 ? 700 : 0 }}>
              <thead>
                <tr>
                  {cols.map((c) => (
                    <th key={c.name} style={{ textAlign: 'left', padding: '11px 13px', borderBottom: `1px solid ${C.border}`, whiteSpace: 'nowrap' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                        <span style={{ fontFamily: mono, fontSize: 12, color: C.text, fontWeight: 600 }}>{c.name}</span>
                        {c.isPrimaryKey && <span style={{ fontSize: 9, fontWeight: 700, color: C.amber, fontFamily: mono }}>PK</span>}
                      </div>
                      <div style={{ marginTop: 4 }}>
                        <KindBadge kind={c.kind} />
                      </div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {items.map((r, i) => (
                  <tr
                    key={i}
                    style={{ borderBottom: `1px solid ${C.border}`, transition: 'background .12s' }}
                    onMouseEnter={(e) => (e.currentTarget.style.background = C.bg2)}
                    onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
                  >
                    {cols.map((c) => (
                      <td key={c.name} style={{ padding: '11px 13px', maxWidth: 240, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        <Cell col={c} value={r[c.name]} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>

            {(columns.isLoading || rows.isLoading) && items.length === 0 && (
              <div style={{ padding: '60px 18px', textAlign: 'center', color: C.faint, fontSize: 13 }}>Loading rows…</div>
            )}
            {!columns.isLoading && !rows.isLoading && items.length === 0 && (
              <div style={{ padding: '40px 18px', color: C.muted, fontSize: 13.5 }}>No rows on this page.</div>
            )}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '12px 18px', borderTop: `1px solid ${C.border}`, flexWrap: 'wrap' }}>
            <span style={{ fontSize: 12.5, color: C.muted }}>
              Page <b style={{ color: C.text }}>{page}</b> of {fmt(totalPages)} · {fmt(total)} rows · cells render by column{' '}
              <span style={{ fontFamily: mono }}>kind</span>
            </span>
            <div style={{ display: 'flex', gap: 6 }}>
              <PageBtn label="Prev" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} />
              <PageBtn label="Next" disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))} />
            </div>
          </div>
        </>
      )}
    </div>
  )
}

function PageBtn({ label, disabled, onClick }: { label: string; disabled: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      style={{
        padding: '6px 14px',
        borderRadius: 8,
        border: `1px solid ${C.border}`,
        cursor: disabled ? 'default' : 'pointer',
        fontFamily: 'inherit',
        fontSize: 12.5,
        fontWeight: 600,
        background: disabled ? 'transparent' : C.bg2,
        color: disabled ? C.faint : C.text,
        opacity: disabled ? 0.5 : 1,
      }}
    >
      {label}
    </button>
  )
}

// ─── page ─────────────────────────────────────────────────────────────────
export default function Tables() {
  const list = useQuery({ queryKey: ['tables'], queryFn: tablesApi.list })
  const [sel, setSel] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const [filter, setFilter] = useState<FilterKey>('all')

  const tables = useMemo(() => list.data ?? [], [list.data])
  const populatedCount = tables.filter((t) => t.populated).length
  const firstPopulated = tables.find((t) => t.populated) ?? tables[0]
  const selectedName = sel ?? firstPopulated?.name ?? null
  const selected = tables.find((t) => t.name === selectedName)

  const query = q.trim().toLowerCase()
  const groups = useMemo(() => {
    const filtered = tables.filter((t) => {
      const matchQ = !query || t.name.toLowerCase().includes(query) || t.displayName.toLowerCase().includes(query)
      const matchF = filter === 'all' || (filter === 'populated' ? t.populated : !t.populated)
      return matchQ && matchF
    })
    const byGroup = new Map<GroupName, TableInfo[]>()
    for (const t of filtered) {
      const g = groupOf(t.name)
      const arr = byGroup.get(g)
      if (arr) arr.push(t)
      else byGroup.set(g, [t])
    }
    return GROUP_ORDER.filter((g) => byGroup.has(g)).map((g) => {
      const arr = byGroup.get(g)!.slice().sort((a, b) => b.rowCount - a.rowCount)
      return [g, arr] as const
    })
  }, [tables, query, filter])

  const totalShown = groups.reduce((n, [, arr]) => n + arr.length, 0)

  return (
    <div style={{ padding: '24px 32px 40px', maxWidth: 1280, margin: '0 auto' }}>
      <div style={{ marginBottom: 18 }}>
        <h1 style={{ ...disp, fontSize: 30, fontWeight: 800, margin: 0 }}>Tables</h1>
        <p style={{ color: C.muted, fontSize: 14, margin: '6px 0 0' }}>
          Every database table · {populatedCount} populated of {tables.length} · read-only
        </p>
      </div>

      {list.isError && <div style={{ ...cardStyle, padding: 20, color: C.red, marginBottom: 16 }}>Failed to load tables.</div>}

      <div style={{ display: 'grid', gridTemplateColumns: '280px minmax(0, 1fr)', gap: 20, alignItems: 'start' }}>
        {/* left: table list */}
        <div style={{ ...cardStyle, padding: 12, position: 'sticky', top: 88 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, background: C.bg, border: `1px solid ${C.border}`, borderRadius: 9, padding: '7px 11px', marginBottom: 10 }}>
            <Icon name="search" size={14} color={C.faint} />
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Filter tables…"
              style={{ background: 'none', border: 'none', outline: 'none', color: C.text, fontFamily: 'inherit', fontSize: 13, width: '100%' }}
            />
          </div>
          <div style={{ display: 'flex', gap: 4, marginBottom: 10 }}>
            {FILTERS.map(([k, l]) => {
              const on = filter === k
              return (
                <button
                  key={k}
                  onClick={() => setFilter(k)}
                  style={{
                    flex: 1,
                    padding: '6px 0',
                    borderRadius: 8,
                    border: 'none',
                    cursor: 'pointer',
                    fontFamily: 'inherit',
                    fontSize: 12,
                    fontWeight: 600,
                    background: on ? C.bg2 : 'transparent',
                    color: on ? C.text : C.muted,
                    boxShadow: on ? `inset 0 0 0 1px ${C.border}` : 'none',
                  }}
                >
                  {l}
                </button>
              )
            })}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2, maxHeight: 560, overflowY: 'auto' }}>
            {list.isLoading &&
              Array.from({ length: 8 }, (_, i) => (
                <div key={i} style={{ height: 32, borderRadius: 8, background: C.bg2, opacity: 0.4, margin: '2px 0' }} />
              ))}
            {!list.isLoading && totalShown === 0 && (
              <div style={{ padding: '24px 8px', textAlign: 'center', color: C.faint, fontSize: 12.5 }}>
                <Icon name="search" size={18} color={C.faint} />
                <div style={{ marginTop: 8 }}>No tables match.</div>
              </div>
            )}
            {groups.map(([g, listForGroup]) => (
              <div key={g}>
                <div style={{ fontSize: 10, fontWeight: 700, letterSpacing: '.1em', textTransform: 'uppercase', color: C.faint, padding: '10px 8px 5px' }}>{g}</div>
                {listForGroup.map((tb) => {
                  const on = selectedName === tb.name
                  return (
                    <button
                      key={tb.name}
                      onClick={() => setSel(tb.name)}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 9,
                        width: '100%',
                        padding: '8px 9px',
                        borderRadius: 8,
                        border: 'none',
                        cursor: 'pointer',
                        textAlign: 'left',
                        fontFamily: 'inherit',
                        background: on ? `${C.blurple}1f` : 'transparent',
                        boxShadow: on ? `inset 2px 0 0 ${C.blurple}` : 'none',
                        transition: 'background .12s',
                      }}
                      onMouseEnter={(e) => {
                        if (!on) e.currentTarget.style.background = C.bg2
                      }}
                      onMouseLeave={(e) => {
                        if (!on) e.currentTarget.style.background = 'transparent'
                      }}
                    >
                      <span style={{ width: 6, height: 6, borderRadius: 3, background: tb.populated ? C.green : C.border, flexShrink: 0 }} />
                      <span
                        style={{
                          fontFamily: mono,
                          fontSize: 12.5,
                          color: on ? C.text : tb.populated ? C.muted : C.faint,
                          flex: 1,
                          whiteSpace: 'nowrap',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                        }}
                      >
                        {tb.name}
                      </span>
                      <span style={{ fontFamily: mono, fontSize: 11, color: tb.populated ? C.muted : C.faint }}>{compact(tb.rowCount)}</span>
                    </button>
                  )
                })}
              </div>
            ))}
          </div>
        </div>

        {/* right: detail */}
        {selected ? (
          <DetailPanel key={selected.name} table={selected} />
        ) : (
          <div style={{ ...cardStyle, padding: 0, overflow: 'hidden' }}>
            <NoSelection />
          </div>
        )}
      </div>
    </div>
  )
}
