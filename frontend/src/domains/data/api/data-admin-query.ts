import { queryOptions } from '@tanstack/react-query'
import { fetchProjectDataAdmin } from './data-admin-api'

export const dataAdminQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: ['project-data-admin', projectId],
    queryFn: ({ signal }) => fetchProjectDataAdmin(projectId, signal),
  })
