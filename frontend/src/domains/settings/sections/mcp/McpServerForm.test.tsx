import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { McpConnectionDraft } from '../../model/mcp'
import { McpServerForm } from './McpServerForm'

const draft: McpConnectionDraft = {
  name: 'Tools',
  transport: 'http',
  endpointUrl: '',
  command: '',
  args: '',
  authType: 'none',
  authToken: '',
  authHeader: '',
  oAuthScopes: '',
}

describe('McpServerForm', () => {
  it('switches between endpoint and stdio command fields', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    const { rerender } = render(
      <McpServerForm
        busy={false}
        draft={draft}
        onCancel={vi.fn()}
        onChange={onChange}
        onSave={vi.fn()}
      />,
    )
    expect(screen.getByLabelText('Endpoint')).toBeVisible()
    await user.selectOptions(screen.getByLabelText('Transport'), 'stdio')
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ transport: 'stdio' }))
    rerender(
      <McpServerForm
        busy={false}
        draft={{ ...draft, transport: 'stdio' }}
        onCancel={vi.fn()}
        onChange={onChange}
        onSave={vi.fn()}
      />,
    )
    expect(screen.getByLabelText('Command')).toBeVisible()
    expect(screen.queryByLabelText('Endpoint')).not.toBeInTheDocument()
  })
})
