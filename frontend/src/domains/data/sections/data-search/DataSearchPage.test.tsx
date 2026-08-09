import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { openSearchPageQueryOptions } from '../../api/open-search-query'
import { DataSearchPage } from './DataSearchPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const api = vi.hoisted(() => ({
  fetchOpenSearchPage: vi.fn(),
  searchOpenSearch: vi.fn(),
  saveOpenSearchDashboard: vi.fn(),
  deleteOpenSearchDashboard: vi.fn(),
  triggerOpenSearchSync: vi.fn(),
}))

vi.mock('../../api/open-search-api', () => api)
vi.mock('react-router-dom', () => ({
  useParams: () => ({ projectId }),
  NavLink: ({ children, to, ...props }: { children: ReactNode; to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}))
vi.mock('./OpenSearchChart', () => ({
  OpenSearchChart: () => <div aria-label="OpenSearch chart" />,
}))

const page = {
  indices: [{ name: 'properties', documentCount: 2, storeSize: '12kb' }],
  dashboards: [],
  selectedIndex: 'properties',
  fields: [],
  lastUpdated: null,
  canSync: false,
  error: null,
}

describe('DataSearchPage', () => {
  beforeEach(() => {
    api.searchOpenSearch.mockReset()
    api.searchOpenSearch.mockResolvedValue({
      total: 1,
      tookMs: 7,
      chartSpecJson: null,
      hits: [
        {
          index: 'properties',
          id: 'property-1',
          score: 1,
          fields: { address: '17 River Street', suburb: 'West End' },
        },
      ],
    })
  })

  it('searches the selected index and opens record details', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(openSearchPageQueryOptions(projectId, '').queryKey, page)

    render(
      <QueryClientProvider client={queryClient}>
        <DataSearchPage />
      </QueryClientProvider>,
    )

    await user.type(screen.getByRole('textbox', { name: 'Query' }), 'river')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    expect(await screen.findByRole('cell', { name: '17 River Street' })).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Inspect' }))
    expect(screen.getByText('Record details')).toBeVisible()
    expect(api.searchOpenSearch).toHaveBeenCalledWith(
      projectId,
      expect.objectContaining({ indexPattern: 'properties', queryText: 'river', page: 1 }),
      expect.any(AbortSignal),
    )
  })
})
