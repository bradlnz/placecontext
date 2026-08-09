import { queryOptions } from '@tanstack/react-query'

import { fetchChatPage } from './chat-api'

export const chatQueryKeys = {
  page: (projectId: string) => ['chat-page', projectId] as const,
}

export const chatPageQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: chatQueryKeys.page(projectId),
    queryFn: ({ signal }) => fetchChatPage(projectId, signal),
  })
