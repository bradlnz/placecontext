import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { RolesPanel } from './RolesPanel'

describe('RolesPanel', () => {
  it('edits non-owner roles and keeps Owner immutable', async () => {
    const user = userEvent.setup()
    render(
      <RolesPanel
        busy={false}
        onCreate={vi.fn()}
        onDelete={vi.fn()}
        onUpdate={vi.fn()}
        permissions={['projects.view', 'jobs.view']}
        roles={[
          {
            id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
            name: 'Owner',
            isSystem: true,
            permissions: ['projects.view', 'jobs.view'],
            memberCount: 1,
          },
          {
            id: 'a102ed75-e94a-48fe-9826-2532d524857f',
            name: 'Member',
            isSystem: true,
            permissions: ['projects.view'],
            memberCount: 2,
          },
        ]}
      />,
    )
    expect(screen.getByText('All permissions (2)')).toBeVisible()
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    expect(screen.getByRole('button', { name: 'Save permissions' })).toBeVisible()
  })
})
