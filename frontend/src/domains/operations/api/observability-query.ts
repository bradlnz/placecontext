import { queryOptions } from '@tanstack/react-query'

import { fetchObservabilityPage, fetchObservabilityRunDetails } from './observability-api'

export const observabilityQueryKeys = { page: ['observability-page'] as const }

export const observabilityPageQueryOptions = queryOptions({
  queryKey: observabilityQueryKeys.page,
  queryFn: ({ signal }) => fetchObservabilityPage(signal),
})

export const observabilityRunDetailsQueryOptions = (runId: string, jobId: string) =>
  queryOptions({
    queryKey: ['observability-run', runId, jobId],
    queryFn: ({ signal }) => fetchObservabilityRunDetails(runId, jobId, signal),
  })
