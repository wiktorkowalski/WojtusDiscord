import { fetchApi } from './client'

export interface SpotifyNowPlaying {
  userDiscordId: string
  username: string | null
  avatarHash: string | null
  track: string | null
  artist: string | null
  album: string | null
  albumArtUrl: string | null
  startedAtUtc: string | null
  endsAtUtc: string | null
}

export interface SpotifyTrack {
  track: string
  artist: string | null
  albumArtUrl: string | null
  plays: number
}

export interface Spotify {
  nowPlaying: SpotifyNowPlaying[]
  topTracks: SpotifyTrack[]
}

export const spotifyApi = {
  get: () => fetchApi<Spotify>('/stats/spotify'),
}
