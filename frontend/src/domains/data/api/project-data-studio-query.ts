import { queryOptions } from '@tanstack/react-query'
import { fetchProjectDataStudio } from './project-data-studio-api'

export const projectDataStudioQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: ['project-data-studio', projectId],
    queryFn: ({ signal }) => fetchProjectDataStudio(projectId, signal),
  })
