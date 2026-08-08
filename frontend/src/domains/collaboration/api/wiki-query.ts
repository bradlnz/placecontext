import { queryOptions } from '@tanstack/react-query'

import { fetchWikiContext } from './wiki-api'

export function wikiContextQueryOptions(slug: string | undefined) {
  return queryOptions({
    queryKey: ['collaboration', 'wiki', slug ?? null] as const,
    queryFn: async ({ signal }) => fetchWikiContext(slug, signal),
    staleTime: 5 * 60_000,
  })
}
