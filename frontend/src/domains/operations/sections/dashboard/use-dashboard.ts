import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { runDashboardChain } from '../../api/dashboard-api'
import { dashboardQuery, dashboardQueryKey } from '../../api/dashboard-query-options'

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'The chain could not be started.'
}

export function useDashboard() {
  const eventBus = useAppEventBus()
  const queryClient = useQueryClient()
  const dashboardResult = useSuspenseQuery(dashboardQuery)
  const [chainMessage, setChainMessage] = useState<string | null>(null)
  const [chainError, setChainError] = useState(false)
  const chainMutation = useMutation({
    mutationFn: async (command: Parameters<typeof runDashboardChain>[0]) =>
      runDashboardChain(command, AbortSignal.timeout(30_000)),
  })

  useEffect(() => {
    return eventBus.subscribe('dashboard.refresh-requested', async () => {
      await queryClient.invalidateQueries({ queryKey: dashboardQueryKey })
    })
  }, [eventBus, queryClient])

  useEffect(() => {
    return eventBus.subscribe('dashboard.chain-run-requested', async (command) => {
      setChainMessage(null)
      setChainError(false)

      try {
        const result = await chainMutation.mutateAsync(command)
        setChainMessage(result.message)
        await eventBus.publish('dashboard.chain-run-started', result)
        await eventBus.publish('dashboard.refresh-requested', { source: 'chain-run' })
      } catch (error: unknown) {
        setChainMessage(errorMessage(error))
        setChainError(true)
      }
    })
  }, [chainMutation, eventBus])

  useEffect(() => {
    void eventBus.publish('dashboard.loaded', {
      runningCount: dashboardResult.data.stats.running,
    })
  }, [dashboardResult.data.stats.running, eventBus])

  return {
    dashboard: dashboardResult.data,
    chainMessage,
    chainError,
    runningChainId: chainMutation.variables?.chainId ?? null,
  }
}
