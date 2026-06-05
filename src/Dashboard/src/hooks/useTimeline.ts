import { useInfiniteQuery } from '@tanstack/react-query'
import { timelineApi, type TimelineFilters } from '../api/timelineApi'

// Cursor-based infinite feed. nextCursor is opaque (base64 keyset); undefined ends.
export function useTimeline(filters: TimelineFilters) {
  return useInfiniteQuery({
    queryKey: ['timeline', filters],
    queryFn: ({ pageParam }) => timelineApi.get(filters, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}
