import { fetchApi, toQuery } from './client'
import type { ColumnMetadata, PagedResult, Row, SortDir, TableInfo } from './types'

export interface RowsQuery {
  page?: number
  pageSize?: number
  sort?: string
  dir?: SortDir
  filterColumn?: string
  filter?: string
}

export const tablesApi = {
  list: () => fetchApi<TableInfo[]>('/tables'),
  columns: (table: string) =>
    fetchApi<ColumnMetadata[]>(`/tables/${encodeURIComponent(table)}/columns`),
  rows: (table: string, query: RowsQuery) =>
    fetchApi<PagedResult<Row>>(`/tables/${encodeURIComponent(table)}${toQuery({ ...query })}`),
}
