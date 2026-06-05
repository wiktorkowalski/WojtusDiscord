// Discord CDN URL builders. All null-safe — return null when the inputs can't
// produce a valid URL, so callers can fall back to gradient placeholders.
const CDN = 'https://cdn.discordapp.com'

/** Snap a requested pixel size to a power-of-two in Discord's documented 16–4096 range. */
function snapSize(px: number): number {
  const pow2 = 2 ** Math.ceil(Math.log2(Math.max(1, px)))
  return Math.min(4096, Math.max(16, pow2))
}

/**
 * User avatar URL. Animated (.gif) when the hash starts with "a_".
 * Returns null when id or hash is missing.
 */
export function userAvatarUrl(
  discordId: string | null | undefined,
  avatarHash: string | null | undefined,
  size = 128,
): string | null {
  if (!discordId || !avatarHash) return null
  const ext = avatarHash.startsWith('a_') ? 'gif' : 'png'
  return `${CDN}/avatars/${discordId}/${avatarHash}.${ext}?size=${snapSize(size)}`
}

/**
 * Default (embed) avatar for a user with no custom avatar.
 * Index is (snowflake >> 22) % 6 — ids exceed Number range, so use BigInt.
 */
export function defaultAvatarUrl(discordId: string | null | undefined): string | null {
  if (!discordId) return null
  let idx: bigint
  try {
    idx = (BigInt(discordId) >> 22n) % 6n
  } catch {
    return null
  }
  return `${CDN}/embed/avatars/${idx.toString()}.png`
}

/** Guild icon URL. Animated (.gif) when the hash starts with "a_". */
export function guildIconUrl(
  guildId: string | null | undefined,
  iconHash: string | null | undefined,
  size = 128,
): string | null {
  if (!guildId || !iconHash) return null
  const ext = iconHash.startsWith('a_') ? 'gif' : 'png'
  return `${CDN}/icons/${guildId}/${iconHash}.${ext}?size=${snapSize(size)}`
}

/** Custom emoji URL. Animated (.gif) when `animated` is true. */
export function emojiUrl(
  emoteId: string | null | undefined,
  animated = false,
): string | null {
  if (!emoteId) return null
  return `${CDN}/emojis/${emoteId}.${animated ? 'gif' : 'png'}`
}
