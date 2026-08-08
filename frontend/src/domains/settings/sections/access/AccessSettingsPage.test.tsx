import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { accessSettingsQueryOptions } from '../../api/access-query'
import { AccessSettingsPage } from './AccessSettingsPage'

describe('AccessSettingsPage', () => {
  it('composes portal, invitation, member, and role administration', () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData(accessSettingsQueryOptions.queryKey, {
      members: [
        {
          id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
          email: 'admin@example.com',
          displayName: 'Admin',
          role: 'Admin',
          isDefaultAdmin: true,
          createdAt: '2026-08-08T00:00:00Z',
        },
      ],
      roles: [
        {
          id: 'a102ed75-e94a-48fe-9826-2532d524857f',
          name: 'Admin',
          isSystem: true,
          permissions: ['projects.view'],
          memberCount: 1,
        },
      ],
      permissions: ['projects.view'],
      customerPortalEnabled: true,
      currentUserId: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <AccessSettingsPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByRole('heading', { name: 'Customer portal accounts' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Invite portal user' })).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Invite a member' })).toBeVisible()
    expect(screen.getByText(/admin@example.com/)).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Roles & permissions' })).toBeVisible()
  })
})
