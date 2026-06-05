import { createContext, useContext } from 'react'

export interface ProfileContextValue {
  /** Discord id of the currently-open profile, or null when closed. */
  openId: string | null
  /** Open the slide-over for a given Discord user id. */
  openProfile: (discordId: string) => void
  /** Close the slide-over. */
  closeProfile: () => void
}

export const ProfileContext = createContext<ProfileContextValue | null>(null)

/** Access openProfile/closeProfile from anywhere inside <ProfileProvider>. */
export function useProfile(): ProfileContextValue {
  const ctx = useContext(ProfileContext)
  if (ctx === null) {
    throw new Error('useProfile must be used within a <ProfileProvider>')
  }
  return ctx
}
