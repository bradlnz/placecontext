export interface BackupManifestPreview {
  fileName: string
  manifest: Record<string, unknown>
  projectCount: number
  jobCount: number
  chainCount: number
}

export interface BackupImportResult {
  projectsCreated: number
  projectsUpdated: number
  jobsCreated: number
  jobsUpdated: number
  jobsSkipped: number
  warnings: string[]
}
