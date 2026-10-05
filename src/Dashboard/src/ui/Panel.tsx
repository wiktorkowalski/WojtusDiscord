import type { CSSProperties, ReactNode } from 'react'
import { C } from '../theme'
import { Icon } from './Icon'
import type { IconName } from './Icon'
import { cardStyle } from './cardStyle'

export type Load = 'loading' | 'error' | 'ready'

const noteStyle: CSSProperties = { margin: 0, padding: '8px 0', fontSize: 13, color: C.muted }

export interface PanelProps {
  title: string
  titleSize?: number
  icon?: { name: IconName; color: string }
  aside?: string
  load: Load
  /** Shown instead of the children when the data loaded but holds nothing. */
  empty?: string | false
  style?: CSSProperties
  children: ReactNode
}

/** Card shell with a title row and the loading / error / empty states. */
export function Panel({ title, titleSize = 17, icon, aside, load, empty, style, children }: PanelProps) {
  return (
    <section className="animate-rise" style={{ ...cardStyle, padding: 'clamp(16px, 4vw, 22px)', minWidth: 0, ...style }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'baseline', justifyContent: 'space-between', gap: 8, marginBottom: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
          {icon && <Icon name={icon.name} size={16} color={icon.color} />}
          <h2 style={{ fontFamily: 'Bricolage Grotesque, sans-serif', letterSpacing: '-0.02em', fontWeight: 700, fontSize: titleSize, margin: 0, color: C.text }}>
            {title}
          </h2>
        </div>
        {aside && <span style={{ fontSize: 12.5, color: C.muted }}>{aside}</span>}
      </div>
      {load === 'loading' ? (
        <p style={noteStyle} aria-busy="true">
          Loading…
        </p>
      ) : load === 'error' ? (
        <p style={{ ...noteStyle, color: C.red }}>Could not load this data.</p>
      ) : empty ? (
        <p style={noteStyle}>{empty}</p>
      ) : (
        children
      )}
    </section>
  )
}
