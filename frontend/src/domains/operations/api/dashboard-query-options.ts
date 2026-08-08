import { queryOptions } from '@tanstack/react-query'

import { fetchDashboard } from './dashboard-api'

export const dashboardQueryKey = ['operations', 'dashboard'] as const

export const dashboardQuery = queryOptions({
  queryKey: dashboardQueryKey,
  queryFn: async ({ signal }) => fetchDashboard(signal),
})
