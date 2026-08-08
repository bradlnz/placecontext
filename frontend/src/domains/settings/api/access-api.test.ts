import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchAccessSettings, setMemberPermission, setMemberRole } from './access-api'

const memberId = '158fdb23-5c46-4777-b0bb-d78ff91b8754'
const body = {
  members: [
    {
      id: memberId,
      email: 'member@example.com',
      displayName: 'Member',
      role: 'Member',
      isDefaultAdmin: false,
      createdAt: '2026-08-08T00:00:00Z',
    },
  ],
  roles: [
    {
      id: 'a102ed75-e94a-48fe-9826-2532d524857f',
      name: 'Member',
      isSystem: true,
      permissions: ['projects.view'],
      memberCount: 1,
    },
  ],
  permissions: ['projects.view'],
  customerPortalEnabled: false,
  currentUserId: 'c971dffe-51a3-4f38-881f-a9fa7d159a4d',
}

describe('access settings API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads the composed access context from api/v1', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))
    await expect(fetchAccessSettings(new AbortController().signal)).resolves.toEqual(body)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/settings/access/context',
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('updates member roles and permission overrides', async () => {
    const matrix = {
      userId: memberId,
      role: 'Member',
      permissions: [
        {
          permission: 'projects.view',
          defaultAllowed: true,
          override: false,
          effective: false,
        },
      ],
    }
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation((_path, init) =>
        Promise.resolve(
          init?.method === 'PUT' &&
            typeof init.body === 'string' &&
            init.body.includes('permission')
            ? new Response(JSON.stringify(matrix), { status: 200 })
            : new Response(null, { status: 204 }),
        ),
      )
    const signal = new AbortController().signal
    await setMemberRole(memberId, 'Admin', signal)
    await expect(setMemberPermission(memberId, 'projects.view', false, signal)).resolves.toEqual(
      matrix,
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/settings/access/members/${memberId}/role`,
      expect.objectContaining({ method: 'PUT' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/settings/access/members/${memberId}/permission`,
      expect.objectContaining({ method: 'PUT' }),
    )
  })
})
