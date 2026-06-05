import { useState } from 'react'
import type { CSSProperties } from 'react'
import { C } from '../theme'
import { initials, hueFromString, statusColor } from './format'
import type { PresenceStatus } from './format'
import { userAvatarUrl } from './cdn'

export interface AvatarProps {
  /** Discord snowflake — enables the real CDN image when an avatarHash is given. */
  discordId?: string
  /** Avatar hash. When present (with discordId) a CDN <img> is used, else the gradient tile. */
  avatarHash?: string | null
  /** Display name — drives initials and the fallback gradient hue. */
  name: string
  /** Override the gradient hue; defaults to a stable hash of name/id. */
  hue?: number
  size?: number
  /** Rounded-square instead of circle. */
  square?: boolean
  /** Draw the ring halo (used for stacked top-bar avatars). */
  ring?: boolean
  /** Presence dot — pass a status string to show it, or false to suppress (default). */
  status?: PresenceStatus | false
  /** Render the BOT badge. */
  isBot?: boolean
  /** Dim to 55% opacity. */
  dim?: boolean
}

export function Avatar({
  discordId,
  avatarHash,
  name,
  hue,
  size = 36,
  square = false,
  ring = false,
  status = false,
  isBot = false,
  dim = false,
}: AvatarProps) {
  const [imgFailed, setImgFailed] = useState(false)

  const h = hue ?? hueFromString(discordId ?? name)
  const grad = `linear-gradient(140deg, hsl(${h} 62% 56%), hsl(${(h + 45) % 360} 64% 44%))`
  const dot = Math.max(8, Math.round(size * 0.28))
  // Request 2x for retina; snapped to a valid power-of-two inside the CDN builder.
  const imgUrl = userAvatarUrl(discordId, avatarHash, size * 2)
  const showImg = imgUrl !== null && !imgFailed

  const tileStyle: CSSProperties = {
    width: size,
    height: size,
    borderRadius: square ? size * 0.3 : '50%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: '#fff',
    fontWeight: 700,
    fontSize: size * 0.38,
    letterSpacing: '-0.02em',
    fontFamily: 'Hanken Grotesk, sans-serif',
    userSelect: 'none',
    boxShadow: ring
      ? `0 0 0 2px ${C.bg1}, 0 0 0 4px hsl(${h} 60% 55% / .5)`
      : 'inset 0 1px 0 rgba(255,255,255,.18)',
    opacity: dim ? 0.55 : 1,
    filter: isBot ? 'saturate(.7)' : 'none',
    objectFit: 'cover',
  }

  return (
    <div style={{ position: 'relative', width: size, height: size, flexShrink: 0 }}>
      {showImg ? (
        <img
          src={imgUrl}
          alt={name}
          onError={() => setImgFailed(true)}
          style={{ ...tileStyle, background: C.bg2 }}
        />
      ) : (
        <div style={{ ...tileStyle, background: grad }}>{initials(name)}</div>
      )}
      {isBot && (
        <div
          style={{
            position: 'absolute',
            bottom: -2,
            left: -2,
            background: C.blurple,
            color: '#fff',
            fontSize: size * 0.2,
            fontWeight: 700,
            padding: '0 3px',
            borderRadius: 3,
            fontFamily: 'JetBrains Mono, monospace',
            lineHeight: 1.5,
            border: `1.5px solid ${C.bg1}`,
          }}
        >
          BOT
        </div>
      )}
      {typeof status === 'string' && !isBot && (
        <div
          style={{
            position: 'absolute',
            bottom: -1,
            right: -1,
            width: dot,
            height: dot,
            borderRadius: '50%',
            background: statusColor[status],
            border: `2.5px solid ${C.bg1}`,
            boxSizing: 'content-box',
          }}
        />
      )}
    </div>
  )
}
