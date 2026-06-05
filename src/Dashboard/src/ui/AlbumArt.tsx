import { useState } from 'react'
import { Icon } from './Icon'

export interface AlbumArtProps {
  /** Cover art URL — falls back to the gradient music placeholder when absent/failed. */
  url?: string | null
  hueA?: number
  hueB?: number
  size?: number
  radius?: number
}

export function AlbumArt({ url, hueA = 280, hueB = 320, size = 56, radius = 8 }: AlbumArtProps) {
  const [imgFailed, setImgFailed] = useState(false)

  if (url && !imgFailed) {
    return (
      <img
        src={url}
        alt="Album art"
        onError={() => setImgFailed(true)}
        style={{
          width: size,
          height: size,
          borderRadius: radius,
          flexShrink: 0,
          objectFit: 'cover',
          boxShadow: 'inset 0 0 0 1px rgba(255,255,255,.12), 0 4px 14px -6px rgba(0,0,0,.6)',
        }}
      />
    )
  }

  return (
    <div
      style={{
        width: size,
        height: size,
        borderRadius: radius,
        flexShrink: 0,
        position: 'relative',
        overflow: 'hidden',
        background: `linear-gradient(135deg, hsl(${hueA} 65% 50%), hsl(${hueB} 70% 38%))`,
        boxShadow: 'inset 0 0 0 1px rgba(255,255,255,.12), 0 4px 14px -6px rgba(0,0,0,.6)',
      }}
    >
      <div
        style={{
          position: 'absolute',
          inset: 0,
          background: 'radial-gradient(circle at 70% 25%, rgba(255,255,255,.35), transparent 55%)',
        }}
      />
      <div
        style={{
          position: 'absolute',
          inset: 0,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          opacity: 0.5,
        }}
      >
        <Icon name="music" size={size * 0.34} color="#fff" />
      </div>
    </div>
  )
}
