// Pure formatting / geometry helpers ported from the prototype's shared.jsx
// (+ app.jsx hours() and timeline.jsx agoStr()). NO React in this file.
import { C } from '../theme'

/** A 2D point [x, y] used by the SVG path helpers. */
export type Pt = [number, number]

/** 1,234,567 — locale-grouped integer. */
export function fmt(n: number): string {
  return n.toLocaleString('en-US')
}

/** 1.2k / 3.4M — compact human count. */
export function compact(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1).replace(/\.0$/, '') + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1).replace(/\.0$/, '') + 'k'
  return String(n)
}

/** Minutes -> "1h 20m" / "45m" / "1.2kh" for very large spans. */
export function hhmm(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  if (h >= 1000) return compact(h) + 'h'
  if (h > 0) return m > 0 ? `${h}h ${m}m` : `${h}h`
  return `${m}m`
}

/** Minutes -> whole-hours label, e.g. "83h" (app.jsx hours()). */
export function hours(min: number): string {
  const h = Math.round(min / 60)
  return (h >= 10000 ? compact(h) : fmt(h)) + 'h'
}

/** Minutes-elapsed -> short relative label (timeline.jsx agoStr). */
export function agoStr(min: number): string {
  if (min < 1) return 'now'
  if (min < 60) return `${min}m`
  if (min < 1440) return `${Math.floor(min / 60)}h`
  return `${Math.floor(min / 1440)}d`
}

/** Discord presence status -> palette colour. */
export const statusColor = {
  online: C.green,
  idle: C.amber,
  dnd: C.red,
  offline: C.faint,
} as const

export type PresenceStatus = keyof typeof statusColor

/** Two-letter uppercase initials from a display name. */
export function initials(name: string): string {
  const s = name.replace(/[^A-Za-zÀ-ž0-9 ]/g, '').trim()
  const parts = s.split(/\s+/)
  const out = parts.length > 1 ? parts[0][0] + parts[1][0] : s.slice(0, 2)
  return out.toUpperCase()
}

/** Deterministic 0–359 hue derived from any string (stable per name/id). */
export function hueFromString(s: string): number {
  let h = 0
  for (let i = 0; i < s.length; i++) {
    h = (h * 31 + s.charCodeAt(i)) % 360
  }
  return h
}

/** Catmull-Rom-ish smooth cubic-bezier path through the given points. */
export function smoothPath(pts: Pt[]): string {
  if (pts.length < 2) return ''
  let d = `M ${pts[0][0]},${pts[0][1]}`
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i === 0 ? 0 : i - 1]
    const p1 = pts[i]
    const p2 = pts[i + 1]
    const p3 = pts[i + 2] ?? p2
    const cp1x = p1[0] + (p2[0] - p0[0]) / 6
    const cp1y = p1[1] + (p2[1] - p0[1]) / 6
    const cp2x = p2[0] - (p3[0] - p1[0]) / 6
    const cp2y = p2[1] - (p3[1] - p1[1]) / 6
    d += ` C ${cp1x},${cp1y} ${cp2x},${cp2y} ${p2[0]},${p2[1]}`
  }
  return d
}
