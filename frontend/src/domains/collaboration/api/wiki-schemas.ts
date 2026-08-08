import { z } from 'zod'

const wikiArticleSummarySchema = z.object({
  slug: z.string().min(1),
  title: z.string().min(1),
  summary: z.string(),
})

export const wikiContextSchema = z.object({
  articles: z.array(wikiArticleSummarySchema),
  article: wikiArticleSummarySchema.extend({ html: z.string() }).nullable(),
})
