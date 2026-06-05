import type { CSSProperties, ReactNode } from 'react'
import { cardStyle } from './cardStyle'

export interface CardProps {
  children: ReactNode
  /** Inner padding (number -> px, or any CSS padding value). Default 20. */
  padding?: number | string
  /** Extra inline styles merged after the base card style. */
  style?: CSSProperties
  className?: string
  onClick?: () => void
}

export function Card({ children, padding = 20, style, className, onClick }: CardProps) {
  return (
    <div
      className={className}
      onClick={onClick}
      style={{ ...cardStyle, padding, ...style }}
    >
      {children}
    </div>
  )
}
