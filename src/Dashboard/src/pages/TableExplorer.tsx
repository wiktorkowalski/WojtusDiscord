import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { tablesApi } from '../api/tablesApi'
import type { Row, SortDir } from '../api/types'
import DataTable from '../components/DataTable'
import Pagination from '../components/Pagination'
import Modal from '../components/Modal'
import JsonViewer from '../components/JsonViewer'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'

// Generic paginated/sortable/filterable view over a single table, with a
// row-detail JSON drill-down. Empty/future tables work here automatically.
export default function TableExplorer() {
  const { table = '' } = useParams()

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [sort, setSort] = useState<string | undefined>(undefined)
  const [dir, setDir] = useState<SortDir>('desc')
  const [filterColumn, setFilterColumn] = useState('')
  const [filterInput, setFilterInput] = useState('')
  const [appliedFilter, setAppliedFilter] = useState('')
  const [selected, setSelected] = useState<Row | null>(null)

  const columnsQuery = useQuery({
    queryKey: ['columns', table],
    queryFn: () => tablesApi.columns(table),
  })

  const rowsQuery = useQuery({
    queryKey: ['rows', table, page, pageSize, sort, dir, filterColumn, appliedFilter],
    queryFn: () =>
      tablesApi.rows(table, {
        page,
        pageSize,
        sort,
        dir,
        filterColumn: appliedFilter ? filterColumn : undefined,
        filter: appliedFilter || undefined,
      }),
    placeholderData: keepPreviousData,
  })

  if (columnsQuery.isLoading) return <Loading />
  if (columnsQuery.isError) return <ErrorState error={columnsQuery.error} />

  const columns = columnsQuery.data ?? []

  const handleSort = (column: string) => {
    if (sort === column) {
      setDir((d) => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSort(column)
      setDir('desc')
    }
    setPage(1)
  }

  const applyFilter = () => {
    setAppliedFilter(filterInput.trim())
    setPage(1)
  }

  return (
    <div className="p-8">
      <PageHeader
        title={table}
        subtitle={rowsQuery.data ? `${rowsQuery.data.totalCount.toLocaleString()} rows` : undefined}
        actions={
          <Link to="/tables" className="text-sm text-discord-muted hover:text-white">
            ← all tables
          </Link>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <select
          value={filterColumn}
          onChange={(e) => setFilterColumn(e.target.value)}
          className="rounded bg-discord-bg-dark px-2 py-1.5 text-sm text-discord-text"
        >
          <option value="">filter column…</option>
          {columns.map((c) => (
            <option key={c.name} value={c.name}>
              {c.name}
            </option>
          ))}
        </select>
        <input
          value={filterInput}
          onChange={(e) => setFilterInput(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && applyFilter()}
          placeholder="contains…"
          disabled={!filterColumn}
          className="rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-text disabled:opacity-40"
        />
        <button
          onClick={applyFilter}
          disabled={!filterColumn}
          className="rounded bg-blurple px-3 py-1.5 text-sm text-white hover:bg-blurple-hover disabled:opacity-40"
        >
          Filter
        </button>
        {appliedFilter && (
          <button
            onClick={() => {
              setFilterInput('')
              setAppliedFilter('')
              setPage(1)
            }}
            className="text-sm text-discord-muted hover:text-white"
          >
            clear
          </button>
        )}
        <button
          onClick={() => rowsQuery.refetch()}
          className="ml-auto rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-muted hover:text-white"
        >
          ⟳ refresh
        </button>
      </div>

      {rowsQuery.isError ? (
        <ErrorState error={rowsQuery.error} />
      ) : (
        <>
          <DataTable
            columns={columns}
            rows={rowsQuery.data?.items ?? []}
            sort={sort}
            dir={dir}
            onSort={handleSort}
            onRowClick={setSelected}
          />
          <div className="mt-4">
            <Pagination
              page={page}
              pageSize={pageSize}
              total={rowsQuery.data?.totalCount ?? 0}
              onPageChange={setPage}
              onPageSizeChange={(s) => {
                setPageSize(s)
                setPage(1)
              }}
            />
          </div>
        </>
      )}

      {selected && (
        <Modal title={`${table} · row`} onClose={() => setSelected(null)}>
          <JsonViewer value={selected} />
        </Modal>
      )}
    </div>
  )
}
