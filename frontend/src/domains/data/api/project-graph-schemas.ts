import { z } from 'zod'

const countSchema = z.number().int().nonnegative()

const projectGraphArtifactSchema = z.object({
  id: z.uuid(),
  runId: z.uuid(),
  kind: z.string(),
  title: z.string(),
  contentType: z.string(),
  createdAt: z.iso.datetime({ offset: true }),
})

export const projectGraphSchema = z.object({
  projectId: z.uuid(),
  nodeCount: countSchema,
  linkCount: countSchema,
  nodes: z.array(
    z.object({
      id: z.string().min(1),
      label: z.string(),
      degree: countSchema,
      isGod: z.boolean(),
      content: z.string().nullable(),
      kind: z.string().nullable(),
      labeled: z.boolean(),
      artifact: projectGraphArtifactSchema.nullable(),
    }),
  ),
  links: z.array(
    z.object({
      source: z.string().min(1),
      target: z.string().min(1),
      confidence: z.string(),
    }),
  ),
})
