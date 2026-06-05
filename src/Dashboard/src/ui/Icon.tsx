// Hand-built 20x20 stroke icon set, ported 1:1 from the prototype's shared.jsx
// PATHS. Stroke-based by default; pass `fill` for solid glyphs.

const PATHS = {
  home: 'M3 9.5L10 4l7 5.5M5 8.5V16h10V8.5',
  timeline: 'M2 10h3l2-5 3 11 2.5-7 1.5 3H18',
  stats: 'M4 16V9M9 16V4M14 16v-6',
  entities:
    'M7 9a2.5 2.5 0 100-5 2.5 2.5 0 000 5zM2 16c0-2.5 2.2-4 5-4s5 1.5 5 4M13.5 9.5a2 2 0 100-4M14 16c0-2-1-3.2-2.5-3.8',
  tables:
    'M3 5c0-1.1 3.1-2 7-2s7 .9 7 2-3.1 2-7 2-7-.9-7-2zM3 5v10c0 1.1 3.1 2 7 2s7-.9 7-2V5M3 10c0 1.1 3.1 2 7 2s7-.9 7-2',
  raw: 'M7 3C5 3 5 6 4 7c-.6.6-1.5.6-1.5 1 0 .4.9.4 1.5 1 1 1 1 4 3 4M13 3c2 0 2 3 3 4 .6.6 1.5.6 1.5 1 0 .4-.9.4-1.5 1-1 1-1 4-3 4',
  message: 'M3 5.5A1.5 1.5 0 014.5 4h11A1.5 1.5 0 0117 5.5v7A1.5 1.5 0 0115.5 14H7l-4 3v-3z',
  reaction: 'M10 17a7 7 0 100-14 7 7 0 000 14zM7 8.5h.01M13 8.5h.01M7 12c.8 1 2 1.5 3 1.5s2.2-.5 3-1.5',
  voice: 'M10 3a2 2 0 012 2v5a2 2 0 11-4 0V5a2 2 0 012-2zM5 9a5 5 0 0010 0M10 14v3',
  members:
    'M7 9a2.5 2.5 0 100-5 2.5 2.5 0 000 5zM2 16c0-2.5 2.2-4 5-4s5 1.5 5 4M13.5 9.5a2 2 0 100-4M14 16c0-2-1-3.2-2.5-3.8',
  refresh: 'M15.5 9a5.5 5.5 0 10-1.6 3.9M15.5 6v3h-3',
  spotify: 'M10 2a8 8 0 100 16 8 8 0 000-16zM6 8.5c2.5-.7 5.5-.5 7.5.8M6.5 11.2c2-.5 4.2-.3 5.8.7M7 13.6c1.4-.3 3-.2 4.2.5',
  music: 'M7 15a2 2 0 11-4 0 2 2 0 014 0zm0 0V5l9-2v8M16 13a2 2 0 11-4 0 2 2 0 014 0z',
  fire: 'M10 17c3 0 5-2 5-4.8 0-2.5-1.7-3.8-2.5-5.2-.6 1-1.2 1.3-1.8 1.5C11 5.8 9.5 3.8 8 3c.4 2-1 3.2-2 4.6C5.3 8.6 5 9.7 5 11c0 3 2.2 6 5 6z',
  search: 'M9 15A6 6 0 109 3a6 6 0 000 12zM17 17l-3.5-3.5',
  chevron: 'M7 4l5 5-5 5',
  chevdown: 'M5 7l5 5 5-5',
  bolt: 'M11 2L4 11h5l-1 7 7-9h-5z',
  dot: 'M10 10m-3 0a3 3 0 106 0 3 3 0 10-6 0',
  clock: 'M10 17a7 7 0 100-14 7 7 0 000 14zM10 6v4l3 2',
  shield: 'M10 3l6 2v4c0 4-2.5 6.5-6 8-3.5-1.5-6-4-6-8V5z',
  pin: 'M10 17v-5M6 8.5L10 3l4 5.5-1.5 1.5h-5z',
  hash: 'M7 3L5 17M13 3l-2 14M4 7h12M3.5 13h12',
  arrow: 'M4 10h12M11 5l5 5-5 5',
  play: 'M6 4l9 6-9 6z',
  crown: 'M3 14l1.5-8 4 4 1.5-6 1.5 6 4-4L17 14z',
  activity: 'M2 10h3l2-5 3 11 2.5-7 1.5 3H18',
  eye: 'M2 10s3-6 8-6 8 6 8 6-3 6-8 6-8-6-8-6zm8 2.5a2.5 2.5 0 100-5 2.5 2.5 0 000 5z',
} as const

export type IconName = keyof typeof PATHS

export interface IconProps {
  name: IconName
  size?: number
  color?: string
  strokeW?: number
  fill?: boolean
}

export function Icon({
  name,
  size = 18,
  color = 'currentColor',
  strokeW = 1.6,
  fill = false,
}: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 20 20"
      fill={fill ? color : 'none'}
      stroke={fill ? 'none' : color}
      strokeWidth={strokeW}
      strokeLinecap="round"
      strokeLinejoin="round"
      style={{ flexShrink: 0, display: 'block' }}
    >
      <path d={PATHS[name]} />
    </svg>
  )
}
