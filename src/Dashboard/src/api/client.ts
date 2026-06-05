// Thin fetch wrapper. All requests go through the .NET backend under /api
// (proxied to :5099 in dev by vite.config.ts; same-origin in production).
const BASE_URL = '/api'

export async function fetchApi<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${endpoint}`, {
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
    ...options,
  })

  if (!res.ok) {
    const body = await res.text().catch(() => '')
    throw new Error(`API ${res.status} ${res.statusText}${body ? `: ${body}` : ''}`)
  }

  return res.json() as Promise<T>
}

// Build a query string from a params object, skipping null/undefined/''.
export function toQuery(params: Record<string, unknown>): string {
  const q = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue
    q.set(key, String(value))
  }
  const s = q.toString()
  return s ? `?${s}` : ''
}
