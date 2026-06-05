import { useState } from 'react'
import { hueFromString } from './format'
import { emojiUrl } from './cdn'

export interface EmojiTileProps {
  /** Emote name — drives the gradient-tile initials and fallback hue. */
  name: string
  /** Custom (guild) emote vs. unicode glyph. */
  custom?: boolean
  /** Custom emote snowflake — enables the real CDN <img>. */
  emoteId?: string | null
  /** Unicode glyph for non-custom emotes. */
  glyph?: string
  /** Whether the custom emote is animated (.gif). */
  animated?: boolean
  size?: number
}

export function EmojiTile({
  name,
  custom = false,
  emoteId,
  glyph,
  animated = false,
  size = 26,
}: EmojiTileProps) {
  const [imgFailed, setImgFailed] = useState(false)

  // Unicode emoji — just render the glyph.
  if (!custom) {
    return (
      <span style={{ fontSize: size * 0.82, lineHeight: 1, display: 'inline-block' }}>
        {glyph}
      </span>
    )
  }

  const imgUrl = emojiUrl(emoteId, animated)
  if (imgUrl !== null && !imgFailed) {
    return (
      <img
        src={imgUrl}
        alt={`:${name}:`}
        title={`:${name}: (custom)`}
        onError={() => setImgFailed(true)}
        style={{
          width: size,
          height: size,
          borderRadius: size * 0.26,
          flexShrink: 0,
          objectFit: 'contain',
        }}
      />
    )
  }

  // Gradient-tile fallback (mock look) when no/failed image.
  const h = hueFromString(name)
  return (
    <div
      title={`:${name}: (custom)`}
      style={{
        width: size,
        height: size,
        borderRadius: size * 0.26,
        flexShrink: 0,
        background: `linear-gradient(140deg, hsl(${h} 70% 56%), hsl(${(h + 50) % 360} 72% 42%))`,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: '#fff',
        fontWeight: 800,
        fontSize: size * 0.42,
        letterSpacing: '-0.03em',
        fontFamily: 'JetBrains Mono, monospace',
        boxShadow: 'inset 0 1px 0 rgba(255,255,255,.25)',
        textShadow: '0 1px 1px rgba(0,0,0,.3)',
      }}
    >
      {name.slice(0, 2).toUpperCase()}
    </div>
  )
}
