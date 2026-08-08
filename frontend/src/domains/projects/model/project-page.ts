export interface ProjectGodNode {
  id: string
  label: string
  degree: number
}
export interface ProjectOverview {
  id: string
  name: string
  path: string
  status: string
  godNodes: ProjectGodNode[]
}
export interface ProjectChange {
  id: string
  sequence: number
  title: string
  kind: string
  commit: string | null
}
export interface ProjectDecision {
  id: string
  question: string
  choice: string
  rationale: string
  decidedAt: string
  decidedAtDisplay: string
}
export interface ProjectRequirements {
  markdown: string
  updatedAt: string | null
  updatedAtDisplay: string | null
}
export interface ProjectPageContext {
  overview: ProjectOverview
  timeline: { changes: ProjectChange[] } | null
  decisions: ProjectDecision[] | null
  requirements: ProjectRequirements | null
  message: string | null
}
