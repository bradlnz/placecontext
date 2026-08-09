import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { crmQueryKeys } from '../../api/crm-query'
import type { CrmPageModel } from '../../model/crm'
import { CrmPage } from './CrmPage'

const projectId = '50c76fdc-bec8-4ca9-8560-38996f5aad5d'
const page: CrmPageModel = {
  appointments: [],
  automations: [],
  calendars: [],
  capabilities: { emailEnabled: true, emailProvider: 'smtp', smsEnabled: false, smsProvider: '' },
  chains: [],
  clients: [
    {
      company: 'River City Planning',
      createdAt: '2026-08-09T00:00:00+00:00',
      customerPortalBrandName: null,
      customerPortalDomain: null,
      customerPortalEnabled: false,
      customerPortalLogoUrl: null,
      customerPortalSlug: null,
      email: 'alex@example.com',
      id: 'a6d59213-65b1-4e6a-a34c-9c017707962e',
      lifecycleStage: 'Qualified',
      name: 'Alex Morgan',
      notes: null,
      phone: '0400 000 000',
      projectId,
      updatedAt: '2026-08-09T01:00:00+00:00',
    },
  ],
}

describe('CrmPage', () => {
  it('renders lifecycle counts and the contacts directory', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(crmQueryKeys.page(projectId), page)
    const user = userEvent.setup()
    render(
      <MemoryRouter initialEntries={[`/project/${projectId}/crm`]}>
        <QueryClientProvider client={queryClient}>
          <Routes>
            <Route path="/project/:projectId/crm" element={<CrmPage />} />
          </Routes>
        </QueryClientProvider>
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Opportunities' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'QualifiedFit confirmed1' })).toBeVisible()
    await user.click(screen.getByRole('button', { name: /Contacts/ }))
    expect(screen.getByText('Alex Morgan')).toBeVisible()
    expect(screen.getByText('alex@example.com')).toBeVisible()
  })
})
