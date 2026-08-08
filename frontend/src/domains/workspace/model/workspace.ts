export interface WorkspaceProject {
  id: string
  name: string
  path: string
  status: string
  isGraphified: boolean
  godNodeCount: number
  nodeCount: number
  linkCount: number
}

export interface WorkspaceFocusItem {
  kind: string
  severity: string
  title: string
  detail: string
  projectId: string
  project: string
  url: string
}

export interface WorkspaceFocus {
  items: WorkspaceFocusItem[]
  projectCount: number
}

export interface WorkspaceStats {
  projectCount: number
  changesToday: number
  agentChangesToday: number
  humanChangesToday: number
  godNodeTotal: number
  staleContextCount: number
}

export interface WorkspaceSession {
  displayName: string
  role: string
  tenant: string
}

export interface WorkspaceOverview {
  projects: WorkspaceProject[]
  focus: WorkspaceFocus
  stats: WorkspaceStats
  session: WorkspaceSession
}
