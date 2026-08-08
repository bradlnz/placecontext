import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { projectGraphQueryOptions } from '../../api/project-graph-query'
import { DataGraphPage } from './DataGraphPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const graph = {
  projectId,
  nodeCount: 3,
  linkCount: 1,
  nodes: [
    {
      id: 'hub',
      label: 'Atlas',
      degree: 1,
      isGod: true,
      content: null,
      kind: 'hub',
      labeled: true,
      artifact: null,
    },
    {
      id: 'roads',
      label: 'tables/roads',
      degree: 1,
      isGod: false,
      content: 'Road records',
      kind: 'table',
      labeled: false,
      artifact: null,
    },
    {
      id: 'people',
      label: 'tables/people',
      degree: 0,
      isGod: false,
      content: null,
      kind: 'table',
      labeled: false,
      artifact: null,
    },
  ],
  links: [{ source: 'hub', target: 'roads', confidence: 'Direct' }],
}

vi.mock('react-router-dom', () => ({
  useParams: () => ({ projectId }),
  NavLink: ({ children, ...props }: { children: string; to: string; className: string }) => (
    <a href={props.to} className={props.className}>
      {children}
    </a>
  ),
}))
vi.mock('./ProjectGraphCanvas', () => ({
  ProjectGraphCanvas: () => <div aria-label="Graph canvas" />,
}))

describe('DataGraphPage', () => {
  it('filters nodes and limits the sidebar to a selected neighborhood', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(projectGraphQueryOptions(projectId).queryKey, graph)
    render(
      <QueryClientProvider client={queryClient}>
        <DataGraphPage />
      </QueryClientProvider>,
    )

    expect(screen.getByText('3 nodes')).toBeVisible()
    await user.click(screen.getByRole('button', { name: /Atlas/ }))
    expect(screen.getByRole('button', { name: /roads/ })).toBeVisible()
    expect(screen.queryByRole('button', { name: /people/ })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Show all' }))
    expect(screen.getByRole('button', { name: /people/ })).toBeVisible()
  })
})
