import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { projectPageQueryOptions } from '../../api/project-page-query'
import { ProjectPage } from './ProjectPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const context = {
  overview: {
    id: projectId,
    name: 'Atlas',
    path: '/code/atlas',
    status: 'Active',
    godNodes: [{ id: 'hub', label: 'Atlas hub', degree: 4 }],
  },
  timeline: {
    changes: [
      {
        id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
        sequence: 2,
        title: 'Indexed roads',
        kind: 'Agent',
        commit: 'abc123',
      },
    ],
  },
  decisions: [
    {
      id: '79d2d944-56ef-4597-a64d-10b56c18e33d',
      question: 'Which database?',
      choice: 'PostgreSQL',
      rationale: 'Spatial support',
      decidedAt: '2026-08-08T00:00:00+00:00',
      decidedAtDisplay: '2026-08-08',
    },
  ],
  requirements: {
    markdown: '# Rules',
    updatedAt: null,
    updatedAtDisplay: null,
  },
  message: null,
}

describe('ProjectPage', () => {
  it('switches among overview, requirements, and activity', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(projectPageQueryOptions(projectId).queryKey, context)
    const router = createMemoryRouter([{ path: '/project/:projectId', element: <ProjectPage /> }], {
      initialEntries: [`/project/${projectId}`],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )

    expect(screen.getByText('Atlas hub')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Requirements' }))
    expect(screen.getByRole('textbox')).toHaveValue('# Rules')
    await user.click(screen.getByRole('button', { name: 'Activity' }))
    expect(screen.getByText('Which database?')).toBeVisible()
    expect(screen.getByText('Indexed roads')).toBeVisible()
  })
})
