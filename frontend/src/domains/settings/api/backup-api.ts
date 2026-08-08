import { z } from 'zod'

import { postJson } from '../../../shared/api/http-client'
import type { BackupImportResult, BackupManifestPreview } from '../model/backup'

const backupManifestSchema = z.looseObject({
  projects: z.array(z.unknown()),
  jobs: z.array(z.unknown()),
  jobChains: z.array(z.unknown()),
})

const backupImportResultSchema = z.looseObject({
  projectsCreated: z.number().int().nonnegative(),
  projectsUpdated: z.number().int().nonnegative(),
  jobsCreated: z.number().int().nonnegative(),
  jobsUpdated: z.number().int().nonnegative(),
  jobsSkipped: z.number().int().nonnegative(),
  warnings: z.array(z.string()),
})

export async function readBackupManifest(file: File): Promise<BackupManifestPreview> {
  if (file.size > 20 * 1024 * 1024)
    throw new Error('File too large — a manifest should be well under 20 MB.')
  const parsed: unknown = JSON.parse(await file.text())
  const manifest = backupManifestSchema.parse(parsed)

  return {
    fileName: file.name,
    manifest,
    projectCount: manifest.projects.length,
    jobCount: manifest.jobs.length,
    chainCount: manifest.jobChains.length,
  }
}

export async function importBackupManifest(
  manifest: Record<string, unknown>,
  signal: AbortSignal,
): Promise<BackupImportResult> {
  return postJson({
    path: '/api/v1/settings/backup/imports',
    body: manifest,
    schema: backupImportResultSchema,
    signal,
  })
}
