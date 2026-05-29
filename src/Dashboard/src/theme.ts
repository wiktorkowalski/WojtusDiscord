// Discord-inspired palette as JS constants — for Recharts (which needs colour
// values, not Tailwind classes) and anywhere a hex is required in code.
// Keep in sync with the @theme block in index.css.
export const colors = {
  bg: '#313338',
  bgAlt: '#2b2d31',
  bgDark: '#1e1f22',
  bgCard: '#383a40',
  border: '#232428',
  blurple: '#5865f2',
  green: '#23a55a',
  yellow: '#f0b232',
  red: '#f23f43',
  text: '#dbdee1',
  muted: '#b5bac1',
  faint: '#949ba4',
} as const

// Categorical palette for charts with many series (e.g. stacked event types).
export const chartPalette = [
  '#5865f2', // blurple
  '#23a55a', // green
  '#f0b232', // yellow
  '#eb459e', // fuchsia
  '#3ba55d', // teal-green
  '#00a8fc', // blue
  '#f23f43', // red
  '#9b59b6', // purple
  '#e67e22', // orange
  '#1abc9c', // turquoise
  '#7289da', // legacy blurple
  '#faa61a', // amber
] as const

export function colorForIndex(i: number): string {
  return chartPalette[i % chartPalette.length]
}
