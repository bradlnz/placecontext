import { z } from 'zod'

const requirementsSchema = z.object({
  markdown: z.string(),
  updatedAt: z.iso.datetime({ offset: true }).nullable(),
  updatedAtDisplay: z.string().nullable(),
})

export const projectPageContextSchema = z.object({
  overview: z.object({
    id: z.uuid(),
    name: z.string(),
    path: z.string(),
    status: z.string(),
    godNodes: z.array(
      z.object({
        id: z.string(),
        label: z.string(),
        degree: z.number().int().nonnegative(),
      }),
    ),
  }),
  timeline: z
    .object({
      changes: z.array(
        z.object({
          id: z.uuid(),
          sequence: z.number().int().nonnegative(),
          title: z.string(),
          kind: z.string(),
          commit: z.string().nullable(),
        }),
      ),
    })
    .nullable(),
  decisions: z
    .array(
      z.object({
        id: z.uuid(),
        question: z.string(),
        choice: z.string(),
        rationale: z.string(),
        decidedAt: z.iso.datetime({ offset: true }),
        decidedAtDisplay: z.string(),
      }),
    )
    .nullable(),
  requirements: requirementsSchema.nullable(),
  message: z.string().nullable(),
})

export { requirementsSchema as projectRequirementsSchema }
