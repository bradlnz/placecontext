import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { entityBrowseQueryOptions } from '../../api/entity-browse-query'
import { EntityBrowsePage } from './EntityBrowsePage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const entityName = 'Properties'
const model = {
  entity: {
    id: '59dd7208-3ece-4fe2-b86a-657cd0ec9927',
    projectId,
    name: entityName,
    tableName: 'properties',
    labelColumn: 'address',
    relations: [],
    tags: ['property'],
    updatedAt: '2026-08-09T01:00:00Z',
  },
  columns: [
    { name: 'id', type: 'uuid', notNull: true, primaryKey: true },
    { name: 'address', type: 'text', notNull: true, primaryKey: false },
  ],
  page: {
    columns: ['id', 'address'],
    rows: [['property-1', '17 River Street']],
    totalCount: 1,
    page: 1,
    pageSize: 50,
  },
}

vi.mock('react-router-dom', () => ({
  useParams: () => ({ projectId, entityName }),
  Link: ({ children, to, ...props }: { children: ReactNode; to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}))

describe('EntityBrowsePage', () => {
  it('renders records and opens the typed create form', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(entityBrowseQueryOptions(projectId, entityName, '', 1).queryKey, model)

    render(
      <QueryClientProvider client={queryClient}>
        <EntityBrowsePage />
      </QueryClientProvider>,
    )

    expect(screen.getByRole('cell', { name: '17 River Street' })).toBeVisible()
    await user.click(screen.getByRole('button', { name: '＋ New' }))
    expect(screen.getByText('New Properties')).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'address' })).toBeVisible()
  })
})
