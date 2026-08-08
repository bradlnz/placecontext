import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import {
  createAccessRole,
  deleteAccessRole,
  deleteMember,
  inviteCustomerPortalUser,
  inviteMember,
  setCustomerPortalEnabled,
  setMemberPermission,
  setMemberRole,
  updateAccessRole,
} from '../../api/access-api'
import { accessSettingsQueryOptions, memberPermissionsQueryOptions } from '../../api/access-query'
import type { MemberInvite, UserPermissions } from '../../model/access'
import { CustomerPortalAccounts } from './CustomerPortalAccounts'
import { MemberAccessList } from './MemberAccessList'
import { RolesPanel } from './RolesPanel'

type AccessCommand =
  | { kind: 'portal-toggle'; enabled: boolean }
  | { kind: 'portal-invite'; email: string; role: string }
  | { kind: 'member-invite'; email: string; role: string }
  | { kind: 'member-role'; userId: string; role: string }
  | { kind: 'member-remove'; userId: string }
  | {
      kind: 'permission'
      userId: string
      permission: string
      allowed: boolean | null
    }
  | { kind: 'role-create'; name: string; permissions: string[] }
  | { kind: 'role-update'; roleId: string; permissions: string[] }
  | { kind: 'role-delete'; roleId: string }

type AccessResult =
  | { type: 'message'; message: string }
  | { type: 'invite'; invite: MemberInvite }
  | { type: 'matrix'; matrix: UserPermissions }
  | { type: 'refresh' }

