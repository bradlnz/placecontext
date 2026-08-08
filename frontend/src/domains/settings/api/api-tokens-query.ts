import { queryOptions } from '@tanstack/react-query'

import { fetchApiTokens } from './api-tokens-api'

export const apiTokensQueryOptions = queryOptions({
  queryKey: ['settings', 'api-tokens'],
  queryFn: async ({ signal }) => fetchApiTokens(signal),
})
