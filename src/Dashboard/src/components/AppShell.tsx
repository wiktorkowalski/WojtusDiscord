import { NavLink } from 'react-router-dom'
import type { ReactNode } from 'react'
import {
  IconHome,
  IconPulse,
  IconChart,
  IconUsers,
  IconDatabase,
  IconBraces,
} from './icons'

interface NavItem {
  to: string
  label: string
  Icon: (p: { className?: string }) => ReactNode
  end?: boolean
}

const NAV: NavItem[] = [
  { to: '/', label: 'Overview', Icon: IconHome, end: true },
  { to: '/timeline', label: 'Timeline', Icon: IconPulse },
  { to: '/stats', label: 'Statistics', Icon: IconChart },
  { to: '/entities', label: 'Entities', Icon: IconUsers },
  { to: '/tables', label: 'Tables', Icon: IconDatabase },
  { to: '/raw', label: 'Raw events', Icon: IconBraces },
]

export default function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-screen overflow-hidden">
      <aside className="flex w-64 flex-shrink-0 flex-col border-r border-discord-border bg-discord-bg-alt/60 backdrop-blur-sm">
        <div className="flex h-16 items-center gap-3 px-5">
          <div className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-blurple to-accent-2 font-display text-lg font-extrabold text-white shadow-lg shadow-blurple/20">
            W
          </div>
          <div className="leading-tight">
            <div className="font-display text-base font-bold text-white">Wojtuś</div>
            <div className="text-[11px] uppercase tracking-[0.18em] text-discord-faint">Event Dashboard</div>
          </div>
        </div>

        <nav className="flex-1 space-y-1 px-3 py-2">
          {NAV.map(({ to, label, Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                `group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-blurple/15 text-white'
                    : 'text-discord-muted hover:bg-white/[0.03] hover:text-white'
                }`
              }
            >
              {({ isActive }) => (
                <>
                  <span
                    className={`absolute left-0 top-1/2 h-5 w-1 -translate-y-1/2 rounded-r-full bg-blurple transition-opacity ${
                      isActive ? 'opacity-100' : 'opacity-0'
                    }`}
                  />
                  <Icon className={`text-lg ${isActive ? 'text-blurple' : 'text-discord-faint group-hover:text-discord-muted'}`} />
                  {label}
                </>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="flex items-center gap-2 border-t border-discord-border px-5 py-4 text-xs text-discord-faint">
          <span className="relative flex h-2 w-2">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-accent opacity-60" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-accent" />
          </span>
          Read-only · home network
        </div>
      </aside>

      <main className="flex-1 overflow-y-auto">{children}</main>
    </div>
  )
}
