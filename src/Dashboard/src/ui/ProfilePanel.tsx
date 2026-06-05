import type { CSSProperties, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { C } from '../theme'
import { profileApi } from '../api/profileApi'
import type { Profile, ProfileNameChange } from '../api/profileApi'
import { fmt, hhmm, hours, hueFromString, statusColor } from './format'
import { Avatar } from './Avatar'
import { EmojiTile } from './EmojiTile'
import { Sparkline } from './Sparkline'
import { Icon } from './Icon'

const disp: CSSProperties = {
  fontFamily: 'Bricolage Grotesque, sans-serif',
  letterSpacing: '-0.02em',
  fontWeight: 700,
}

const mono: CSSProperties = { fontFamily: 'JetBrains Mono, monospace' }

const KEYFRAMES = `@keyframes dProfileFade{from{opacity:0}to{opacity:1}}@keyframes dProfileSlide{from{transform:translateX(100%)}to{transform:translateX(0)}}`

const backdropStyle: CSSProperties = {
  position: 'absolute',
  inset: 0,
  zIndex: 60,
  background: 'rgba(10,11,13,.55)',
  backdropFilter: 'blur(4px)',
  display: 'flex',
  justifyContent: 'flex-end',
  animation: 'dProfileFade .2s ease',
}

const panelStyle: CSSProperties = {
  width: 440,
  height: '100%',
  background: C.bg1,
  borderLeft: `1px solid ${C.border}`,
  overflowY: 'auto',
  animation: 'dProfileSlide .3s cubic-bezier(.22,1,.36,1)',
}

const closeBtnStyle: CSSProperties = {
  position: 'absolute',
  top: 14,
  right: 14,
  width: 30,
  height: 30,
  borderRadius: 15,
  border: 'none',
  background: 'rgba(0,0,0,.3)',
  color: '#fff',
  fontSize: 18,
  cursor: 'pointer',
  lineHeight: 1,
}

const statTileStyle: CSSProperties = {
  background: C.card,
  border: `1px solid ${C.border}`,
  borderRadius: 12,
  padding: '11px 13px',
}

interface NameRow {
  from: string
  to: string
  at: string
}

/** Map the per-field before/after API shape into a single from→to row. */
function toNameRow(change: ProfileNameChange): NameRow {
  const globalChanged = change.globalNameBefore !== change.globalNameAfter
  const from = globalChanged ? change.globalNameBefore : change.usernameBefore
  const to = globalChanged ? change.globalNameAfter : change.usernameAfter
  return {
    from: from ?? '—',
    to: to ?? '—',
    at: change.changedAtUtc.slice(0, 10),
  }
}

function PanelShell({
  onClose,
  banner,
  children,
}: {
  onClose: () => void
  banner: string
  children: ReactNode
}) {
  return (
    <div onClick={onClose} style={backdropStyle}>
      <div onClick={(e) => e.stopPropagation()} style={panelStyle}>
        <div style={{ height: 90, background: banner, position: 'relative' }}>
          <button onClick={onClose} aria-label="Close profile" style={closeBtnStyle}>
            ×
          </button>
        </div>
        <div style={{ padding: '0 24px 24px', color: C.muted, fontSize: 14 }}>{children}</div>
      </div>
      <style>{KEYFRAMES}</style>
    </div>
  )
}

function ProfileBody({ profile }: { profile: Profile }) {
  const displayName = profile.globalName ?? profile.username
  const hue = hueFromString(profile.userDiscordId)
  const fav = profile.favoriteEmote
  const busiest = profile.busiestChannel
  const nameRows = [...profile.nameHistory]
    .sort((a, b) => (a.changedAtUtc < b.changedAtUtc ? 1 : a.changedAtUtc > b.changedAtUtc ? -1 : 0))
    .map(toNameRow)

  const stats: Array<{ label: string; value: string; color: string }> = [
    { label: 'Messages', value: fmt(profile.messageCount), color: C.blurple },
    { label: 'Memes', value: fmt(profile.memeCount), color: C.fuchsia },
    { label: 'Reacts recv', value: fmt(profile.reactionsReceivedCount), color: C.amber },
    { label: 'Voice', value: hhmm(profile.voiceMinutes), color: C.teal },
    { label: 'Online', value: hours(profile.onlineMinutes), color: C.green },
  ]

  return (
    <>
      <div
        style={{
          borderRadius: '50%',
          border: `4px solid ${C.bg1}`,
          width: 'fit-content',
          marginTop: -34,
        }}
      >
        <Avatar
          discordId={profile.userDiscordId}
          avatarHash={profile.avatarHash}
          name={displayName}
          hue={hue}
          size={80}
          status={false}
          isBot={profile.isBot}
        />
      </div>
      <h2 style={{ ...disp, fontSize: 23, color: C.text, margin: '12px 0 2px' }}>{displayName}</h2>
      <div style={{ ...mono, fontSize: 12.5, color: C.faint, display: 'flex', alignItems: 'center', gap: 8 }}>
        @{profile.username}
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, color: C.muted }}>
          <span style={{ width: 7, height: 7, borderRadius: 4, background: statusColor[profile.status] }} />
          {profile.status}
        </span>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 10, marginTop: 20 }}>
        {stats.map((s) => (
          <div key={s.label} style={statTileStyle}>
            <div style={{ ...disp, fontSize: 18, color: s.color }}>{s.value}</div>
            <div style={{ fontSize: 11, color: C.muted, marginTop: 3 }}>{s.label}</div>
          </div>
        ))}
        <div style={statTileStyle}>
          {fav !== null ? (
            <EmojiTile
              name={fav.emoteName}
              custom={fav.isCustom}
              emoteId={fav.emoteDiscordId}
              glyph={fav.emoteName}
              size={22}
            />
          ) : (
            <div style={{ ...disp, fontSize: 18, color: C.faint }}>—</div>
          )}
          <div style={{ fontSize: 11, color: C.muted, marginTop: 3 }}>Top emote</div>
        </div>
      </div>

      <div style={{ marginTop: 18, background: C.card, border: `1px solid ${C.border}`, borderRadius: 12, padding: 16 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
          <span style={{ fontSize: 12.5, color: C.muted }}>Last 14 days</span>
          {busiest !== null && (
            <span style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, color: C.muted }}>
              <Icon name="hash" size={13} color={C.blurple} />
              {busiest.channelName}
            </span>
          )}
        </div>
        <Sparkline data={profile.messagesDaily14} w={376} h={42} color={C.blurple} />
      </div>

      {nameRows.length > 0 && (
        <div style={{ marginTop: 18 }}>
          <h4
            style={{
              ...disp,
              fontSize: 12.5,
              color: C.muted,
              margin: '0 0 12px',
              textTransform: 'uppercase',
              letterSpacing: '.08em',
            }}
          >
            Name history
          </h4>
          {nameRows.map((n, i) => (
            <div key={`${n.at}-${i}`} style={{ display: 'flex', gap: 12, paddingBottom: 14 }}>
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                <span
                  style={{ width: 9, height: 9, borderRadius: 5, background: i === 0 ? C.blurple : C.faint, marginTop: 4 }}
                />
                {i < nameRows.length - 1 && (
                  <span style={{ width: 2, flex: 1, background: C.border, marginTop: 4 }} />
                )}
              </div>
              <div style={{ paddingTop: 1 }}>
                <div style={{ fontSize: 13, color: C.text, lineHeight: 1.4 }}>
                  <span style={{ ...mono, color: C.faint }}>{n.from}</span> →{' '}
                  <b style={mono}>{n.to}</b>
                </div>
                <div style={{ ...mono, fontSize: 11, color: C.faint, marginTop: 3 }}>{n.at}</div>
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  )
}

export interface ProfilePanelProps {
  discordId: string
  onClose: () => void
}

/** Slide-over profile card driven by profileApi.get(discordId). */
export function ProfilePanel({ discordId, onClose }: ProfilePanelProps) {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['profile', discordId],
    queryFn: () => profileApi.get(discordId),
  })

  const hue = hueFromString(discordId)
  const banner = `linear-gradient(120deg, hsl(${hue} 60% 42%), hsl(${(hue + 50) % 360} 62% 32%))`

  return (
    <PanelShell onClose={onClose} banner={banner}>
      {isLoading && <div style={{ paddingTop: 48, textAlign: 'center' }}>Loading profile…</div>}
      {isError && (
        <div style={{ paddingTop: 48, textAlign: 'center', color: C.red }}>
          Failed to load profile{error instanceof Error ? `: ${error.message}` : ''}
        </div>
      )}
      {!isLoading && !isError && data === undefined && (
        <div style={{ paddingTop: 48, textAlign: 'center' }}>No profile found.</div>
      )}
      {data !== undefined && <ProfileBody profile={data} />}
    </PanelShell>
  )
}
