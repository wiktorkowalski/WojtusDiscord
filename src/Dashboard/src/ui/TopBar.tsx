import { useState } from 'react'
import type { CSSProperties } from 'react'
import { NavLink } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { C } from '../theme'
import { guildApi } from '../api/guildApi'
import { Avatar } from './Avatar'
import { Icon } from './Icon'
import type { IconName } from './Icon'
import { useProfile } from './profileCtx'

const disp: CSSProperties = {
  fontFamily: 'Bricolage Grotesque, sans-serif',
  letterSpacing: '-0.02em',
  fontWeight: 700,
}

const label: CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  letterSpacing: '.12em',
  textTransform: 'uppercase',
  color: C.faint,
}

const KEYFRAMES = `@keyframes dTopBarSpin{to{transform:rotate(360deg)}}`

interface NavSpec {
  to: string
  name: string
  icon: IconName
  end?: boolean
}

const NAV: NavSpec[] = [
  { to: '/', name: 'Overview', icon: 'home', end: true },
  { to: '/timeline', name: 'Timeline', icon: 'timeline' },
  { to: '/stats', name: 'Statistics', icon: 'stats' },
  { to: '/entities', name: 'Entities', icon: 'entities' },
  { to: '/tables', name: 'Tables', icon: 'tables' },
  { to: '/raw', name: 'Raw events', icon: 'raw' },
]

function navLinkStyle(isActive: boolean): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: 7,
    padding: '8px 13px',
    borderRadius: 9,
    textDecoration: 'none',
    cursor: 'pointer',
    background: isActive ? C.bg2 : 'transparent',
    color: isActive ? C.text : C.muted,
    fontWeight: isActive ? 600 : 500,
    fontSize: 13.5,
    fontFamily: 'inherit',
    boxShadow: isActive ? `inset 0 0 0 1px ${C.border}` : 'none',
    transition: 'background .15s, color .15s',
  }
}

export function TopBar() {
  const { openProfile } = useProfile()
  const queryClient = useQueryClient()
  const [spin, setSpin] = useState(false)

  const { data: guild } = useQuery({ queryKey: ['guild'], queryFn: guildApi.get })

  const online = guild?.online ?? []
  const onlineShown = online.slice(0, 6)

  const refresh = () => {
    void queryClient.invalidateQueries()
    setSpin(true)
    setTimeout(() => setSpin(false), 700)
  }

  return (
    <header
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 22,
        padding: '16px 32px',
        borderBottom: `1px solid ${C.border}`,
        background: 'rgba(19,20,23,.6)',
        backdropFilter: 'blur(8px)',
        position: 'sticky',
        top: 0,
        zIndex: 30,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexShrink: 0 }}>
        <div
          style={{
            width: 38,
            height: 38,
            borderRadius: 11,
            background: `linear-gradient(140deg, ${C.blurple}, ${C.fuchsia})`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            ...disp,
            fontSize: 19,
            color: '#fff',
          }}
        >
          W
        </div>
        <div>
          <div style={{ ...disp, fontSize: 16, lineHeight: 1 }}>{guild?.name ?? 'Wojtuś'}</div>
          <div style={{ ...label, fontSize: 9, marginTop: 3 }}>Event Dashboard</div>
        </div>
      </div>

      <nav style={{ display: 'flex', gap: 2, marginLeft: 12 }}>
        {NAV.map(({ to, name, icon, end }) => (
          <NavLink key={to} to={to} end={end} style={({ isActive }) => navLinkStyle(isActive)}>
            {({ isActive }) => (
              <>
                <Icon name={icon} size={15} color={isActive ? C.blurple : C.faint} />
                {name}
              </>
            )}
          </NavLink>
        ))}
      </nav>

      <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center' }}>
          {onlineShown.map((u, i) => (
            <button
              key={u.userDiscordId}
              onClick={() => openProfile(u.userDiscordId)}
              aria-label={`Open profile for ${u.username ?? u.userDiscordId}`}
              style={{
                marginLeft: i ? -8 : 0,
                background: 'none',
                border: 'none',
                padding: 0,
                cursor: 'pointer',
                zIndex: 10 - i,
              }}
            >
              <Avatar
                discordId={u.userDiscordId}
                avatarHash={u.avatarHash}
                name={u.username ?? u.userDiscordId}
                size={28}
                ring
                status={false}
              />
            </button>
          ))}
          <span style={{ marginLeft: 11, fontSize: 12.5, color: C.muted, whiteSpace: 'nowrap' }}>
            <b style={{ color: C.text }}>{online.length}</b> online
          </span>
        </div>

        <button
          onClick={refresh}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 7,
            padding: '8px 13px',
            borderRadius: 9,
            border: `1px solid ${C.border}`,
            background: C.card,
            color: C.muted,
            fontSize: 13,
            fontWeight: 600,
            cursor: 'pointer',
            fontFamily: 'inherit',
          }}
        >
          <span style={{ display: 'inline-flex', animation: spin ? 'dTopBarSpin .7s linear' : 'none' }}>
            <Icon name="refresh" size={14} />
          </span>
          refresh
        </button>
      </div>
      <style>{KEYFRAMES}</style>
    </header>
  )
}
