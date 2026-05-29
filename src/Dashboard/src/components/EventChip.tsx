// Colour-codes an event type by family so the feed scans quickly.
const FAMILIES: { test: RegExp; classes: string }[] = [
  { test: /^Message/, classes: 'bg-blurple/20 text-blurple' },
  { test: /Reaction|Poll/, classes: 'bg-discord-yellow/20 text-discord-yellow' },
  { test: /Voice|Stage/, classes: 'bg-discord-green/20 text-discord-green' },
  { test: /Presence|Activity/, classes: 'bg-discord-faint/20 text-discord-faint' },
  { test: /Typing/, classes: 'bg-discord-faint/10 text-discord-faint' },
  { test: /Member|Ban|Guild|Role|Integration|Invite/, classes: 'bg-[#eb459e]/20 text-[#eb459e]' },
  { test: /Channel|Thread|Webhook/, classes: 'bg-[#00a8fc]/20 text-[#00a8fc]' },
  { test: /AutoMod|Audit|Scheduled|Emoji|Sticker/, classes: 'bg-discord-red/20 text-discord-red' },
]

function classesFor(eventType: string): string {
  return FAMILIES.find((f) => f.test.test(eventType))?.classes ?? 'bg-discord-bg-dark text-discord-muted'
}

export default function EventChip({ eventType }: { eventType: string }) {
  return (
    <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${classesFor(eventType)}`}>
      {eventType}
    </span>
  )
}
