export type JobTestAssertion = 'Succeeds' | 'OutputEquals' | 'OutputContains' | 'JsonSubset'

export interface JobTestJob {
  id: string
  name: string
}
export interface JobTestCodeFile {
  path: string
  content: string
}
export interface JobTestMethod {
  name: string
  status: string
  durationMs: number | null
  message: string | null
}
export interface JobTestBlock {
  id: string
  projectId: string
  jobId: string
  jobName: string
  name: string
  inputPayload: string | null
  assertionType: JobTestAssertion
  expectedValue: string | null
  enabled: boolean
  lastStatus: string
  lastMessage: string | null
  lastActualOutput: string | null
  lastDurationMs: number | null
  runtimeId: string
  runtimeLabel: string
  entrypoint: string | null
  codeFiles: JobTestCodeFile[]
  methodResults: JobTestMethod[]
}
export interface JobTestsPageModel {
  jobs: JobTestJob[]
  tests: JobTestBlock[]
}
export interface JobTestRuntime {
  id: string
  label: string
  frameworkLabel: string
  entrypoint: string
  starterFiles: JobTestCodeFile[]
}
export interface JobTestCodePageModel {
  test: JobTestBlock
  runtimes: JobTestRuntime[]
}
export interface SaveJobTestBlockBody {
  jobId: string
  name: string
  inputPayload: string
  assertionType: JobTestAssertion
  expectedValue: string
  enabled: boolean
}
export interface UpdateJobTestCodeBody {
  runtimeId: string
  entrypoint: string | null
  codeFiles: JobTestCodeFile[]
}
