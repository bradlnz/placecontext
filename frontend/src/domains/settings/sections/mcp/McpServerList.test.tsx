import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { McpConnection } from '../../model/mcp'
import { McpServerList } from './McpServerList'

const connection: McpConnection = {
  id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
  projectId: 'a102ed75-e94a-48fe-9826-2532d524857f',
  name: 'Tools',
  transport: 'http',
  endpointUrl: 'https://mcp.example.com/mcp',
  command: null,
  args: null,
  authType: 'oauth',
  enabled: true,
  lastStatus: 'oauth:connected',
  lastConnectedAt: null,
  createdAt: '2026-08-08T00:00:00Z',
  oAuthTokenExpiresAt: '2099-08-08T00:00:00Z',
  oAuthClientId: null,
  oAuthScopes: 'openid',
}

describe('McpServerList', () => {
  it('shows OAuth state and server actions', () => {
    render(
      <McpServerList
        busy={false}
        connections={[connection]}
        onAuthorize={vi.fn()}
        onDelete={vi.fn()}
        onTest={vi.fn()}
        projectSelected
      />,
    )
    expect(screen.getByText('OAuth connected')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Authorize' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Test' })).toBeVisible()
  })
})
