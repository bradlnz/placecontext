import { getJson } from '../../../shared/api/http-client'
import type { WikiContext } from '../model/wiki'
import { wikiContextSchema } from './wiki-schemas'

export async function fetchWikiContext(
  slug: string | undefined,
  signal: AbortSignal,
): Promise<WikiContext> {
  const query = slug === undefined ? '' : `?slug=${encodeURIComponent(slug)}`
  return getJson({
    path: `/api/v1/wiki${query}`,
    schema: wikiContextSchema,
    signal,
  })
}
