import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type { ChainRun, JobChain, JobChainsPageModel, SaveChainBody } from '../model/job-chains'
import { chain, chainRun, chainRuns, chainsPage } from './job-chains-schemas'
const root = (projectId: string) => `/api/jobs/projects/${encodeURIComponent(projectId)}/chain-page`
const chainPath = (projectId: string, chainId: string) =>
  `${root(projectId)}/chains/${encodeURIComponent(chainId)}`
export const fetchChains = (projectId: string, signal: AbortSignal): Promise<JobChainsPageModel> =>
  getJson({ path: root(projectId), schema: chainsPage, signal })
export const createChain = (
  projectId: string,
  body: SaveChainBody,
  signal: AbortSignal,
): Promise<JobChain> => postJson({ path: `${root(projectId)}/chains`, body, schema: chain, signal })
export const updateChain = (
  projectId: string,
  chainId: string,
  body: SaveChainBody,
  signal: AbortSignal,
): Promise<JobChain> =>
  putJson({ path: chainPath(projectId, chainId), body, schema: chain, signal })
export const deleteChain = (
  projectId: string,
  chainId: string,
  signal: AbortSignal,
): Promise<void> => deleteRequest(chainPath(projectId, chainId), signal)
export const runChain = (
  projectId: string,
  chainId: string,
  inputPayload: string | null,
  signal: AbortSignal,
): Promise<ChainRun> =>
  postJson({
    path: `${chainPath(projectId, chainId)}/runs`,
    body: { inputPayload, stepPayloadOverrides: null },
    schema: chainRun,
    signal,
  })
export const fetchChainRuns = (
  projectId: string,
  chainId: string,
  signal: AbortSignal,
): Promise<ChainRun[]> =>
  getJson({
    path: `${chainPath(projectId, chainId)}/runs`,
    schema: chainRuns,
    signal,
  })
