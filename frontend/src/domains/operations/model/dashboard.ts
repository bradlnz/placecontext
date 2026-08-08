export interface DashboardProject {
  id: string
  name: string
}

export interface DashboardStatsModel {
  running: number
  queued: number
  failed24: number
  succeeded24: number
}

export interface DashboardParameter {
  name: string
  label: string
  required: boolean
  type: string
  options: string[]
  defaultValue: string
}

export interface DashboardChainStep {
  index: number
  jobName: string
  parameters: DashboardParameter[]
}

export interface DashboardChain {
  id: string
  projectId: string
  name: string
  stageCount: number
  jobCount: number
  promptSteps: DashboardChainStep[]
}

export interface DashboardEntityBar {
  label: string
  count: number
  percentage: number
}

export interface DashboardEntity {
  id: string
  projectId: string
  name: string
  tableName: string
  rowCount: number | null
  chartColumn: string | null
  bars: DashboardEntityBar[]
}

export interface DashboardChart {
  name: string
  spec: Record<string, unknown>
  generatedAt: string
}

export interface DashboardRun {
  id: string
  jobName: string
  projectName: string
  status: string
  succeededShards: number
  failedShards: number
  startedAt: string
  finishedAt: string | null
  sourceKind: string
}

export interface Dashboard {
  project: DashboardProject | null
  stats: DashboardStatsModel
  chains: DashboardChain[]
  entities: DashboardEntity[]
  charts: DashboardChart[]
  recentRuns: DashboardRun[]
}

export interface RunDashboardChainCommand {
  projectId: string
  chainId: string
  inputPayload: string | null
  stepPayloadOverrides: Record<number, string> | null
}

export interface RunDashboardChainResult {
  chainRunId: string
  message: string
}
