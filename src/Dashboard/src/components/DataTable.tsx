import type { ColumnMetadata, Row, SortDir } from '../api/types'
import CellValue from './CellValue'

interface DataTableProps {
  columns: ColumnMetadata[]
  rows: Row[]
  sort?: string
  dir?: SortDir
  onSort?: (column: string) => void
  onRowClick?: (row: Row) => void
}

// Server-driven table: sorting/paging are owned by the parent; this only renders
// and emits sort requests. Reused by the explorer and entity views.
export default function DataTable({ columns, rows, sort, dir, onSort, onRowClick }: DataTableProps) {
  return (
    <div className="overflow-x-auto rounded-lg border border-discord-border">
      <table className="w-full border-collapse text-sm">
        <thead className="sticky top-0 bg-discord-bg-alt">
          <tr>
            {columns.map((col) => {
              const active = sort === col.name
              return (
                <th
                  key={col.name}
                  onClick={() => onSort?.(col.name)}
                  className={`select-none whitespace-nowrap border-b border-discord-border px-3 py-2 text-left font-semibold ${
                    onSort ? 'cursor-pointer hover:text-white' : ''
                  } ${col.isPrimaryKey ? 'text-blurple' : 'text-discord-muted'}`}
                  title={`${col.kind}${col.isPrimaryKey ? ' · primary key' : ''}`}
                >
                  {col.name}
                  {active && <span className="ml-1">{dir === 'asc' ? '▲' : '▼'}</span>}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr
              key={i}
              onClick={() => onRowClick?.(row)}
              className={`border-b border-discord-border/40 ${
                onRowClick ? 'cursor-pointer hover:bg-discord-bg-card' : ''
              }`}
            >
              {columns.map((col) => (
                <td key={col.name} className="max-w-md truncate px-3 py-1.5 align-top">
                  <CellValue value={row[col.name]} column={col} />
                </td>
              ))}
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={columns.length} className="px-3 py-8 text-center text-discord-faint">
                No rows.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
