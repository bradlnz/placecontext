export interface ArtifactFile {
  id: string
  runId: string
  jobId: string
  projectId: string
  kind: string
  title: string
  contentType: string
  sizeBytes: number
  createdAt: string
}

export interface ArtifactProject {
  id: string
  name: string
}

export interface ArtifactCategory {
  id: string
  label: string
  prefixes: string[]
}

export interface ArtifactsPageModel {
  files: ArtifactFile[]
  projects: ArtifactProject[]
  config: { categories: ArtifactCategory[] }
  canDelete: boolean
  canShare: boolean
  canManageSettings: boolean
  loadMayBeIncomplete: boolean
}

export interface ArtifactShareStatus {
  isActive: boolean
  tokenPrefix: string
  createdAt: string
  expiresAt: string
  lastAccessedAt: string | null
}

export interface ArtifactShareCreated {
  token: string
  tokenPrefix: string
  expiresAt: string
}
