import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { MemberAccessList } from './MemberAccessList'

describe('MemberAccessList', () => {
  it('renders the effective permission matrix and protects the default admin', () => {
    const member = {
      id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
      email: 'owner@example.com',
      displayName: 'Owner',
      role: 'Owner',
      isDefaultAdmin: true,
      createdAt: '2026-08-08T00:00:00Z',
    }
    render(
      <MemberAccessList
        assignableRoles={[
          {
            id: 'a102ed75-e94a-48fe-9826-2532d524857f',
            name: 'Member',
            isSystem: true,
            permissions: [],
            memberCount: 0,
          },
        ]}
        busy={false}
        currentUserId={member.id}
        expandedUserId={member.id}
        matrix={{
          userId: member.id,
          role: 'Owner',
          permissions: [
            {
              permission: 'projects.view',
              defaultAllowed: true,
              override: null,
              effective: true,
            },
          ],
        }}
        matrixLoading={false}
        members={[member]}
        onExpand={vi.fn()}
        onManage={vi.fn()}
        onRemove={vi.fn()}
        onRoleChange={vi.fn()}
        onSetPermission={vi.fn()}
      />,
    )
    expect(screen.getByText('Default admin')).toBeVisible()
    expect(screen.getByText('granted')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Role for Owner')).toBeDisabled()
  })
})
