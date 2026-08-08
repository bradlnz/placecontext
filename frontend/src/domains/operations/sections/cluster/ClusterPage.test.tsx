import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { clusterQueryOptions } from '../../api/cluster-query'
import { ClusterPage } from './ClusterPage'

describe('ClusterPage', () => {
  it('renders fleet metrics and node health', () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(clusterQueryOptions.queryKey, {
      isRealCluster: true,
      designatedMasterName: 'master-1',
      lastSyncLabel: '10:00:00',
      nodes: [
        {
          name: 'master-1',
          roles: ['control-plane'],
          ready: true,
          kubeletVersion: 'v1.34',
          preferredIp: '100.64.0.1',
          cpuCapacity: '4',
          memoryCapacity: '8Gi',
          isSelf: true,
          isControlPlane: true,
          isDesignatedMaster: true,
          platformLabel: 'linux · amd64',
          relativeAge: '2d ago',
        },
      ],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <ClusterPage />
      </QueryClientProvider>,
    )
    expect(screen.getByText('100% ready')).toBeVisible()
    expect(screen.getByRole('heading', { name: /master-1/ })).toBeVisible()
    expect(screen.getByText('★ Fleet master')).toBeVisible()
  })
})
