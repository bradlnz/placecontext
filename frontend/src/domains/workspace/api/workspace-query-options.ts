import { queryOptions } from '@tanstack/react-query'

import {
  fetchWorkspaceFocus,
  fetchWorkspaceProjects,
  fetchWorkspaceSession,
  fetchWorkspaceStats,
} from './fetch-workspace-overview'

export const workspaceQueryKeys = {
  projects: ['workspace', 'projects'] as const,
  focus: ['workspace', 'focus'] as const,
  stats: ['workspace', 'stats'] as const,
  session: ['workspace', 'session'] as const,
}

export const workspaceProjectsQuery = queryOptions({
  queryKey: workspaceQueryKeys.projects,
  queryFn: async ({ signal }) => fetchWorkspaceProjects(signal),
})

export const workspaceFocusQuery = queryOptions({
  queryKey: workspaceQueryKeys.focus,
  queryFn: async ({ signal }) => fetchWorkspaceFocus(signal),
})

export const workspaceStatsQuery = queryOptions({
  queryKey: workspaceQueryKeys.stats,
  queryFn: async ({ signal }) => fetchWorkspaceStats(signal),
})

export const workspaceSessionQuery = queryOptions({
  queryKey: workspaceQueryKeys.session,
  queryFn: async ({ signal }) => fetchWorkspaceSession(signal),
})
