import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { projectDataStudioQueryOptions } from '../../api/project-data-studio-query'
import { ProjectDataPage } from './ProjectDataPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const api = vi.hoisted(() => ({
  fetchProjectDataStudio: vi.fn(),
  runProjectDataQuery: vi.fn(),
  createProjectDataTable: vi.fn(),
  deleteProjectDataQuery: vi.fn(),
  fetchProjectDataRowLinks: vi.fn(),
  materializeProjectDataTable: vi.fn(),
  saveProjectDataQuery: vi.fn(),
}))

vi.mock('../../api/project-data-studio-api', () => api)
vi.mock('react-router-dom', () => ({
  useParams: () => ({ projectId }),
  NavLink: ({ children, to, ...props }: { children: ReactNode; to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}))

const model = {
  tables: [{ name: 'properties', rowEstimate: 2, readOnly: false, isView: false }],
  indices: [{ name: 'pc-properties', documentCount: 2, storeSize: '12kb' }],
  savedQueries: [],
}

describe('ProjectDataPage', () => {
  beforeEach(() => {
    api.runProjectDataQuery.mockReset()
    api.runProjectDataQuery.mockResolvedValue({
      columns: ['id', 'address'],
      rows: [
        ['property-1', '17 River Street'],
        ['property-2', '8 Hill Road'],
      ],
      affectedRows: 0,
      truncated: false,
    })
  })

  it('opens a table in SQL Studio and renders its query result', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(projectDataStudioQueryOptions(projectId).queryKey, model)

    render(
      <QueryClientProvider client={queryClient}>
        <ProjectDataPage />
      </QueryClientProvider>,
    )

    await user.click(screen.getByRole('button', { name: /propertiestable/ }))
    expect(screen.getByRole('textbox', { name: 'SQL query' })).toHaveValue(
      'SELECT * FROM "properties" LIMIT 100;',
    )
    expect(await screen.findByRole('cell', { name: '17 River Street' })).toBeVisible()
    expect(api.runProjectDataQuery).toHaveBeenCalledWith(
      projectId,
      'SELECT * FROM "properties" LIMIT 100;',
      'postgres',
      expect.any(AbortSignal),
    )
  })
})
