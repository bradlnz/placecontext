export interface ScheduleTarget {
  id: string
  name: string
}
export interface ScheduleTrigger {
  id: string
  name: string
  kind: string
  enabled: boolean
  cronExpression: string | null
  eventName: string | null
  jobId: string | null
  chainId: string | null
  sourceTable: string | null
  prompt: string | null
  targetLabel: string
  nextRunLabel: string
  lastFiredLabel: string
}
export interface SchedulePageModel {
  timeZoneId: string
  jobs: ScheduleTarget[]
  chains: ScheduleTarget[]
  tables: string[]
  eventTypes: string[]
  triggers: ScheduleTrigger[]
}
