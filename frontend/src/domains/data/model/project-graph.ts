export interface ProjectGraphArtifact {
  id: string
  runId: string
  kind: string
  title: string
  contentType: string
  createdAt: string
}

export interface ProjectGraphNode {
  id: string
  label: string
  degree: number
  isGod: boolean
  content: string | null
  kind: string | null
  labeled: boolean
  artifact: ProjectGraphArtifact | null
}

export interface ProjectGraphLink {
  source: string
  target: string
  confidence: string
}

export interface ProjectGraph {
  projectId: string
  nodeCount: number
  linkCount: number
  nodes: ProjectGraphNode[]
  links: ProjectGraphLink[]
}
