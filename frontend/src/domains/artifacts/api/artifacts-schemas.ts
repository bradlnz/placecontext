import { z } from 'zod'

export const artifactFileSchema = z.object({
  id: z.uuid(),
  runId: z.uuid(),
  jobId: z.uuid(),
  projectId: z.uuid(),
  kind: z.string(),
  title: z.string(),
  contentType: z.string(),
  sizeBytes: z.number().int().nonnegative(),
  createdAt: z.iso.datetime({ offset: true }),
})

export const artifactShareStatusSchema = z.object({
  isActive: z.boolean(),
  tokenPrefix: z.string(),
  createdAt: z.iso.datetime({ offset: true }),
  expiresAt: z.iso.datetime({ offset: true }),
  lastAccessedAt: z.iso.datetime({ offset: true }).nullable(),
})

export const artifactShareCreatedSchema = z.object({
  token: z.string(),
  tokenPrefix: z.string(),
  expiresAt: z.iso.datetime({ offset: true }),
})

export const artifactsPageSchema = z.object({
  files: z.array(artifactFileSchema),
  projects: z.array(z.object({ id: z.uuid(), name: z.string() })),
  config: z.object({
    categories: z.array(
      z.object({ id: z.string(), label: z.string(), prefixes: z.array(z.string()) }),
    ),
  }),
  canDelete: z.boolean(),
  canShare: z.boolean(),
  canManageSettings: z.boolean(),
  loadMayBeIncomplete: z.boolean(),
})

export const artifactCapabilitiesSchema = z.object({
  canDelete: z.boolean(),
  canShare: z.boolean(),
  canManageSettings: z.boolean(),
})

export const deleteArtifactsResultSchema = z.object({ deleted: z.number().int().nonnegative() })
