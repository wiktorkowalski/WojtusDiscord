import type { CSSProperties } from 'react'
import { C } from '../theme'

/**
 * The mock's card surface: rounded 16, 1px border, subtle top-highlight gradient
 * + inset light edge, and a soft drop shadow. Values lifted 1:1 from overviewA.jsx.
 * Spread this into inline styles when a full Card component is overkill.
 */
export const cardStyle: CSSProperties = {
  borderRadius: 16,
  border: `1px solid ${C.border}`,
  background: `linear-gradient(180deg, rgba(255,255,255,.025), rgba(255,255,255,0) 120px), ${C.card}`,
  boxShadow: '0 1px 0 rgba(255,255,255,.04) inset, 0 12px 30px -18px rgba(0,0,0,.7)',
}
