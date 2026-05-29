import { NavLink } from 'react-router-dom'
import type { ReactNode } from 'react'

interface NavItem {
  to: string
  label: string
  icon: string
  end?: boolean
}

// Ordered to match the dashboard's primary views. Timeline is the landing page.
const NAV: NavItem[] = [
  { to: '/', label: 'Timeline', icon: '🕓', end: true },
  { to: '/stats', label: 'Statistics', icon: '📊' },
  { to: '/entities', label: 'Entities', icon: '👥' },
  { to: '/tables', label: 'Tables', icon: '🗄️' },
  { to: '/raw', label: 'Raw events', icon: '🧬' },
]

export default function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-screen overflow-hidden">
      <aside className="flex w-60 flex-shrink-0 flex-col bg-discord-bg-alt border-r border-discord-border">
        <div className="flex h-12 items-center gap-2 border-b border-discord-border px-4">
          <span className="text-lg">🤖</span>
          <h1 className="text-base font-semibold text-white">Wojtuś Events</h1>
        </div>
        <nav className="flex-1 space-y-0.5 overflow-y-auto p-2">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded px-3 py-2 text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-blurple text-white'
                    : 'text-discord-muted hover:bg-discord-bg-card hover:text-white'
                }`
              }
            >
              <span className="text-base">{item.icon}</span>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-discord-border px-4 py-3 text-xs text-discord-faint">
          Read-only · home network
        </div>
      </aside>
      <main className="flex-1 overflow-y-auto bg-discord-bg">{children}</main>
    </div>
  )
}
