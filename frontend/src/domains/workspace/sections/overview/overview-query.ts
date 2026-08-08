import { useQueryClient, useSuspenseQueries } from '@tanstack/react-query'
import { useEffect } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import type { WorkspaceOverview } from '../../model/workspace'
import {
  workspaceFocusQuery,
  workspaceProjectsQuery,
  workspaceSessionQuery,
  workspaceStatsQuery,
} from '../../api/workspace-query-options'

export function useWorkspaceOverview(): {
  data: WorkspaceOverview
  isRefreshing: boolean
} {
  const eventBus = useAppEventBus()
  const queryClient = useQueryClient()
  const [projectsQuery, focusQuery, statsQuery, sessionQuery] = useSuspenseQueries({
    queries: [
      workspaceProjectsQuery,
      workspaceFocusQuery,
      workspaceStatsQuery,
      workspaceSessionQuery,
    ],
  })

  const data: WorkspaceOverview = {
    projects: projectsQuery.data,
    focus: focusQuery.data,
    stats: statsQuery.data,
    session: sessionQuery.data,
  }
  const isRefreshing = projectsQuery.isFetching
    || focusQuery.isFetching
    || statsQuery.isFetching
    || sessionQuery.isFetching

  useEffect(() => {
    return eventBus.subscribe('workspace.overview-refresh-requested', async () => {
      await queryClient.invalidateQueries({ queryKey: ['workspace'] })
    })
  }, [eventBus, queryClient])

  useEffect(() => {
    void eventBus.publish('workspace.overview-refreshed', {
      projectCount: data.projects.length,
    })
  }, [data.projects.length, eventBus])

  return { data, isRefreshing }
}
