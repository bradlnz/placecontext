import type { CommunicationProviderInput } from '../model/communications'
import type { ArtifactFilter, BrandingSettings, MenuSettingsItem } from '../model/settings'

export interface SettingsEventMap {
  'settings.branding-save-requested': BrandingEventPayload
  'settings.branding-reset-requested': Record<string, never>
  'settings.branding-saved': BrandingEventPayload
  'settings.locality-save-requested': { timeZoneId: string }
  'settings.locality-saved': { timeZoneId: string }
  'settings.menu-save-requested': MenuEventPayload
  'settings.menu-reset-requested': Record<string, never>
  'settings.menu-saved': MenuEventPayload
  'settings.artifact-filters-save-requested': ArtifactFiltersEventPayload
  'settings.artifact-filters-reset-requested': Record<string, never>
  'settings.artifact-filters-saved': ArtifactFiltersEventPayload
  'settings.api-token-create-requested': { name: string; lifetimeDays: number }
  'settings.api-token-created': { tokenId: string }
  'settings.api-token-revoke-requested': { tokenId: string }
  'settings.api-token-revoked': { tokenId: string }
  'settings.communication-save-requested': {
    providerId: string | null
    input: CommunicationProviderInput
  }
  'settings.communication-action-requested': CommunicationAction
  'settings.communication-changed': { providerId: string }
  'settings.connections-changed': { projectId: string }
  'settings.mcp-connections-changed': { projectId: string }
  'settings.access-changed': {
    scope: 'member' | 'permission' | 'portal' | 'role'
  }
  'settings.backup-import-requested': {
    manifest: Record<string, unknown>
  }
  'settings.backup-imported': {
    projectsCreated: number
    jobsCreated: number
  }
}

interface BrandingEventPayload {
  branding: BrandingSettings
}

interface MenuEventPayload {
  workspace: MenuSettingsItem[]
}

interface ArtifactFiltersEventPayload {
  categories: ArtifactFilter[]
}

export type CommunicationAction =
  | { kind: 'delete'; providerId: string }
  | { kind: 'default'; providerId: string }
  | { kind: 'toggle-enabled'; providerId: string; enabled: boolean }
  | { kind: 'two-factor'; providerId: string; enabled: boolean }
  | { kind: 'test'; providerId: string; recipient: string }
