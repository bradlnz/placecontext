export interface AccessMember {
  id: string
  email: string
  displayName: string
  role: string
  isDefaultAdmin: boolean
  createdAt: string
}

export interface AccessRole {
  id: string
  name: string
  isSystem: boolean
  permissions: string[]
  memberCount: number
}

export interface PermissionGrant {
  permission: string
  defaultAllowed: boolean
  override: boolean | null
  effective: boolean
}

export interface UserPermissions {
  userId: string
  role: string
  permissions: PermissionGrant[]
}

export interface AccessSettings {
  members: AccessMember[]
  roles: AccessRole[]
  permissions: string[]
  customerPortalEnabled: boolean
  currentUserId: string
}

export interface MemberInvite {
  email: string
  inviteLink: string
}
