import type { SVGProps } from 'react'

// Minimal lucide-style stroke icons (24x24, currentColor). Keeps the bundle lean
// and the look consistent vs. emoji.
type IconProps = SVGProps<SVGSVGElement>

function Svg({ children, ...props }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      width="1em"
      height="1em"
      {...props}
    >
      {children}
    </svg>
  )
}

export const IconHome = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 10.5 12 3l9 7.5" />
    <path d="M5 9.5V21h14V9.5" />
    <path d="M9 21v-6h6v6" />
  </Svg>
)
export const IconActivity = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 12h4l3 8 4-16 3 8h4" />
  </Svg>
)
export const IconChart = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 3v18h18" />
    <rect x="7" y="11" width="3" height="6" rx="1" />
    <rect x="12" y="7" width="3" height="10" rx="1" />
    <rect x="17" y="13" width="3" height="4" rx="1" />
  </Svg>
)
export const IconUsers = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="9" cy="8" r="3.2" />
    <path d="M3.5 20a5.5 5.5 0 0 1 11 0" />
    <path d="M16 5.2a3.2 3.2 0 0 1 0 5.6" />
    <path d="M17.5 20a5.5 5.5 0 0 0-3-4.9" />
  </Svg>
)
export const IconDatabase = (p: IconProps) => (
  <Svg {...p}>
    <ellipse cx="12" cy="5" rx="8" ry="3" />
    <path d="M4 5v6c0 1.66 3.58 3 8 3s8-1.34 8-3V5" />
    <path d="M4 11v6c0 1.66 3.58 3 8 3s8-1.34 8-3v-6" />
  </Svg>
)
export const IconBraces = (p: IconProps) => (
  <Svg {...p}>
    <path d="M8 3c-2 0-3 1-3 3v2c0 1.5-.5 2.5-2 3 1.5.5 2 1.5 2 3v2c0 2 1 3 3 3" />
    <path d="M16 3c2 0 3 1 3 3v2c0 1.5.5 2.5 2 3-1.5.5-2 1.5-2 3v2c0 2-1 3-3 3" />
  </Svg>
)
export const IconMessage = (p: IconProps) => (
  <Svg {...p}>
    <path d="M21 11.5a8.5 8.5 0 0 1-12.3 7.6L3 21l1.9-5.7A8.5 8.5 0 1 1 21 11.5Z" />
  </Svg>
)
export const IconSmile = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M8.5 14.5a4.5 4.5 0 0 0 7 0" />
    <path d="M9 9.5h.01M15 9.5h.01" />
  </Svg>
)
export const IconMic = (p: IconProps) => (
  <Svg {...p}>
    <rect x="9" y="3" width="6" height="11" rx="3" />
    <path d="M5 11a7 7 0 0 0 14 0" />
    <path d="M12 18v3" />
  </Svg>
)
export const IconHash = (p: IconProps) => (
  <Svg {...p}>
    <path d="M5 9h14M5 15h14M10 4 8 20M16 4l-2 16" />
  </Svg>
)
export const IconPulse = (p: IconProps) => (
  <Svg {...p}>
    <path d="M22 12h-5l-2 6-4-12-2 6H3" />
  </Svg>
)
export const IconCrown = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 7l4 4 5-6 5 6 4-4-1.5 12H4.5L3 7Z" />
  </Svg>
)
