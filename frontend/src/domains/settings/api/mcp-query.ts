import { queryOptions } from '@tanstack/react-query'

import { fetchMcpSettings } from './mcp-api'

export function mcpSettingsQueryOptions(projectId: string | undefined) {
  return queryOptions({
    queryKey: ['settings', 'mcp', projectId ?? 'default'],
    queryFn: ({ signal }: { signal: AbortSignal }) => fetchMcpSettings(projectId, signal),
    staleTime: 30_000,
  })
}
