import { getJson, postJson } from '../../../shared/api/http-client'
import type { ClusterPageModel } from '../model/cluster'
import { clusterJoinCommandSchema, clusterPageSchema } from './cluster-schemas'

export async function fetchCluster(signal: AbortSignal): Promise<ClusterPageModel> {
  return getJson({
    path: '/api/v1/cluster',
    schema: clusterPageSchema,
    signal,
  })
}

export async function createClusterJoinCommand(signal: AbortSignal): Promise<string> {
  const response = await postJson({
    path: '/api/v1/cluster/workers/join-command',
    body: {},
    schema: clusterJoinCommandSchema,
    signal,
  })
  return response.command
}
