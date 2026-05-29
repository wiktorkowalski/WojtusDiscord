import type { ColumnMetadata } from '../api/types'
import { formatTimestamp, truncate } from '../utils/format'

// Renders one explorer cell according to its column's logical kind.
export default function CellValue({ value, column }: { value: unknown; column: ColumnMetadata }) {
  if (value === null || value === undefined) {
    return <span className="text-discord-faint">—</span>
  }

  switch (column.kind) {
    case 'snowflake':
    case 'uuid':
      return <span className="mono text-discord-muted">{String(value)}</span>

    case 'timestamp': {
      const { text, title } = formatTimestamp(value)
      return (
        <span className="whitespace-nowrap text-discord-muted" title={title}>
          {text}
        </span>
      )
    }

    case 'bool':
      return value ? (
        <span className="text-discord-green">true</span>
      ) : (
        <span className="text-discord-faint">false</span>
      )

    case 'enum': {
      const match = column.enumValues?.find((e) => e.value === Number(value))
      return (
        <span className="rounded bg-discord-bg-dark px-1.5 py-0.5 text-xs text-discord-text">
          {match ? match.name : String(value)}
        </span>
      )
    }

    case 'json': {
      const text = typeof value === 'string' ? value : JSON.stringify(value)
      return <span className="mono text-discord-faint">{truncate(text, 60)}</span>
    }

    default:
      return <span className="text-discord-text">{truncate(String(value), 120)}</span>
  }
}