export function AccessSettingsPage() {
  const { data } = useSuspenseQuery(accessSettingsQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [message, setMessage] = useState<string | null>(null)
  const [portalMessage, setPortalMessage] = useState<string | null>(null)
  const [inviteEmail, setInviteEmail] = useState('')
  const assignableRoles = data.roles.filter((role) => role.name.toLowerCase() !== 'owner')
  const [inviteRole, setInviteRole] = useState(() => assignableRoles[0]?.name ?? 'Member')
  const [inviteLink, setInviteLink] = useState<string | null>(null)
  const [inviteError, setInviteError] = useState<string | null>(null)
  const [expandedUserId, setExpandedUserId] = useState<string | null>(null)
  const matrixQueryOptions = memberPermissionsQueryOptions(expandedUserId)
  const matrixQuery = useQuery(matrixQueryOptions)

  const mutation = useMutation({
    mutationFn: async (command: AccessCommand): Promise<AccessResult> => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'portal-toggle')
        return {
          type: 'message',
          message: await setCustomerPortalEnabled(command.enabled, signal),
        }
      if (command.kind === 'portal-invite')
        return {
          type: 'message',
          message: await inviteCustomerPortalUser(command.email, command.role, signal),
        }
      if (command.kind === 'member-invite')
        return {
          type: 'invite',
          invite: await inviteMember(command.email, command.role, signal),
        }
      if (command.kind === 'member-role') {
        await setMemberRole(command.userId, command.role, signal)
        return { type: 'refresh' }
      }
      if (command.kind === 'member-remove') {
        await deleteMember(command.userId, signal)
        return { type: 'refresh' }
      }
      if (command.kind === 'permission')
        return {
          type: 'matrix',
          matrix: await setMemberPermission(
            command.userId,
            command.permission,
            command.allowed,
            signal,
          ),
        }
      if (command.kind === 'role-create') {
        await createAccessRole(command.name, command.permissions, signal)
        return { type: 'refresh' }
      }
      if (command.kind === 'role-update') {
        await updateAccessRole(command.roleId, command.permissions, signal)
        return { type: 'refresh' }
      }
      await deleteAccessRole(command.roleId, signal)
      return { type: 'refresh' }
    },
    onSuccess: async (result, command) => {
      if (command.kind === 'portal-toggle' || command.kind === 'portal-invite')
        setPortalMessage(result.type === 'message' ? result.message : null)
      if (command.kind === 'member-invite' && result.type === 'invite') {
        setInviteLink(result.invite.inviteLink)
        setInviteEmail('')
        setMessage(`Invite sent for ${result.invite.email}.`)
      }
      if (command.kind === 'member-role') setMessage('Role updated.')
      if (command.kind === 'member-remove') {
        setMessage('Member removed.')
        if (expandedUserId === command.userId) setExpandedUserId(null)
      }
      if (command.kind === 'permission' && result.type === 'matrix')
        queryClient.setQueryData(matrixQueryOptions.queryKey, result.matrix)
      if (command.kind === 'role-create') setMessage('Role created.')
      if (command.kind === 'role-update') setMessage('Role permissions updated.')
      if (command.kind === 'role-delete') setMessage('Role deleted.')
      const changesContext =
        command.kind !== 'portal-invite' &&
        command.kind !== 'member-invite' &&
        command.kind !== 'permission'
      const scope = command.kind.startsWith('role-')
        ? 'role'
        : command.kind === 'permission'
          ? 'permission'
          : command.kind.startsWith('portal-')
            ? 'portal'
            : 'member'
      await Promise.all([
        changesContext
          ? queryClient.invalidateQueries({
              queryKey: accessSettingsQueryOptions.queryKey,
            })
          : Promise.resolve(),
        eventBus.publish('settings.access-changed', { scope }),
      ])
    },
  })

  async function execute(command: AccessCommand): Promise<boolean> {
    setMessage(null)
    if (command.kind === 'member-invite') {
      setInviteError(null)
      setInviteLink(null)
    }
    try {
      await mutation.mutateAsync(command)
      return true
    } catch (error: unknown) {
      const text = error instanceof Error ? error.message : 'Access settings could not be updated.'
      if (command.kind === 'portal-toggle' || command.kind === 'portal-invite')
        setPortalMessage(text)
      else if (command.kind === 'member-invite') setInviteError(text)
      else setMessage(text)
      return false
    }
  }

  async function toggleExpanded(userId: string): Promise<void> {
    await Promise.resolve()
    setExpandedUserId((current) => (current === userId ? null : userId))
  }

  async function manageMember(userId: string): Promise<void> {
    setExpandedUserId(userId)
    await queryClient.fetchQuery(memberPermissionsQueryOptions(userId))
    requestAnimationFrame(() => {
      document
        .getElementById(`member-${userId}`)
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })
  }

  return (
    <div className="settings-page access-settings-page">
      <title>PlaceContext — Access</title>
      <header className="settings-page-head">
        <div>
          <span className="settings-kicker">Workspace administration</span>
          <h1>Access</h1>
          <p>
            Manage members, roles and permission overrides. Explicit overrides take precedence over
            role permissions.
          </p>
        </div>
      </header>
      {message === null ? null : (
        <div className="settings-message access-message" role="status">
          {message}
        </div>
      )}
      <CustomerPortalAccounts
        busy={mutation.isPending}
        enabled={data.customerPortalEnabled}
        message={portalMessage}
        onInvite={async (email, role) => execute({ kind: 'portal-invite', email, role })}
        onToggle={async (enabled) => {
          await execute({ kind: 'portal-toggle', enabled })
        }}
      />
      <section className="dccard access-card">
        <h2>Invite a member</h2>
        <div className="access-invite-row">
          <label className="dcfield">
            <span>Email</span>
            <input
              onChange={(event) => {
                setInviteEmail(event.target.value)
              }}
              placeholder="name@example.com"
              type="email"
              value={inviteEmail}
            />
          </label>
          <label className="dcfield">
            <span>Role</span>
            <select
              onChange={(event) => {
                setInviteRole(event.target.value)
              }}
              value={inviteRole}
            >
              {assignableRoles.map((role) => (
                <option key={role.id} value={role.name}>
                  {role.name}
                </option>
              ))}
            </select>
          </label>
          <button
            className="dcbtn primary"
            disabled={mutation.isPending}
            onClick={() =>
              void execute({
                kind: 'member-invite',
                email: inviteEmail.trim(),
                role: inviteRole,
              })
            }
            type="button"
          >
            {mutation.isPending ? 'Inviting…' : 'Send invite'}
          </button>
        </div>
        {inviteLink === null ? null : (
          <div className="settings-hint">
            Invite link (share it — single use): <code>{inviteLink}</code>
          </div>
        )}
        {inviteError === null ? null : (
          <div className="connection-error" role="alert">
            {inviteError}
          </div>
        )}
      </section>
      <MemberAccessList
        assignableRoles={assignableRoles}
        busy={mutation.isPending}
        currentUserId={data.currentUserId}
        expandedUserId={expandedUserId}
        matrix={matrixQuery.data}
        matrixLoading={matrixQuery.isLoading}
        members={data.members}
        onExpand={toggleExpanded}
        onManage={manageMember}
        onRemove={async (userId) => {
          await execute({ kind: 'member-remove', userId })
        }}
        onRoleChange={async (userId, role) => {
          await execute({ kind: 'member-role', userId, role })
        }}
        onSetPermission={async (userId, permission, allowed) => {
          await execute({ kind: 'permission', userId, permission, allowed })
        }}
      />
      <RolesPanel
        busy={mutation.isPending}
        onCreate={async (name, permissions) => execute({ kind: 'role-create', name, permissions })}
        onDelete={async (roleId) => {
          await execute({ kind: 'role-delete', roleId })
        }}
        onUpdate={async (roleId, permissions) => {
          await execute({ kind: 'role-update', roleId, permissions })
        }}
        permissions={data.permissions}
        roles={data.roles}
      />
    </div>
  )
}
