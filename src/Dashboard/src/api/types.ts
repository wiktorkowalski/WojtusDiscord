// Mirrors the backend DTOs (camelCase). Snowflakes arrive as strings.

export type ColumnKind =
  | 'string'
  | 'int'
  | 'long'
  | 'snowflake'
  | 'bool'
  | 'timestamp'
  | 'uuid'
  | 'json'
  | 'number'
  | 'enum'
  | 'other'

export interface EnumValue {
  value: number
  name: string
}

export interface ColumnMetadata {
  name: string
  kind: ColumnKind
  isPrimaryKey: boolean
  isNullable: boolean
  enumValues?: EnumValue[]
}

export interface TableInfo {
  name: string
  displayName: string
  entityName: string
  rowCount: number
  populated: boolean
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

/** A generic explorer row: snake_case column name -> value. */
export type Row = Record<string, unknown>

export type SortDir = 'asc' | 'desc'
