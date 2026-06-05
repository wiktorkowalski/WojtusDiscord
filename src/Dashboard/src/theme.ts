// Palette as JS constants — for Recharts (which needs colour values, not Tailwind
// classes). Keep in sync with the @theme block in index.css.
export const colors = {
  bg: '#1a1b1e',
  bgAlt: '#202125',
  bgDark: '#131417',
  bgCard: '#25262b',
  border: '#2e2f35',
  blurple: '#5865f2',
  accent: '#2dd4bf',
  accent2: '#eb459e',
  green: '#3ba55d',
  yellow: '#faa61a',
  red: '#ed4245',
  text: '#e7e9ec',
  muted: '#a8adb6',
  faint: '#6f747d',
} as const

// Categorical palette for charts with many series.
export const chartPalette = [
  '#5865f2', // blurple
  '#2dd4bf', // teal
  '#faa61a', // amber
  '#eb459e', // fuchsia
  '#3ba55d', // green
  '#00a8fc', // blue
  '#ed4245', // red
  '#9b59b6', // purple
  '#e67e22', // orange
  '#7289da', // legacy blurple
] as const

export function colorForIndex(i: number): string {
  return chartPalette[i % chartPalette.length]
}

// Full prototype palette ("C") — matches the redesign mock 1:1. Used by the
// new presentational primitives in src/ui/* (inline styles, pixel-faithful).
export const C = {
  bg: '#131417',
  bg1: '#1a1b1e',
  bg2: '#202125',
  card: '#25262b',
  border: '#2e2f35',
  borderSoft: '#34353c',
  blurple: '#5865f2',
  teal: '#2dd4bf',
  fuchsia: '#eb459e',
  green: '#3ba55d',
  amber: '#faa61a',
  red: '#ed4245',
  blue: '#00a8fc',
  text: '#e7e9ec',
  muted: '#a8adb6',
  faint: '#6f747d',
} as const
