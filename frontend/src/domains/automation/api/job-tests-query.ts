import { queryOptions } from '@tanstack/react-query'
import { fetchJobTestCode, fetchJobTests } from './job-tests-api'

export const jobTestsQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: ['automation', 'tests', projectId] as const,
    queryFn: async ({ signal }) => fetchJobTests(projectId, signal),
  })
export const jobTestCodeQueryOptions = (projectId: string, testId: string) =>
  queryOptions({
    queryKey: ['automation', 'tests', projectId, testId, 'code'] as const,
    queryFn: async ({ signal }) => fetchJobTestCode(projectId, testId, signal),
  })
