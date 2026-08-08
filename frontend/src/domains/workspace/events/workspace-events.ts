export interface WorkspaceEventMap {
  'workspace.project-selected': {
    projectId: string
  }
  'workspace.overview-refresh-requested': {
    source: 'overview-header'
  }
  'workspace.overview-refreshed': {
    projectCount: number
  }
}
