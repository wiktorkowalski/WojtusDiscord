import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { C } from '../theme'
import { authApi, AUTH_CHANGED } from '../api/authApi'
import { Avatar } from './Avatar'

const button = {
  padding: '11px 20px',
  borderRadius: 10,
  border: 'none',
  background: C.blurple,
  color: '#fff',
  fontSize: 14.5,
  fontWeight: 600,
  cursor: 'pointer',
  fontFamily: 'inherit',
  textDecoration: 'none',
} as const

function Screen({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 18,
        padding: 16,
        textAlign: 'center',
        color: C.text,
      }}
    >
      {children}
    </div>
  )
}

// #339: the shell is public, the data is not. Shows the login or no-access screen instead of the app.
export function AuthGate({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const { data: me, isPending, isError } = useQuery({ queryKey: ['me'], queryFn: authApi.me, staleTime: 60_000 })

  useEffect(() => {
    const onChange = () => void queryClient.invalidateQueries({ queryKey: ['me'] })
    window.addEventListener(AUTH_CHANGED, onChange)
    return () => window.removeEventListener(AUTH_CHANGED, onChange)
  }, [queryClient])

  if (isPending) return null
  if (isError) return <Screen>Nie udało się sprawdzić logowania. Odśwież stronę.</Screen>

  if (me === null)
    return (
      <Screen>
        <div style={{ fontSize: 22, fontWeight: 700 }}>Wojtuś Dashboard</div>
        <a href={authApi.loginUrl()} style={button}>
          Zaloguj przez Discord
        </a>
      </Screen>
    )

  if (!me.isMember)
    return (
      <Screen>
        <div style={{ fontSize: 22, fontWeight: 700 }}>Brak dostępu</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, color: C.muted }}>
          <Avatar discordId={me.id ?? undefined} avatarHash={me.avatarHash} name={me.username ?? '?'} size={32} />
          Zalogowano jako <b style={{ color: C.text }}>{me.username}</b>, ale nie jesteś na serwerze.
        </div>
        <button onClick={() => void authApi.logout()} style={button}>
          Wyloguj
        </button>
      </Screen>
    )

  return <>{children}</>
}
