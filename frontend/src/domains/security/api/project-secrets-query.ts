import { queryOptions } from '@tanstack/react-query'

import { fetchProjectSecrets } from './project-secrets-api'

export function projectSecretsQueryOptions(projectId: string) {
  return queryOptions({
    queryKey: ['security', 'project-secrets', projectId] as const,
    queryFn: async ({ signal }) => fetchProjectSecrets(projectId, signal),
  })
}
