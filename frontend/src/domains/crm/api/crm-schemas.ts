import { z } from 'zod'

const date = z.iso.datetime({ offset: true })
export const crmClientSchema = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  name: z.string(),
  company: z.string().nullable(),
  email: z.string().nullable(),
  phone: z.string().nullable(),
  lifecycleStage: z.string(),
  notes: z.string().nullable(),
  customerPortalEnabled: z.boolean(),
  customerPortalSlug: z.string().nullable(),
  customerPortalDomain: z.string().nullable(),
  customerPortalBrandName: z.string().nullable(),
  customerPortalLogoUrl: z.string().nullable(),
  createdAt: date,
  updatedAt: date,
})
export const crmAppointmentSchema = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  calendarId: z.uuid().nullable(),
  clientId: z.uuid().nullable(),
  clientName: z.string().nullable(),
  title: z.string(),
  startsAt: date,
  endsAt: date,
  location: z.string().nullable(),
  notes: z.string().nullable(),
  createdAt: date,
})
export const crmCalendarSchema = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  name: z.string(),
  color: z.string(),
  createdAt: date,
  updatedAt: date,
})
export const crmAutomationSchema = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  name: z.string(),
  eventType: z.string(),
  lifecycleStage: z.string().nullable(),
  chainId: z.uuid(),
  chainName: z.string(),
  chainSteps: z.number().int(),
  enabled: z.boolean(),
  updatedAt: date,
})
export const crmChainSchema = z
  .object({
    id: z.uuid(),
    name: z.string(),
    stages: z.array(
      z.object({ jobs: z.array(z.object({ jobId: z.uuid(), jobName: z.string() })) }).loose(),
    ),
  })
  .loose()
export const crmChainRunSchema = z.object({
  id: z.uuid(),
  clientId: z.uuid(),
  chainId: z.uuid(),
  chainName: z.string(),
  chainRunId: z.uuid(),
  lifecycleStage: z.string(),
  status: z.string(),
  startedAt: date,
  finishedAt: date.nullable(),
})
export const crmCommunicationSchema = z.object({
  id: z.uuid(),
  clientId: z.uuid(),
  channel: z.string(),
  subject: z.string().nullable(),
  body: z.string(),
  recipient: z.string().nullable(),
  status: z.string(),
  provider: z.string().nullable(),
  error: z.string().nullable(),
  createdByUserId: z.uuid(),
  createdAt: date,
  sentAt: date.nullable(),
})
export const crmArtifactSchema = z.object({
  id: z.uuid(),
  clientId: z.uuid(),
  title: z.string(),
  contentType: z.string(),
  sizeBytes: z.number().int().nonnegative(),
  source: z.string(),
  chainRunId: z.uuid().nullable(),
  createdAt: date,
})
export const crmCapabilitiesSchema = z.object({
  emailEnabled: z.boolean(),
  smsEnabled: z.boolean(),
  emailProvider: z.string(),
  smsProvider: z.string(),
})
export const crmPageSchema = z.object({
  clients: z.array(crmClientSchema),
  chains: z.array(crmChainSchema),
  automations: z.array(crmAutomationSchema),
  appointments: z.array(crmAppointmentSchema),
  calendars: z.array(crmCalendarSchema),
  capabilities: crmCapabilitiesSchema,
})
export const crmClientDetailSchema = z.object({
  runs: z.array(crmChainRunSchema),
  communications: z.array(crmCommunicationSchema),
  artifacts: z.array(crmArtifactSchema),
  assignedChainIds: z.array(z.uuid()),
})
export const crmIngestionSettingsSchema = z.object({
  projectId: z.uuid(),
  allowedOrigin: z.string(),
  enabled: z.boolean(),
  tokenPrefix: z.string().nullable(),
  updatedAt: date.nullable(),
})
export const crmIngestionTokenSchema = z.object({
  settings: crmIngestionSettingsSchema,
  token: z.string(),
})
