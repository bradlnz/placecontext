import { z } from 'zod'

import { deleteJson, getJson, postJson } from '../../../shared/api/http-client'
import type { ApiToken, CreatedApiToken } from '../model/api-token'

const apiTokenSchema = z.object({
  id: z.uuid(),
  name: z.string(),
  tokenPrefix: z.string(),
  createdAt: z.iso.datetime({ offset: true }),
  lastUsedAt: z.iso.datetime({ offset: true }).nullable(),
  expiresAt: z.iso.datetime({ offset: true }).nullable(),
})
const createdApiTokenSchema: z.ZodType<CreatedApiToken> = apiTokenSchema.extend({
  rawToken: z.string(),
})
const apiTokenListSchema: z.ZodType<ApiToken[]> = z.array(apiTokenSchema)
const revocationSchema = z.object({ revoked: z.literal(true) })

export async function fetchApiTokens(signal: AbortSignal): Promise<ApiToken[]> {
  return getJson({ path: '/api/v1/settings/api-tokens', schema: apiTokenListSchema, signal })
}

export async function createApiToken(
  name: string,
  lifetimeDays: number,
  signal: AbortSignal,
): Promise<CreatedApiToken> {
  return postJson({
    path: '/api/v1/settings/api-tokens',
    body: { name, lifetimeDays },
    schema: createdApiTokenSchema,
    signal,
  })
}

export async function revokeApiToken(tokenId: string, signal: AbortSignal): Promise<void> {
  await deleteJson({
    path: `/api/v1/settings/api-tokens/${tokenId}`,
    schema: revocationSchema,
    signal,
  })
}
