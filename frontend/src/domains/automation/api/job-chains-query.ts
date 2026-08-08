import { queryOptions } from '@tanstack/react-query'
import { fetchChainRuns, fetchChains } from './job-chains-api'
export const chainsQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: ['automation', 'chains', projectId] as const,
    queryFn: async ({ signal }) => fetchChains(projectId, signal),
  })
export const chainRunsQueryOptions = (projectId: string, chainId: string) =>
  queryOptions({
    queryKey: ['automation', 'chains', projectId, chainId, 'runs'] as const,
    queryFn: async ({ signal }) => fetchChainRuns(projectId, chainId, signal),
  })
