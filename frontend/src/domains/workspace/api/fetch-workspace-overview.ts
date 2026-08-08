import { getJson } from '../../../shared/api/http-client'
import type { WorkspaceOverview } from '../model/workspace'
import {
  workspaceFocusSchema,
  workspaceProjectsSchema,
  workspaceSessionSchema,
  workspaceStatsSchema,
} from './workspace-schemas'

const WORKSPACE_API_PATH = '/api/v1/workspace'

export async function fetchWorkspaceProjects(signal: AbortSignal) {
  return getJson({
    path: `${WORKSPACE_API_PATH}/projects`,
    schema: workspaceProjectsSchema,
    signal,
  })
}

export async function fetchWorkspaceFocus(signal: AbortSignal) {
  return getJson({
    path: `${WORKSPACE_API_PATH}/focus`,
    schema: workspaceFocusSchema,
    signal,
  })
}

export async function fetchWorkspaceStats(signal: AbortSignal) {
  return getJson({
    path: `${WORKSPACE_API_PATH}/stats`,
    schema: workspaceStatsSchema,
    signal,
  })
}

export async function fetchWorkspaceSession(signal: AbortSignal) {
  return getJson({
    path: `${WORKSPACE_API_PATH}/session`,
    schema: workspaceSessionSchema,
    signal,
  })
}

export async function fetchWorkspaceOverview(
  signal: AbortSignal,
): Promise<WorkspaceOverview> {
  const projectsPromise = fetchWorkspaceProjects(signal)
  const focusPromise = fetchWorkspaceFocus(signal)
  const statsPromise = fetchWorkspaceStats(signal)
  const sessionPromise = fetchWorkspaceSession(signal)

  const [projects, focus, stats, session] = await Promise.all([
    projectsPromise,
    focusPromise,
    statsPromise,
    sessionPromise,
  ])

  return { projects, focus, stats, session }
}
