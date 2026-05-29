import type { ReactNode } from 'react'
import { formatNumber } from '../utils/format'
import Sparkline from './Sparkline'

interface StatCardProps {
  label: string
  value: number
  Icon: (p: { className?: string }) => ReactNode
  accent: string
  footer?: ReactNode
  spark?: number[]
  delay?: number
}

// Headline metric card: accent-tinted icon chip, big display figure, optional
// footnote (e.g. today/week/month) and sparkline.
export default function StatCard({ label, value, Icon, accent, footer, spark, delay = 0 }: StatCardProps) {
  return (
    <div
      className="card card-interactive animate-rise relative overflow-hidden p-5"
      style={{ animationDelay: `${delay}ms` }}
    >
      <div
        className="pointer-events-none absolute -right-6 -top-10 h-28 w-28 rounded-full opacity-20 blur-2xl"
        style={{ background: accent }}
      />
      <div className="flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-[0.14em] text-discord-faint">{label}</span>
        <span
          className="grid h-8 w-8 place-items-center rounded-lg text-base"
          style={{ background: `${accent}1f`, color: accent }}
        >
          <Icon />
        </span>
      </div>
      <div className="mt-3 flex items-end justify-between gap-2">
        <span className="stat-figure text-4xl font-bold text-white">{formatNumber(value)}</span>
        {spark && spark.length > 1 && <Sparkline values={spark} color={accent} />}
      </div>
      {footer && <div className="mt-3 text-xs text-discord-muted">{footer}</div>}
    </div>
  )
}
