// Login state from the backend gate (#339). /auth/* is outside /api, so plain fetch.
export interface Me {
  id: string | null
  username: string | null
  avatarHash: string | null
  isMember: boolean
  isAdmin: boolean
}

// Fired by fetchApi on 401/403 so the AuthGate re-reads /auth/me.
export const AUTH_CHANGED = 'wojtus:auth-changed'

export const authApi = {
  // null = not logged in.
  me: async (): Promise<Me | null> => {
    const res = await fetch('/auth/me')
    if (res.status === 401) return null
    if (!res.ok) throw new Error(`/auth/me ${res.status}`)
    return res.json() as Promise<Me>
  },
  logout: async (): Promise<void> => {
    await fetch('/auth/logout', { method: 'POST' })
    window.location.assign('/')
  },
  loginUrl: () => `/auth/login?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search)}`,
}
