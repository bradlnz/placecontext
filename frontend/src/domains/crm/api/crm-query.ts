import { queryOptions } from '@tanstack/react-query'

import { fetchCrmClientDetail, fetchCrmPage } from './crm-api'

export const crmQueryKeys = {
  page: (projectId: string) => ['crm-page', projectId] as const,
  client: (projectId: string, clientId: string) => ['crm-client', projectId, clientId] as const,
}
export const crmPageQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: crmQueryKeys.page(projectId),
    queryFn: ({ signal }) => fetchCrmPage(projectId, signal),
  })
export const crmClientQueryOptions = (projectId: string, clientId: string) =>
  queryOptions({
    queryKey: crmQueryKeys.client(projectId, clientId),
    queryFn: ({ signal }) => fetchCrmClientDetail(projectId, clientId, signal),
  })
