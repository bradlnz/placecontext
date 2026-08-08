import { queryOptions } from '@tanstack/react-query'
import { fetchJobCode, fetchJobRuns, fetchJobs } from './jobs-api'
export const jobsQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: ['automation', 'jobs', projectId] as const,
    queryFn: async ({ signal }) => fetchJobs(projectId, signal),
  })
export const jobRunsQueryOptions = (projectId: string, jobId: string) =>
  queryOptions({
    queryKey: ['automation', 'jobs', projectId, jobId, 'runs'] as const,
    queryFn: async ({ signal }) => fetchJobRuns(projectId, jobId, signal),
  })
export const jobCodeQueryOptions = (projectId: string, jobId: string) =>
  queryOptions({
    queryKey: ['automation', 'jobs', projectId, jobId, 'code'] as const,
    queryFn: async ({ signal }) => fetchJobCode(projectId, jobId, signal),
  })
