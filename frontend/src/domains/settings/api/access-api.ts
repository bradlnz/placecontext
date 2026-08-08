import { z } from 'zod'

import {
  deleteRequest,
  getJson,
  postJson,
  putJson,
  putRequest,
} from '../../../shared/api/http-client'
import type { AccessRole, AccessSettings, MemberInvite, UserPermissions } from '../model/access'

const memberSchema = z.object({
  id: z.uuid(),
  email: z.string(),
  displayName: z.string(),
  role: z.string(),
  isDefaultAdmin: z.boolean(),
  createdAt: z.string(),
})
const roleSchema: z.ZodType<AccessRole> = z.object({
  id: z.uuid(),
  name: z.string(),
  isSystem: z.boolean(),
  permissions: z.array(z.string()),
  memberCount: z.number().int().nonnegative(),
})
const permissionGrantSchema = z.object({
  permission: z.string(),
  defaultAllowed: z.boolean(),
  override: z.boolean().nullable(),
  effective: z.boolean(),
})
const userPermissionsSchema: z.ZodType<UserPermissions> = z.object({
  userId: z.uuid(),
  role: z.string(),
  permissions: z.array(permissionGrantSchema),
})
const accessSettingsSchema: z.ZodType<AccessSettings> = z.object({
  members: z.array(memberSchema),
  roles: z.array(roleSchema),
  permissions: z.array(z.string()),
  customerPortalEnabled: z.boolean(),
  currentUserId: z.uuid(),
})
const messageSchema = z.object({ message: z.string() })
const memberInviteSchema: z.ZodType<MemberInvite> = z.object({
  email: z.string(),
  inviteLink: z.string(),
})
const ROOT = '/api/v1/settings/access'

export async function fetchAccessSettings(signal: AbortSignal): Promise<AccessSettings> {
  return getJson({
    path: `${ROOT}/context`,
    schema: accessSettingsSchema,
    signal,
  })
}
export async function setCustomerPortalEnabled(
  enabled: boolean,
  signal: AbortSignal,
): Promise<string> {
  return (
    await putJson({
      path: `${ROOT}/customer-portal`,
      body: { enabled },
      schema: messageSchema,
      signal,
    })
  ).message
}
export async function inviteCustomerPortalUser(
  email: string,
  role: string,
  signal: AbortSignal,
): Promise<string> {
  return (
    await postJson({
      path: `${ROOT}/customer-portal/invitations`,
      body: { email, role },
      schema: messageSchema,
      signal,
    })
  ).message
}
export async function inviteMember(
  email: string,
  role: string,
  signal: AbortSignal,
): Promise<MemberInvite> {
  return postJson({
    path: `${ROOT}/invitations`,
    body: { email, role },
    schema: memberInviteSchema,
    signal,
  })
}
export async function setMemberRole(
  userId: string,
  role: string,
  signal: AbortSignal,
): Promise<void> {
  await putRequest(`${ROOT}/members/${userId}/role`, { role }, signal)
}
export async function deleteMember(userId: string, signal: AbortSignal): Promise<void> {
  await deleteRequest(`${ROOT}/members/${userId}`, signal)
}
export async function fetchMemberPermissions(
  userId: string,
  signal: AbortSignal,
): Promise<UserPermissions> {
  return getJson({
    path: `${ROOT}/members/${userId}/permissions`,
    schema: userPermissionsSchema,
    signal,
  })
}
export async function setMemberPermission(
  userId: string,
  permission: string,
  allowed: boolean | null,
  signal: AbortSignal,
): Promise<UserPermissions> {
  return putJson({
    path: `${ROOT}/members/${userId}/permission`,
    body: { permission, allowed },
    schema: userPermissionsSchema,
    signal,
  })
}
export async function createAccessRole(
  name: string,
  permissions: string[],
  signal: AbortSignal,
): Promise<AccessRole> {
  return postJson({
    path: `${ROOT}/roles`,
    body: { name, permissions },
    schema: roleSchema,
    signal,
  })
}
export async function updateAccessRole(
  roleId: string,
  permissions: string[],
  signal: AbortSignal,
): Promise<AccessRole> {
  return putJson({
    path: `${ROOT}/roles/${roleId}/permissions`,
    body: { permissions },
    schema: roleSchema,
    signal,
  })
}
export async function deleteAccessRole(roleId: string, signal: AbortSignal): Promise<void> {
  await deleteRequest(`${ROOT}/roles/${roleId}`, signal)
}
