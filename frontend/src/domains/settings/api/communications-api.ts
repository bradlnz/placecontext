import { z } from 'zod'

import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type {
  CommunicationProvider,
  CommunicationProviderInput,
  CommunicationSecret,
  CommunicationsSettings,
} from '../model/communications'

const providerSchema: z.ZodType<CommunicationProvider> = z.object({
  id: z.uuid(),
  channel: z.enum(['email', 'sms']),
  kind: z.enum(['postmark', 'sendgrid', 'twilio']),
  name: z.string(),
  enabled: z.boolean(),
  isDefault: z.boolean(),
  useForTwoFactor: z.boolean(),
  authType: z.enum(['none', 'bearer', 'header', 'basic']),
  authHeaderName: z.string().nullable(),
  vaultProjectId: z.uuid().nullable(),
  apiKeySecretName: z.string().nullable(),
  settingsJson: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
})
const contextSchema: z.ZodType<CommunicationsSettings> = z.object({
  providers: z.array(providerSchema),
  projects: z.array(z.object({ id: z.uuid(), name: z.string() }).loose()),
})
const secretsSchema: z.ZodType<CommunicationSecret[]> = z.array(
  z.object({ name: z.string(), createdAt: z.string() }),
)
const testResultSchema = z.object({
  provider: z.string(),
  externalId: z.string().nullable().optional(),
})

const ROOT = '/api/v1/settings/communications'

export async function fetchCommunications(signal: AbortSignal): Promise<CommunicationsSettings> {
  return getJson({ path: `${ROOT}/context`, schema: contextSchema, signal })
}

export async function fetchCommunicationSecrets(
  projectId: string,
  signal: AbortSignal,
): Promise<CommunicationSecret[]> {
  return getJson({ path: `${ROOT}/projects/${projectId}/secrets`, schema: secretsSchema, signal })
}

export async function createCommunicationProvider(
  input: CommunicationProviderInput,
  signal: AbortSignal,
): Promise<CommunicationProvider> {
  return postJson({ path: `${ROOT}/providers`, body: input, schema: providerSchema, signal })
}

export async function updateCommunicationProvider(
  id: string,
  input: CommunicationProviderInput,
  signal: AbortSignal,
): Promise<CommunicationProvider> {
  return putJson({ path: `${ROOT}/providers/${id}`, body: input, schema: providerSchema, signal })
}

export async function deleteCommunicationProvider(id: string, signal: AbortSignal): Promise<void> {
  await deleteRequest(`${ROOT}/providers/${id}`, signal)
}

export async function setDefaultCommunicationProvider(
  id: string,
  signal: AbortSignal,
): Promise<CommunicationProvider> {
  return postJson({
    path: `${ROOT}/providers/${id}/default`,
    body: {},
    schema: providerSchema,
    signal,
  })
}

export async function setCommunicationProviderTwoFactor(
  id: string,
  enabled: boolean,
  signal: AbortSignal,
): Promise<CommunicationProvider> {
  return postJson({
    path: `${ROOT}/providers/${id}/two-factor`,
    body: { enabled },
    schema: providerSchema,
    signal,
  })
}

export async function sendCommunicationProviderTest(
  id: string,
  recipient: string,
  signal: AbortSignal,
): Promise<string> {
  const result = await postJson({
    path: `${ROOT}/providers/${id}/test`,
    body: { recipient },
    schema: testResultSchema,
    signal,
  })
  return result.provider
}
