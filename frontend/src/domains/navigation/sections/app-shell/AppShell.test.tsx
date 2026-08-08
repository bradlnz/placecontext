import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { workspaceOverviewFixture } from '../../../../test/fixtures/workspace'
import { workspaceQueryKeys } from '../../../workspace/api/workspace-query-options'
import { AppShell } from './AppShell'

function renderShell() {
  const queryClient = new QueryClient()
  queryClient.setQueryData(workspaceQueryKeys.projects, workspaceOverviewFixture.projects)
  queryClient.setQueryData(workspaceQueryKeys.session, workspaceOverviewFixture.session)

  const router = createMemoryRouter([
    {
      element: <AppShell />,
      children: [
        {
          index: true,
          element: <p>Overview content</p>,
          handle: {
            title: 'Overview',
            subtitle: 'codebase visibility · projects register via MCP',
          },
        },
      ],
    },
  ])

  return render(
    <QueryClientProvider client={queryClient}>
      <AppEventBusProvider>
        <RouterProvider router={router} />
      </AppEventBusProvider>
    </QueryClientProvider>,
  )
}

describe('AppShell', () => {
  it('renders migrated and legacy navigation around section content', () => {
    renderShell()

    expect(screen.getByText('Overview content')).toBeVisible()
    expect(screen.getByRole('link', { name: 'Dashboard' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('link', { name: /overview/i })).toHaveAttribute('href', '/overview')
    expect(screen.getByRole('link', { name: /search context/i })).toHaveAttribute(
      'href',
      '/inspector',
    )
    expect(screen.getByRole('link', { name: /artifacts/i })).toHaveAttribute('href', '/artifacts')
    expect(screen.getByText('Ada Lovelace')).toBeVisible()
  })

  it('opens and closes mobile navigation from async handlers', async () => {
    const user = userEvent.setup()
    renderShell()
    const sidebar = screen.getByRole('complementary')

    await user.click(screen.getByRole('button', { name: 'Toggle navigation' }))
    expect(sidebar).toHaveClass('open')

    const closeButtons = screen.getAllByRole('button', { name: 'Close navigation' })
    const closeButton = closeButtons.at(0)
    if (closeButton === undefined) throw new Error('Close navigation button was not rendered.')
    await user.click(closeButton)
    expect(sidebar).not.toHaveClass('open')
  })
})
