import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type {
  JobTestBlock,
  JobTestCodePageModel,
  JobTestsPageModel,
  SaveJobTestBlockBody,
  UpdateJobTestCodeBody,
} from '../model/job-tests'
import { jobTestBlock, jobTestCodePage, jobTestsPage } from './job-tests-schemas'

const root = (projectId: string) => `/api/jobs/projects/${encodeURIComponent(projectId)}/test-page`
const testPath = (projectId: string, testId: string) =>
  `${root(projectId)}/tests/${encodeURIComponent(testId)}`
export const fetchJobTests = (projectId: string, signal: AbortSignal): Promise<JobTestsPageModel> =>
  getJson({ path: root(projectId), schema: jobTestsPage, signal })
export const createJobTest = (
  projectId: string,
  body: SaveJobTestBlockBody,
  signal: AbortSignal,
): Promise<JobTestBlock> =>
  postJson({
    path: `${root(projectId)}/tests`,
    body,
    schema: jobTestBlock,
    signal,
  })
export const updateJobTest = (
  projectId: string,
  testId: string,
  body: SaveJobTestBlockBody,
  signal: AbortSignal,
): Promise<JobTestBlock> =>
  putJson({
    path: testPath(projectId, testId),
    body,
    schema: jobTestBlock,
    signal,
  })
export const runJobTest = (
  projectId: string,
  testId: string,
  signal: AbortSignal,
): Promise<JobTestBlock> =>
  postJson({
    path: `${testPath(projectId, testId)}/run`,
    body: {},
    schema: jobTestBlock,
    signal,
  })
export const deleteJobTest = (
  projectId: string,
  testId: string,
  signal: AbortSignal,
): Promise<void> => deleteRequest(testPath(projectId, testId), signal)
export const fetchJobTestCode = (
  projectId: string,
  testId: string,
  signal: AbortSignal,
): Promise<JobTestCodePageModel> =>
  getJson({
    path: `${testPath(projectId, testId)}/code-page`,
    schema: jobTestCodePage,
    signal,
  })
export const saveJobTestCode = (
  projectId: string,
  testId: string,
  body: UpdateJobTestCodeBody,
  signal: AbortSignal,
): Promise<JobTestBlock> =>
  putJson({
    path: `${testPath(projectId, testId)}/code-page`,
    body,
    schema: jobTestBlock,
    signal,
  })
export const runJobTestCode = (
  projectId: string,
  testId: string,
  body: UpdateJobTestCodeBody,
  signal: AbortSignal,
): Promise<JobTestBlock> =>
  postJson({
    path: `${testPath(projectId, testId)}/code-page/run`,
    body,
    schema: jobTestBlock,
    signal,
  })
