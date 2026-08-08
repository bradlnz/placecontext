import { getJson, postJson } from '../../../shared/api/http-client'
import type {
  Dashboard,
  RunDashboardChainCommand,
  RunDashboardChainResult,
} from '../model/dashboard'
import {
  dashboardSchema,
  runDashboardChainResultSchema,
} from './dashboard-schemas'

const DASHBOARD_API_PATH = '/api/v1/dashboard'

export async function fetchDashboard(signal: AbortSignal): Promise<Dashboard> {
  return getJson({
    path: DASHBOARD_API_PATH,
    schema: dashboardSchema,
    signal,
  })
}

export async function runDashboardChain(
  command: RunDashboardChainCommand,
  signal: AbortSignal,
): Promise<RunDashboardChainResult> {
  return postJson({
    path: `${DASHBOARD_API_PATH}/projects/${command.projectId}/chains/${command.chainId}/runs`,
    body: {
      inputPayload: command.inputPayload,
      stepPayloadOverrides: command.stepPayloadOverrides,
    },
    schema: runDashboardChainResultSchema,
    signal,
  })
}
