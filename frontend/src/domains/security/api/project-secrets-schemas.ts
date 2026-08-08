import { z } from 'zod'

export const projectSecretSchema = z.object({
  name: z.string().min(1),
  createdAt: z.iso.datetime({ offset: true }),
  createdAtDisplay: z.string().min(1),
})

export const projectSecretsSchema = z.array(projectSecretSchema)
