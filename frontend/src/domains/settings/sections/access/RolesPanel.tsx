import { useState } from 'react'

import type { AccessRole } from '../../model/access'

interface RolesPanelProps {
  busy: boolean
  permissions: string[]
  roles: AccessRole[]
  onCreate: (name: string, permissions: string[]) => Promise<boolean>
  onDelete: (roleId: string) => Promise<void>
  onUpdate: (roleId: string, permissions: string[]) => Promise<void>
}

function toggleValue(values: string[], value: string, enabled: boolean): string[] {
  return enabled
    ? values.includes(value)
      ? values
      : [...values, value]
    : values.filter((current) => current !== value)
}

function permissionSummary(role: AccessRole, permissionCount: number): string {
  if (role.permissions.length === 0) return 'No permissions granted'
  if (role.permissions.length === permissionCount)
    return `All permissions (${String(permissionCount)})`
  return role.permissions.join(', ')
}

export function RolesPanel({
  busy,
  permissions,
  roles,
  onCreate,
  onDelete,
  onUpdate,
}: RolesPanelProps) {
  const [editRoleId, setEditRoleId] = useState<string | null>(null)
  const [editPermissions, setEditPermissions] = useState<string[]>([])
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)
  const [newRoleName, setNewRoleName] = useState('')
  const [newRolePermissions, setNewRolePermissions] = useState<string[]>([])

  async function createRole(): Promise<void> {
    if (await onCreate(newRoleName.trim(), newRolePermissions)) {
      setNewRoleName('')
      setNewRolePermissions([])
    }
  }

  return (
    <section className="dccard access-card access-roles-card">
      <h2>Roles &amp; permissions</h2>
      <p>
        A role is a named set of permissions. System roles ship with the workspace — you can edit
        what they grant (except Owner) but not remove them. Custom roles can be assigned to members
        and deleted while nobody holds them.
      </p>
      <div className="access-role-list">
        {roles.map((role) => {
          const editing = editRoleId === role.id
          const isOwner = role.name.toLowerCase() === 'owner'
          return (
            <article className="access-role-item" key={role.id}>
              <div className="access-role-row">
                <div>
                  <strong>{role.name}</strong>
                  {role.isSystem ? <span className="flag-badge">System</span> : null}
                  <small>
                    {role.memberCount} {role.memberCount === 1 ? 'member' : 'members'}
                  </small>
                </div>
                <div>
                  {!isOwner ? (
                    <button
                      className="dcbtn"
                      onClick={() => {
                        if (editing) setEditRoleId(null)
                        else {
                          setEditRoleId(role.id)
                          setEditPermissions([...role.permissions])
                          setConfirmDeleteId(null)
                        }
                      }}
                      type="button"
                    >
                      {editing ? 'Close' : 'Edit'}
                    </button>
                  ) : null}
                  {!role.isSystem && role.memberCount === 0 ? (
                    confirmDeleteId === role.id ? (
                      <>
                        <button
                          className="dcbtn danger"
                          disabled={busy}
                          onClick={() => void onDelete(role.id)}
                          type="button"
                        >
                          Confirm delete
                        </button>
                        <button
                          className="dcbtn"
                          onClick={() => {
                            setConfirmDeleteId(null)
                          }}
                          type="button"
                        >
                          Keep
                        </button>
                      </>
                    ) : (
                      <button
                        className="dcbtn danger"
                        onClick={() => {
                          setConfirmDeleteId(role.id)
                        }}
                        type="button"
                      >
                        Delete
                      </button>
                    )
                  ) : null}
                </div>
              </div>
              <p>{permissionSummary(role, permissions.length)}</p>
              {editing ? (
                <>
                  <div className="access-permission-grid">
                    {permissions.map((permission) => (
                      <label key={permission}>
                        <input
                          checked={editPermissions.includes(permission)}
                          onChange={(event) => {
                            setEditPermissions((current) =>
                              toggleValue(current, permission, event.target.checked),
                            )
                          }}
                          type="checkbox"
                        />
                        {permission}
                      </label>
                    ))}
                  </div>
                  <div className="settings-actions">
                    <button
                      className="dcbtn primary"
                      disabled={busy}
                      onClick={() => void onUpdate(role.id, editPermissions)}
                      type="button"
                    >
                      Save permissions
                    </button>
                    <button
                      className="dcbtn"
                      onClick={() => {
                        setEditRoleId(null)
                      }}
                      type="button"
                    >
                      Cancel
                    </button>
                  </div>
                </>
              ) : null}
            </article>
          )
        })}
      </div>
      <div className="access-role-create">
        <h2>Create a role</h2>
        <label className="dcfield">
          <span>Name</span>
          <input
            onChange={(event) => {
              setNewRoleName(event.target.value)
            }}
            placeholder="Support"
            value={newRoleName}
          />
        </label>
        <div className="access-permission-grid">
          {permissions.map((permission) => (
            <label key={permission}>
              <input
                checked={newRolePermissions.includes(permission)}
                onChange={(event) => {
                  setNewRolePermissions((current) =>
                    toggleValue(current, permission, event.target.checked),
                  )
                }}
                type="checkbox"
              />
              {permission}
            </label>
          ))}
        </div>
        <button
          className="dcbtn primary"
          disabled={busy || newRoleName.trim() === ''}
          onClick={() => void createRole()}
          type="button"
        >
          Create role
        </button>
      </div>
    </section>
  )
}
