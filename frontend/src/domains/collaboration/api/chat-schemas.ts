import { z } from 'zod'

export const chatMessageSchema = z.object({
  role: z.string(),
  content: z.string(),
  timestamp: z.iso.datetime({ offset: true }),
})

export const chatSessionSchema = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  userId: z.uuid().nullable(),
  title: z.string().nullable(),
  messages: z.array(chatMessageSchema),
  createdAt: z.iso.datetime({ offset: true }),
  updatedAt: z.iso.datetime({ offset: true }),
})

export const chatConfigSchema = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  baseModel: z.string(),
  systemPrompt: z.string(),
  preamble: z.string(),
  toolCatalog: z.string(),
  launchpadToolCatalog: z.string(),
  maxContextChunks: z.number().int().positive(),
  temperature: z.number(),
  topP: z.number(),
  enabled: z.boolean(),
  createdAt: z.iso.datetime({ offset: true }),
  updatedAt: z.iso.datetime({ offset: true }),
})

export const chatPageSchema = z.object({
  config: chatConfigSchema,
  sessions: z.array(chatSessionSchema),
})
