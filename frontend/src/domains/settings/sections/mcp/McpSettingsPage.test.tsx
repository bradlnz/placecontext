import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { mcpSettingsQueryOptions } from '../../api/mcp-query'
import { McpSettingsPage } from './McpSettingsPage'

describe('McpSettingsPage', () => {
  it('selects the initial project and opens the add form asynchronously', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
    queryClient.setQueryData(mcpSettingsQueryOptions(undefined).queryKey, {
      projectId,
      projects: [{ id: projectId, name: 'Atlas' }],
      connections: [],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <McpSettingsPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByLabelText('Project')).toHaveValue(projectId)
    expect(screen.getByText('No MCP servers configured for this project.')).toBeVisible()
    await user.click(screen.getByRole('button', { name: '+ Add server' }))
    expect(screen.getByRole('heading', { name: 'New MCP server' })).toBeVisible()
  })
})
