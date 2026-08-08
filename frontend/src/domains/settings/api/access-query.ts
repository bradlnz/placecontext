import { queryOptions } from '@tanstack/react-query'

import { fetchAccessSettings, fetchMemberPermissions } from './access-api'

export const accessSettingsQueryOptions = queryOptions({
  queryKey: ['settings', 'access'],
  queryFn: ({ signal }) => fetchAccessSettings(signal),
  staleTime: 30_000,
})

export function memberPermissionsQueryOptions(userId: string | null) {
  return queryOptions({
    queryKey: ['settings', 'access', 'members', userId, 'permissions'],
    queryFn: ({ signal }: { signal: AbortSignal }) => {
      if (userId === null) throw new Error('A member must be selected.')
      return fetchMemberPermissions(userId, signal)
    },
    enabled: userId !== null,
    staleTime: 15_000,
  })
}
