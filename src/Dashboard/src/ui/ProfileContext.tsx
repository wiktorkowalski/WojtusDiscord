import { useCallback, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { ProfileContext } from './profileCtx'
import type { ProfileContextValue } from './profileCtx'
import { ProfilePanel } from './ProfilePanel'

/**
 * Provides openProfile/closeProfile to the whole tree and renders the
 * <ProfilePanel> slide-over when an id is open. Mount this INSIDE the
 * position:relative app-root so the absolute overlay covers the app, not the
 * viewport. Renders {children} then the panel as siblings — no extra wrapper.
 */
export function ProfileProvider({ children }: { children: ReactNode }) {
  const [openId, setOpenId] = useState<string | null>(null)

  const openProfile = useCallback((discordId: string) => {
    if (discordId) setOpenId(discordId)
  }, [])
  const closeProfile = useCallback(() => setOpenId(null), [])

  const value = useMemo<ProfileContextValue>(
    () => ({ openId, openProfile, closeProfile }),
    [openId, openProfile, closeProfile],
  )

  return (
    <ProfileContext.Provider value={value}>
      {children}
      {openId !== null && <ProfilePanel discordId={openId} onClose={closeProfile} />}
    </ProfileContext.Provider>
  )
}
