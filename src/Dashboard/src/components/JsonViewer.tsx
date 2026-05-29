import { useState } from 'react'

// Renders a value (object, array, or JSON string) as pretty-printed, scrollable
// JSON with a copy button. Used for raw event payloads and explorer row detail.
export default function JsonViewer({ value }: { value: unknown }) {
  const [copied, setCopied] = useState(false)

  const normalized = normalize(value)
  const text = JSON.stringify(normalized, null, 2)

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // clipboard may be unavailable (insecure context); ignore
    }
  }

  return (
    <div className="relative">
      <button
        onClick={copy}
        className="absolute right-2 top-2 rounded bg-discord-bg-dark px-2 py-1 text-xs text-discord-muted hover:text-white"
      >
        {copied ? 'copied' : 'copy'}
      </button>
      <pre className="mono max-h-[60vh] overflow-auto rounded-lg bg-discord-bg-dark p-4 text-xs leading-relaxed text-discord-text">
        {text}
      </pre>
    </div>
  )
}

// Parse JSON strings so payloads stored as text render as structured trees.
function normalize(value: unknown): unknown {
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
      try {
        return JSON.parse(trimmed)
      } catch {
        return value
      }
    }
  }
  return value
}
