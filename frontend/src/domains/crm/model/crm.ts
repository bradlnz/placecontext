export interface CrmClient {
  id: string
  projectId: string
  name: string
  company: string | null
  email: string | null
  phone: string | null
  lifecycleStage: string
  notes: string | null
  customerPortalEnabled: boolean
  customerPortalSlug: string | null
  customerPortalDomain: string | null
  customerPortalBrandName: string | null
  customerPortalLogoUrl: string | null
  createdAt: string
  updatedAt: string
}

export interface CrmAppointment {
  id: string
  projectId: string
  calendarId: string | null
  clientId: string | null
  clientName: string | null
  title: string
  startsAt: string
  endsAt: string
  location: string | null
  notes: string | null
  createdAt: string
}

export interface CrmCalendar {
  id: string
  projectId: string
  name: string
  color: string
  createdAt: string
  updatedAt: string
}

export interface CrmAutomation {
  id: string
  projectId: string
  name: string
  eventType: string
  lifecycleStage: string | null
  chainId: string
  chainName: string
  chainSteps: number
  enabled: boolean
  updatedAt: string
}

export interface CrmChain {
  id: string
  name: string
  stages: { jobs: { jobId: string; jobName: string }[] }[]
}

export interface CrmChainRun {
  id: string
  clientId: string
  chainId: string
  chainName: string
  chainRunId: string
  lifecycleStage: string
  status: string
  startedAt: string
  finishedAt: string | null
}

export interface CrmCommunication {
  id: string
  clientId: string
  channel: string
  subject: string | null
  body: string
  recipient: string | null
  status: string
  provider: string | null
  error: string | null
  createdByUserId: string
  createdAt: string
  sentAt: string | null
}

export interface CrmArtifact {
  id: string
  clientId: string
  title: string
  contentType: string
  sizeBytes: number
  source: string
  chainRunId: string | null
  createdAt: string
}

export interface CrmCapabilities {
  emailEnabled: boolean
  smsEnabled: boolean
  emailProvider: string
  smsProvider: string
}

export interface CrmPageModel {
  clients: CrmClient[]
  chains: CrmChain[]
  automations: CrmAutomation[]
  appointments: CrmAppointment[]
  calendars: CrmCalendar[]
  capabilities: CrmCapabilities
}

export interface CrmClientDetail {
  runs: CrmChainRun[]
  communications: CrmCommunication[]
  artifacts: CrmArtifact[]
  assignedChainIds: string[]
}
