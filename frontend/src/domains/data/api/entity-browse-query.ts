import { queryOptions } from '@tanstack/react-query'
import { fetchEntityBrowse } from './entity-browse-api'

export const entityBrowseQueryOptions = (
  projectId: string,
  entityName: string,
  search: string,
  page: number,
) =>
  queryOptions({
    queryKey: ['entity-browse', projectId, entityName, search, page],
    queryFn: ({ signal }) => fetchEntityBrowse(projectId, entityName, search, page, signal),
  })
