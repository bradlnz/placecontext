import { useState } from 'react'

import type { AccessMember, AccessRole, UserPermissions } from '../../model/access'

interface MemberAccessListProps {
  assignableRoles: AccessRole[]
  busy: boolean
  currentUserId: string
  expandedUserId: string | null
  matrix: UserPermissions | undefined
  matrixLoading: boolean
  members: AccessMember[]
  onExpand: (userId: string) => Promise<void>
  onManage: (userId: string) => Promise<void>
  onRemove: (userId: string) => Promise<void>
  onRoleChange: (userId: string, role: string) => Promise<void>
  onSetPermission: (userId: string, permission: string, allowed: boolean | null) => Promise<void>
}

function canRemove(member: AccessMember, currentUserId: string): boolean {
  return (
    !member.isDefaultAdmin && member.role.toLowerCase() !== 'owner' && member.id !== currentUserId
  )
}

export function MemberAccessList({
  assignableRoles,
  busy,
  currentUserId,
  expandedUserId,
  matrix,
  matrixLoading,
  members,
  onExpand,
  onManage,
  onRemove,
  onRoleChange,
  onSetPermission,
}: MemberAccessListProps) {
  const [confirmRemoveId, setConfirmRemoveId] = useState<string | null>(null)

  if (members.length === 0)
    return <div className="dccard empty-note">No members yet — invite one above.</div>

  return (
    <>
      <label className="dcfield access-manage">
        <span>Manage member</span>
        <select
          onChange={(event) => {
            if (event.target.value !== '') void onManage(event.target.value)
          }}
          value=""
        >
          <option value="">Select a member…</option>
          {members
            .filter((member) => !member.isDefaultAdmin)
            .map((member) => (
              <option key={member.id} value={member.id}>
                {member.displayName} · {member.email}
              </option>
            ))}
        </select>
      </label>
      <div className="access-member-list">
        {members.map((member) => {
          const roleOptions = assignableRoles.some((role) => role.name === member.role)
            ? assignableRoles
            : [
                ...assignableRoles,
                {
                  id: member.role,
                  name: member.role,
                  isSystem: true,
                  permissions: [],
                  memberCount: 0,
                },
              ]
          const expanded = expandedUserId === member.id
          return (
            <article
              className="dccard access-member-card"
              id={`member-${member.id}`}
              key={member.id}
            >
              <div className="access-member-row">
                <div className="access-member-info">
                  <strong>
                    {member.displayName} <span>· {member.email}</span>
                  </strong>
                  {member.isDefaultAdmin ? <span className="flag-badge">Default admin</span> : null}
                  <small>member since {member.createdAt.slice(0, 10)}</small>
                </div>
                <select
                  aria-label={`Role for ${member.displayName}`}
                  className="access-role-select"
                  disabled={busy || member.isDefaultAdmin}
                  onChange={(event) => void onRoleChange(member.id, event.target.value)}
                  value={member.role}
                >
                  {roleOptions.map((role) => (
                    <option key={role.name} value={role.name}>
                      {role.name}
                    </option>
                  ))}
                </select>
                <button className="dcbtn" onClick={() => void onExpand(member.id)} type="button">
                  {expanded ? 'Hide permissions' : 'Permissions'}
                </button>
                {canRemove(member, currentUserId) ? (
                  confirmRemoveId === member.id ? (
                    <>
                      <button
                        className="dcbtn danger"
                        disabled={busy}
                        onClick={() => void onRemove(member.id)}
                        type="button"
                      >
                        Confirm remove
                      </button>
                      <button
                        className="dcbtn"
                        onClick={() => {
                          setConfirmRemoveId(null)
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
                        setConfirmRemoveId(member.id)
                      }}
                      type="button"
                    >
                      Remove
                    </button>
                  )
                ) : null}
              </div>
              {expanded ? (
                <div className="access-matrix">
                  {matrixLoading || matrix === undefined ? (
                    <div>Loading…</div>
                  ) : (
                    <>
                      <p>
                        Role default shown in parentheses; Inherit clears the override and follows
                        the role.
                      </p>
                      {matrix.permissions.map((grant) => (
                        <div className="access-permission-row" key={grant.permission}>
                          <span>
                            {grant.permission}{' '}
                            <small>(default: {grant.defaultAllowed ? 'allow' : 'deny'})</small>
                          </span>
                          <div>
                            <button
                              className={`dcbtn${grant.override === true ? ' primary' : ''}`}
                              disabled={busy}
                              onClick={() =>
                                void onSetPermission(member.id, grant.permission, true)
                              }
                              type="button"
                            >
                              Allow
                            </button>
                            <button
                              className={`dcbtn${grant.override === false ? ' danger' : ''}`}
                              disabled={busy}
                              onClick={() =>
                                void onSetPermission(member.id, grant.permission, false)
                              }
                              type="button"
                            >
                              Revoke
                            </button>
                            <button
                              className="dcbtn"
                              disabled={busy || grant.override === null}
                              onClick={() =>
                                void onSetPermission(member.id, grant.permission, null)
                              }
                              type="button"
                            >
                              Inherit
                            </button>
                          </div>
                          <strong className={grant.effective ? 'granted' : ''}>
                            {grant.effective ? 'granted' : 'denied'}
                          </strong>
                        </div>
                      ))}
                    </>
                  )}
                </div>
              ) : null}
            </article>
          )
        })}
      </div>
    </>
  )
}
