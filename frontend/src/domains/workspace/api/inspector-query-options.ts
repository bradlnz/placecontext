import { queryOptions } from '@tanstack/react-query'

import { fetchInspectorToolCalls } from './inspector-api'

export const inspectorToolCallsQueryKey = ['workspace', 'inspector', 'tool-calls'] as const

export const inspectorToolCallsQuery = queryOptions({
  queryKey: inspectorToolCallsQueryKey,
  queryFn: async ({ signal }) => fetchInspectorToolCalls(signal),
  refetchInterval: 3_000,
})
