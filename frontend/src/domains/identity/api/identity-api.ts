import { z } from 'zod'

import { getJson } from '../../../shared/api/http-client'
import type { IdentityContext } from '../model/identity'

const identityContextSchema = z.object({
  configured: z.boolean(),
  antiforgeryFieldName: z.string().min(1),
  antiforgeryToken: z.string().min(1),
})

export async function fetchIdentityContext(signal: AbortSignal): Promise<IdentityContext> {
  return getJson({
    path: '/api/v1/identity/context',
    schema: identityContextSchema,
    signal,
  })
}
