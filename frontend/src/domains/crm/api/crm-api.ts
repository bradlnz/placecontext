import { z } from 'zod'

import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type { CrmClientDetail, CrmPageModel } from '../model/crm'
import {
  crmAppointmentSchema,
  crmArtifactSchema,
  crmAutomationSchema,
  crmCalendarSchema,
  crmCapabilitiesSchema,
  crmChainRunSchema,
  crmChainSchema,
  crmClientDetailSchema,
  crmClientSchema,
  crmCommunicationSchema,
  crmIngestionSettingsSchema,
  crmIngestionTokenSchema,
  crmPageSchema,
} from './crm-schemas'

const CRM = '/api/crm'

export async function fetchCrmPage(projectId: string, signal: AbortSignal): Promise<CrmPageModel> {
  const root = `${CRM}/projects/${encodeURIComponent(projectId)}`
  const [clients, chains, automations, appointments, calendars, capabilities] = await Promise.all([
    getJson({ path: `${root}/clients`, schema: z.array(crmClientSchema), signal }),
    getJson({
      path: `/api/jobs/projects/${encodeURIComponent(projectId)}/chains`,
      schema: z.array(crmChainSchema),
      signal,
    }),
    getJson({ path: `${root}/automations`, schema: z.array(crmAutomationSchema), signal }),
    getJson({ path: `${root}/appointments`, schema: z.array(crmAppointmentSchema), signal }),
    getJson({ path: `${root}/calendars`, schema: z.array(crmCalendarSchema), signal }),
    getJson({ path: `${CRM}/communication-capabilities`, schema: crmCapabilitiesSchema, signal }),
  ])
  return crmPageSchema.parse({
    clients,
    chains,
    automations,
    appointments,
    calendars,
    capabilities,
  })
}

export async function fetchCrmClientDetail(
  projectId: string,
  clientId: string,
  signal: AbortSignal,
): Promise<CrmClientDetail> {
  const client = `${CRM}/clients/${encodeURIComponent(clientId)}`
  const [runs, communications, artifacts, assignedChainIds] = await Promise.all([
    getJson({ path: `${client}/runs`, schema: z.array(crmChainRunSchema), signal }),
    getJson({ path: `${client}/communications`, schema: z.array(crmCommunicationSchema), signal }),
    getJson({ path: `${client}/artifacts`, schema: z.array(crmArtifactSchema), signal }),
    getJson({
      path: `${CRM}/projects/${encodeURIComponent(projectId)}/clients/${encodeURIComponent(clientId)}/chain-assignments`,
      schema: z.array(z.uuid()),
      signal,
    }),
  ])
  return crmClientDetailSchema.parse({ runs, communications, artifacts, assignedChainIds })
}

export const saveCrmClient = (projectId: string, body: unknown, signal: AbortSignal) =>
  postJson({ path: `${CRM}/projects/${projectId}/clients`, body, schema: crmClientSchema, signal })
export const moveCrmClient = (clientId: string, lifecycleStage: string, signal: AbortSignal) =>
  putJson({
    path: `${CRM}/clients/${clientId}/stage`,
    body: { lifecycleStage },
    schema: crmClientSchema,
    signal,
  })
export const deleteCrmClient = (clientId: string, signal: AbortSignal) =>
  deleteRequest(`${CRM}/clients/${clientId}`, signal)
export const saveCrmAppointment = (projectId: string, body: unknown, signal: AbortSignal) =>
  postJson({
    path: `${CRM}/projects/${projectId}/appointments`,
    body,
    schema: crmAppointmentSchema,
    signal,
  })
export const deleteCrmAppointment = (id: string, signal: AbortSignal) =>
  deleteRequest(`${CRM}/appointments/${id}`, signal)
export const saveCrmCalendar = (projectId: string, body: unknown, signal: AbortSignal) =>
  postJson({
    path: `${CRM}/projects/${projectId}/calendars`,
    body,
    schema: crmCalendarSchema,
    signal,
  })
export const deleteCrmCalendar = (id: string, signal: AbortSignal) =>
  deleteRequest(`${CRM}/calendars/${id}`, signal)
export const saveCrmAutomation = (projectId: string, body: unknown, signal: AbortSignal) =>
  postJson({
    path: `${CRM}/projects/${projectId}/automations`,
    body,
    schema: crmAutomationSchema,
    signal,
  })
export const setCrmAutomationEnabled = (id: string, enabled: boolean, signal: AbortSignal) =>
  putJson({
    path: `${CRM}/automations/${id}/enabled`,
    body: enabled,
    schema: crmAutomationSchema,
    signal,
  })
export const deleteCrmAutomation = (id: string, signal: AbortSignal) =>
  deleteRequest(`${CRM}/automations/${id}`, signal)
export const sendCrmCommunication = (clientId: string, body: unknown, signal: AbortSignal) =>
  postJson({
    path: `${CRM}/clients/${clientId}/communications`,
    body,
    schema: crmCommunicationSchema,
    signal,
  })
export const runCrmAutomation = (clientId: string, chainId: string, signal: AbortSignal) =>
  postJson({
    path: `${CRM}/clients/${clientId}/automation-runs`,
    body: { chainId },
    schema: crmChainRunSchema,
    signal,
  })
export const setCrmChainAssignments = (
  projectId: string,
  clientId: string,
  chainIds: string[],
  signal: AbortSignal,
) =>
  putJson({
    path: `${CRM}/clients/${clientId}/chain-assignments`,
    body: { projectId, chainIds },
    schema: z.array(z.uuid()),
    signal,
  })
export const removeCrmArtifact = (clientId: string, artifactId: string, signal: AbortSignal) =>
  deleteRequest(`${CRM}/clients/${clientId}/artifacts/${artifactId}`, signal)
export const configureCrmPortal = (clientId: string, body: unknown, signal: AbortSignal) =>
  putJson({ path: `${CRM}/clients/${clientId}/portal`, body, schema: crmClientSchema, signal })

export const fetchCrmIngestionSettings = (projectId: string, signal: AbortSignal) =>
  getJson({
    path: `${CRM}/ingestion/settings?projectId=${projectId}`,
    schema: crmIngestionSettingsSchema,
    signal,
  })
export const saveCrmIngestionOrigin = (
  projectId: string,
  allowedOrigin: string,
  signal: AbortSignal,
) =>
  putJson({
    path: `${CRM}/ingestion/settings`,
    body: { projectId, allowedOrigin },
    schema: crmIngestionSettingsSchema,
    signal,
  })
export const rotateCrmIngestionToken = (
  projectId: string,
  allowedOrigin: string,
  signal: AbortSignal,
) =>
  postJson({
    path: `${CRM}/ingestion/settings/rotate`,
    body: { projectId, allowedOrigin },
    schema: crmIngestionTokenSchema,
    signal,
  })
export const disableCrmIngestion = (projectId: string, signal: AbortSignal) =>
  deleteRequest(`${CRM}/ingestion/settings?projectId=${projectId}`, signal)
