export interface ChainJob {
  id: string
  name: string
}
export interface ChainGate {
  type: string
  durationSeconds: number | null
  expression: string | null
}
export interface ChainAction {
  type: string
  displayName: string
  recipient: string | null
  recipientName: string | null
  subject: string | null
  body: string | null
  attachmentPath: string | null
}
export interface ChainStage {
  jobs: ChainJob[]
  gate: ChainGate | null
  action: ChainAction | null
}
export interface JobChain {
  id: string
  projectId: string
  name: string
  description: string | null
  stages: ChainStage[]
  updatedAt: string
  updatedAtDisplay: string
}
export interface JobChainsPageModel {
  jobs: ChainJob[]
  chains: JobChain[]
  canSendEmail: boolean
  canSendSms: boolean
}
export interface ChainRunStep {
  index: number
  stageIndex: number
  branchIndex: number
  jobId: string
  jobName: string
  runId: string | null
  status: string
  startedAt: string | null
  finishedAt: string | null
  actionType: string | null
  provider: string | null
  externalId: string | null
  error: string | null
}
export interface ChainRun {
  id: string
  chainId: string
  chainName: string
  status: string
  steps: ChainRunStep[]
  finalOutput: string | null
  startedAt: string
  finishedAt: string | null
  startedAtDisplay: string
  durationDisplay: string | null
}
export interface SaveChainBody {
  name: string
  description: string | null
  stages: {
    jobIds: string[]
    gate: ChainGate | null
    action: ChainAction | null
  }[]
}
