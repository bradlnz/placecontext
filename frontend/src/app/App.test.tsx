import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { createMemoryRouter } from 'react-router-dom'
import { QueryClient } from '@tanstack/react-query'

import { workspaceOverviewFixture } from '../test/fixtures/workspace'
import { AppShell } from '../domains/navigation/sections/app-shell/AppShell'
import { workspaceQueryKeys } from '../domains/workspace/api/workspace-query-options'
import { OverviewPage } from '../domains/workspace/sections/overview/OverviewPage'
import { App } from './App'

describe('App', () => {
  it('composes routing, events, queries, and the overview section', async () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData(workspaceQueryKeys.projects, workspaceOverviewFixture.projects)
    queryClient.setQueryData(workspaceQueryKeys.focus, workspaceOverviewFixture.focus)
    queryClient.setQueryData(workspaceQueryKeys.stats, workspaceOverviewFixture.stats)
    queryClient.setQueryData(workspaceQueryKeys.session, workspaceOverviewFixture.session)
    const router = createMemoryRouter(
      [
        {
          element: <AppShell />,
          children: [{ index: true, element: <OverviewPage /> }],
        },
      ],
      { initialEntries: ['/'] },
    )

    render(<App queryClient={queryClient} router={router} />)

    expect(await screen.findByRole('heading', { name: 'Projects' })).toBeVisible()
    expect(screen.getByText('Ada Lovelace')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Open Atlas' })).toBeEnabled()
  })
})
