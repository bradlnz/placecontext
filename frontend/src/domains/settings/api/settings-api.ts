import { z } from 'zod'

import { getJson, postJson, putJson } from '../../../shared/api/http-client'
import type { ArtifactFilterSettings, BrandingSettings, LocalitySettings, MenuSettings, MenuSettingsItem } from '../model/settings'

const brandingSchema: z.ZodType<BrandingSettings> = z.object({
  productName: z.string().nullable(),
  logoDataUri: z.string().nullable(),
  bgColor: z.string().nullable(),
  panelColor: z.string().nullable(),
  textColor: z.string().nullable(),
  accentColor: z.string().nullable(),
})

const localitySchema: z.ZodType<LocalitySettings> = z.object({
  timeZoneId: z.string(),
  timeZones: z.array(z.string()),
})

const menuItemSchema: z.ZodType<MenuSettingsItem> = z.object({
  id: z.string(),
  defaultLabel: z.string(),
  label: z.string(),
  order: z.number().int(),
  visible: z.boolean(),
  section: z.string(),
})

const menuSchema: z.ZodType<MenuSettings> = z.object({ workspace: z.array(menuItemSchema) })
const artifactFiltersSchema: z.ZodType<ArtifactFilterSettings> = z.object({
  categories: z.array(z.object({ id: z.string(), label: z.string(), prefixes: z.array(z.string()) })),
})

export async function fetchBranding(signal: AbortSignal): Promise<BrandingSettings> {
  return getJson({ path: '/api/v1/settings/branding', schema: brandingSchema, signal })
}

export async function saveBranding(value: BrandingSettings, signal: AbortSignal): Promise<BrandingSettings> {
  return putJson({ path: '/api/v1/settings/branding', body: value, schema: brandingSchema, signal })
}

export async function resetBranding(signal: AbortSignal): Promise<BrandingSettings> {
  return postJson({ path: '/api/v1/settings/branding/reset', body: {}, schema: brandingSchema, signal })
}

export async function fetchLocality(signal: AbortSignal): Promise<LocalitySettings> {
  return getJson({ path: '/api/v1/settings/locality', schema: localitySchema, signal })
}

export async function saveLocality(timeZoneId: string, signal: AbortSignal): Promise<LocalitySettings> {
  return putJson({ path: '/api/v1/settings/locality', body: { timeZoneId }, schema: localitySchema, signal })
}

export async function fetchMenu(signal: AbortSignal): Promise<MenuSettings> {
  return getJson({ path: '/api/v1/settings/menu', schema: menuSchema, signal })
}

export async function saveMenu(workspace: MenuSettingsItem[], signal: AbortSignal): Promise<MenuSettings> {
  return putJson({ path: '/api/v1/settings/menu', body: { workspace }, schema: menuSchema, signal })
}

export async function resetMenu(signal: AbortSignal): Promise<MenuSettings> {
  return postJson({ path: '/api/v1/settings/menu/reset', body: {}, schema: menuSchema, signal })
}

export async function fetchArtifactFilters(signal: AbortSignal): Promise<ArtifactFilterSettings> {
  return getJson({ path: '/api/v1/settings/artifacts', schema: artifactFiltersSchema, signal })
}

export async function saveArtifactFilters(value: ArtifactFilterSettings, signal: AbortSignal): Promise<ArtifactFilterSettings> {
  return putJson({ path: '/api/v1/settings/artifacts', body: value, schema: artifactFiltersSchema, signal })
}

export async function resetArtifactFilters(signal: AbortSignal): Promise<ArtifactFilterSettings> {
  return postJson({ path: '/api/v1/settings/artifacts/reset', body: {}, schema: artifactFiltersSchema, signal })
}
