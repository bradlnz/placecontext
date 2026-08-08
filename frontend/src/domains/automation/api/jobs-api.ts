import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type {
  Job,
  JobCodeFile,
  JobCodePageModel,
  JobRequestBody,
  JobRun,
  JobRunDetail,
  JobsPageModel,
  RunJobCodeResult,
} from '../model/jobs'
import { job, jobCodePage, jobRunDetail, jobRuns, jobsPage, runJobCodeResult } from './jobs-schemas'
const root = (projectId: string) => `/api/v1/projects/${encodeURIComponent(projectId)}/job-page`
const jobPath = (projectId: string, jobId: string) =>
  `${root(projectId)}/jobs/${encodeURIComponent(jobId)}`
export const fetchJobs = (projectId: string, signal: AbortSignal): Promise<JobsPageModel> =>
  getJson({ path: root(projectId), schema: jobsPage, signal })
export const createJob = (
  projectId: string,
  body: JobRequestBody,
  signal: AbortSignal,
): Promise<Job> => postJson({ path: `${root(projectId)}/jobs`, body, schema: job, signal })
export const updateJob = (
  projectId: string,
  jobId: string,
  body: JobRequestBody,
  signal: AbortSignal,
): Promise<Job> => putJson({ path: jobPath(projectId, jobId), body, schema: job, signal })
export const deleteJob = (projectId: string, jobId: string, signal: AbortSignal): Promise<void> =>
  deleteRequest(jobPath(projectId, jobId), signal)
export const runJob = (
  projectId: string,
  jobId: string,
  inputPayload: string | null,
  signal: AbortSignal,
): Promise<JobRunDetail> =>
  postJson({
    path: `${jobPath(projectId, jobId)}/runs`,
    body: { inputPayload },
    schema: jobRunDetail,
    signal,
  })
export const fetchJobRuns = (
  projectId: string,
  jobId: string,
  signal: AbortSignal,
): Promise<JobRun[]> =>
  getJson({
    path: `${jobPath(projectId, jobId)}/runs`,
    schema: jobRuns,
    signal,
  })
export const fetchJobCode = (
  projectId: string,
  jobId: string,
  signal: AbortSignal,
): Promise<JobCodePageModel> =>
  getJson({
    path: `${jobPath(projectId, jobId)}/code-page`,
    schema: jobCodePage,
    signal,
  })
export const saveJobCode = (
  projectId: string,
  jobId: string,
  runtimeId: string,
  entrypoint: string | null,
  files: JobCodeFile[],
  signal: AbortSignal,
): Promise<Job> =>
  putJson({
    path: `${jobPath(projectId, jobId)}/code-page`,
    body: { runtimeId, entrypoint, files },
    schema: job,
    signal,
  })
export const runJobCode = (
  projectId: string,
  jobId: string,
  runtimeId: string,
  entrypoint: string | null,
  files: JobCodeFile[],
  signal: AbortSignal,
): Promise<RunJobCodeResult> =>
  postJson({
    path: `${jobPath(projectId, jobId)}/code-page/run`,
    body: { runtimeId, entrypoint, files },
    schema: runJobCodeResult,
    signal,
  })
