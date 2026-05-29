// Compact channel reference styled like a Discord channel mention.
export default function ChannelChip({ name, id }: { name: string; id?: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className="rounded bg-discord-bg-dark px-1.5 py-0.5 text-discord-text">#{name}</span>
      {id && <span className="mono text-xs text-discord-faint">{id}</span>}
    </span>
  )
}
