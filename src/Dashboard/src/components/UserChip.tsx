// Compact user reference: name + optional snowflake id.
export default function UserChip({ name, id }: { name: string; id?: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-blurple text-[10px] font-bold text-white">
        {name.slice(0, 1).toUpperCase()}
      </span>
      <span className="text-discord-text">{name}</span>
      {id && <span className="mono text-xs text-discord-faint">{id}</span>}
    </span>
  )
}
