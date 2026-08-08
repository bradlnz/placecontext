export interface JobCodeFile {
  path: string
  content: string
}
export interface JobParameter {
  name: string
  label: string | null
  required: boolean
  type: string
  options: string[] | null
}
export interface Job {
  id: string
  projectId: string
  name: string
  description: string | null
  mapSourceKind: string
  mapImage: string | null
  mapRuntimeId: string | null
  mapSource: string | null
  mapEntrypoint: string | null
  mapFiles: JobCodeFile[]
  inputPayloads: string[]
  mapEnv: Record<string, string>
  reduceSourceKind: string | null
  reduceImage: string | null
  reduceRuntimeId: string | null
  reduceSource: string | null
  reduceEntrypoint: string | null
  reduceFiles: JobCodeFile[]
  reduceEnv: Record<string, string> | null
  concurrencyLimit: number
  successExitCodes: number[]
  partialExitCodes: number[]
  allowNetworkEgress: boolean
  allowApiInvocation: boolean
  parameters: JobParameter[]
  postJobActions: string[]
  returnType: string
  returnFileName: string | null
  retryCount: number
  retryDelaySeconds: number
  mcpConnectionIds: string[]
  createdAt: string
  updatedAt: string
}
export interface JobsTrigger {
  id: string
  jobId: string | null
  name: string
  kind: string
  enabled: boolean
  cronExpression: string | null
  eventName: string | null
}
export interface JobsPageModel {
  jobs: Job[]
  triggers: JobsTrigger[]
}
export interface JobRun {
  id: string
  jobId: string
  status: string
  startedAt: string
  finishedAt: string | null
  startedAtDisplay: string
  durationDisplay: string | null
  shardCount: number
  succeededShards: number
  partialShards: number
  failedShards: number
}
export interface JobRunShard {
  index: number
  exitCode: number
  outcome: string
  artifact: string | null
  log: string | null
}
export interface JobRunDetail {
  id: string
  jobId: string
  status: string
  startedAt: string
  finishedAt: string | null
  attemptNumber: number
  originalRunId: string | null
  shards: JobRunShard[]
}
export interface JobCodePageModel {
  job: Job
}
export interface RunJobCodeResult {
  job: Job
  run: JobRunDetail
}
export interface JobRequestBody {
  name: string
  description: string | null
  mapImage: string | null
  mapRuntimeId: string | null
  mapSource: string | null
  mapEntrypoint: string | null
  mapFiles: JobCodeFile[] | null
  inputPayloads: string[]
  mapEnv: Record<string, string>
  reduceImage: string | null
  reduceRuntimeId: string | null
  reduceSource: string | null
  reduceEntrypoint: string | null
  reduceFiles: JobCodeFile[] | null
  reduceEnv: Record<string, string> | null
  concurrencyLimit: number
  successExitCodes: number[]
  partialExitCodes: number[]
  allowNetworkEgress: boolean
  allowApiInvocation: boolean
  parameters: JobParameter[]
  postJobActions: string[]
  returnType: string
  returnFileName: string | null
  retryCount: number
  retryDelaySeconds: number
  mcpConnectionIds: string[]
}
